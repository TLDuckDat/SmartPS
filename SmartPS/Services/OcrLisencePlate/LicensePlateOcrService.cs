using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using SmartPS.Models.Ocr;
using SmartPS.Services.Localization;

namespace SmartPS.Services.OcrLisencePlate;

/// <summary>
/// Gọi LicensePlateEngine.exe (CLI ONNX ALPR) cho từng ảnh: <c>LicensePlateEngine.exe --input &lt;ảnh&gt;</c>.
/// Engine ghi kết quả vào thư mục <c>&lt;tên ảnh&gt;_&lt;thời gian&gt;/</c> cạnh exe và in đường dẫn ra stdout
/// (<c>JSON: ...</c>, <c>Annotated: ...</c>); service chuyển thư mục đó sang thư mục tạm của ứng dụng rồi đọc JSON.
/// </summary>
public class LicensePlateOcrService : IOcrLicensePlateService
{
    private const string EngineFileName = "LicensePlateEngine.exe";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ResultRetention = TimeSpan.FromDays(1);

    // Engine nạp mô hình ONNX mỗi lần chạy và tốn nhiều CPU/RAM, nên mỗi lúc chỉ chạy một tiến trình
    private readonly SemaphoreSlim _runLock = new(1, 1);
    private readonly string _resultsRoot;

    private Process? _process;
    private bool _isDisposed = false;

    public bool IsRunning => _process != null;
    public string? EngineExecutablePath { get; private set; }

    public LicensePlateOcrService()
    {
        EngineExecutablePath = ResolveEnginePath();
        _resultsRoot = Path.Combine(Path.GetTempPath(), "SmartPS", "ocr");
    }

    public static string? ResolveEnginePath()
    {
        var candidates = new List<string>();

        // 1. Theo AppContext.BaseDirectory
        var baseDir = AppContext.BaseDirectory;
        candidates.Add(Path.Combine(baseDir, "Services", "OcrLisencePlate", EngineFileName));

        // 2. Tìm ngược lên các thư mục cha từ BaseDirectory (phục vụ lúc chạy Debug/Release trong bin)
        var dirInfo = new DirectoryInfo(baseDir);
        for (int i = 0; i < 5 && dirInfo != null; i++)
        {
            candidates.Add(Path.Combine(dirInfo.FullName, "Services", "OcrLisencePlate", EngineFileName));
            dirInfo = dirInfo.Parent;
        }

        // 3. Theo CurrentDirectory
        var curDir = Directory.GetCurrentDirectory();
        candidates.Add(Path.Combine(curDir, "Services", "OcrLisencePlate", EngineFileName));

        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                return Path.GetFullPath(path);
            }
        }

        return null;
    }

    /// <summary>Engine dạng CLI không cần khởi động trước; chỉ kiểm tra exe tồn tại và dọn kết quả cũ.</summary>
    public Task<bool> EnsureEngineStartedAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(EngineExecutablePath) || !File.Exists(EngineExecutablePath))
        {
            EngineExecutablePath = ResolveEnginePath();
        }

        var available = !string.IsNullOrEmpty(EngineExecutablePath) && File.Exists(EngineExecutablePath);
        if (!available)
        {
            Debug.WriteLine($"[LicensePlateOcrService Error]: Không tìm thấy {EngineFileName}");
        }
        else
        {
            PruneOldResults();
        }

        return Task.FromResult(available);
    }

    public async Task<OcrPlateResponse> RecognizePlateAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return CreateErrorResponse("EMPTY_PATH", AppText.Get("Msg_Ocr_EmptyPath"));
        }

        var fullPath = Path.GetFullPath(imagePath);
        if (!File.Exists(fullPath))
        {
            return CreateErrorResponse("FILE_NOT_FOUND", AppText.Get("Msg_Ocr_FileNotFound", fullPath));
        }

        if (!await EnsureEngineStartedAsync(cancellationToken))
        {
            return CreateErrorResponse("ENGINE_NOT_READY", AppText.Get("Msg_Ocr_EngineNotReady", EngineFileName));
        }

        await _runLock.WaitAsync(cancellationToken);
        try
        {
            return await RunEngineAsync(EngineExecutablePath!, fullPath, cancellationToken);
        }
        finally
        {
            _runLock.Release();
        }
    }

    private async Task<OcrPlateResponse> RunEngineAsync(string enginePath, string imagePath, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var startInfo = new ProcessStartInfo
        {
            FileName = enginePath,
            WorkingDirectory = Path.GetDirectoryName(enginePath) ?? string.Empty,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--input");
        startInfo.ArgumentList.Add(imagePath);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return CreateErrorResponse("ENGINE_START_FAILED", AppText.Get("Msg_Ocr_EngineStartFailed", EngineFileName));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LicensePlateOcrService Exception during startup]: {ex.Message}");
            return CreateErrorResponse("ENGINE_START_FAILED", AppText.Get("Msg_Ocr_EngineStartFailedDetail", EngineFileName, ex.Message));
        }

        _process = process;
        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            using var timeoutCts = new CancellationTokenSource(RequestTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);
            try
            {
                await process.WaitForExitAsync(linkedCts.Token);
            }
            catch (OperationCanceledException)
            {
                KillQuietly(process);
                cancellationToken.ThrowIfCancellationRequested();
                return CreateErrorResponse("TIMEOUT", AppText.Get("Msg_Ocr_Timeout", RequestTimeout.TotalSeconds));
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            stopwatch.Stop();

            var (jsonPath, annotatedPath) = ParseOutputPaths(stdout);
            if (process.ExitCode != 0 || jsonPath == null || !File.Exists(jsonPath))
            {
                var reason = LastNonEmptyLine(stderr) ?? LastNonEmptyLine(stdout) ?? AppText.Get("Str_Ocr_ExitCode", process.ExitCode);
                Debug.WriteLine($"[LicensePlateEngine STDERR]: {stderr}");
                return CreateErrorResponse("ENGINE_FAILED", AppText.Get("Msg_Ocr_EngineFailed", reason));
            }

            (jsonPath, annotatedPath) = MoveResultFolder(jsonPath, annotatedPath);
            var json = await File.ReadAllTextAsync(jsonPath, cancellationToken);
            return ParseEngineResult(json, annotatedPath, stopwatch.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LicensePlateOcrService Exception]: {ex.Message}");
            return CreateErrorResponse("REQUEST_EXCEPTION", AppText.Get("Msg_Ocr_RequestError", ex.Message));
        }
        finally
        {
            _process = null;
        }
    }

    /// <summary>Đọc đường dẫn JSON và ảnh annotated mà engine in ra stdout.</summary>
    internal static (string? JsonPath, string? AnnotatedPath) ParseOutputPaths(string stdout)
    {
        string? jsonPath = null;
        string? annotatedPath = null;

        foreach (var rawLine in stdout.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("JSON:", StringComparison.OrdinalIgnoreCase))
            {
                jsonPath = line["JSON:".Length..].Trim();
            }
            else if (line.StartsWith("Annotated:", StringComparison.OrdinalIgnoreCase))
            {
                annotatedPath = line["Annotated:".Length..].Trim();
            }
        }

        return (string.IsNullOrEmpty(jsonPath) ? null : jsonPath, string.IsNullOrEmpty(annotatedPath) ? null : annotatedPath);
    }

    /// <summary>
    /// Chuyển JSON của engine (<c>{"status":"ok","vehicles":[{"license":"30K-555.55","confidence":0.82,"status":"ok"}]}</c>)
    /// sang <see cref="OcrPlateResponse"/>; biển số tin cậy nhất đứng đầu.
    /// </summary>
    internal static OcrPlateResponse ParseEngineResult(string json, string? annotatedPath, double elapsedMs)
    {
        var root = JsonNode.Parse(json);
        var plates = new List<OcrPlateItem>();

        if (root?["vehicles"] is JsonArray vehicles)
        {
            foreach (var vehicle in vehicles)
            {
                var license = vehicle?["license"]?.GetValue<string>()?.Trim();
                var status = vehicle?["status"]?.GetValue<string>();
                if (string.IsNullOrEmpty(license) || !string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var confidence = vehicle?["confidence"]?.GetValue<double>() ?? 0;
                plates.Add(new OcrPlateItem
                {
                    PlateText = license,
                    OcrConfidence = confidence,
                    DetectionConfidence = confidence
                });
            }
        }

        if (plates.Count == 0)
        {
            var response = CreateErrorResponse("NO_PLATE", AppText.Get("Str_GateUi_NoPlateDetected"));
            response.OutputImage = annotatedPath;
            response.Processing = new OcrProcessingInfo { TotalMs = elapsedMs };
            return response;
        }

        plates.Sort((a, b) => b.OcrConfidence.CompareTo(a.OcrConfidence));
        return new OcrPlateResponse
        {
            Status = "success",
            InputType = "image",
            OutputImage = annotatedPath,
            Processing = new OcrProcessingInfo { TotalMs = elapsedMs },
            Count = plates.Count,
            Plates = plates
        };
    }

    /// <summary>Chuyển thư mục kết quả ra khỏi thư mục engine để không làm bẩn mã nguồn/thư mục cài đặt.</summary>
    private (string JsonPath, string? AnnotatedPath) MoveResultFolder(string jsonPath, string? annotatedPath)
    {
        try
        {
            var sourceDir = Path.GetDirectoryName(jsonPath);
            if (string.IsNullOrEmpty(sourceDir))
            {
                return (jsonPath, annotatedPath);
            }

            Directory.CreateDirectory(_resultsRoot);
            var targetDir = Path.Combine(_resultsRoot, $"{Path.GetFileName(sourceDir)}_{Guid.NewGuid():N}");
            Directory.Move(sourceDir, targetDir);

            var movedJson = Path.Combine(targetDir, Path.GetFileName(jsonPath));
            var movedAnnotated = annotatedPath == null ? null : Path.Combine(targetDir, Path.GetFileName(annotatedPath));
            return (movedJson, movedAnnotated);
        }
        catch (Exception ex)
        {
            // Khác ổ đĩa hoặc bị khóa: vẫn dùng kết quả tại chỗ
            Debug.WriteLine($"[LicensePlateOcrService Move Result Warning]: {ex.Message}");
            return (jsonPath, annotatedPath);
        }
    }

    private void PruneOldResults()
    {
        try
        {
            if (!Directory.Exists(_resultsRoot)) return;

            var cutoff = DateTime.UtcNow - ResultRetention;
            foreach (var dir in Directory.EnumerateDirectories(_resultsRoot))
            {
                if (Directory.GetCreationTimeUtc(dir) < cutoff)
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LicensePlateOcrService Prune Warning]: {ex.Message}");
        }
    }

    private static string? LastNonEmptyLine(string text)
    {
        return text.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0);
    }

    private static void KillQuietly(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LicensePlateOcrService Kill Warning]: {ex.Message}");
        }
    }

    private static OcrPlateResponse CreateErrorResponse(string code, string message)
    {
        return new OcrPlateResponse
        {
            Status = "error",
            Error = new OcrErrorInfo { Code = code, Message = message }
        };
    }

    public Task StopEngineAsync()
    {
        var process = _process;
        if (process != null)
        {
            KillQuietly(process);
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        await StopEngineAsync();
        _runLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
