using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using SmartPS.Models.Ocr;

namespace SmartPS.Services.OcrLisencePlate;

public class LicensePlateOcrService : IOcrLicensePlateService
{
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<OcrPlateResponse>> _pendingRequests = new();
    
    private Process? _process;
    private TaskCompletionSource<bool>? _readyTcs;
    private long _requestCounter = 0;
    private bool _isDisposed = false;

    public bool IsRunning => _process != null && !_process.HasExited;
    public string? EngineExecutablePath { get; private set; }

    public LicensePlateOcrService()
    {
        EngineExecutablePath = ResolveEnginePath();
    }

    public static string? ResolveEnginePath()
    {
        var candidates = new List<string>();

        // 1. Theo AppContext.BaseDirectory
        var baseDir = AppContext.BaseDirectory;
        candidates.Add(Path.Combine(baseDir, "Services", "OcrLisencePlate", "LicensePlateEngine.exe"));

        // 2. Tìm ngược lên các thư mục cha từ BaseDirectory (phục vụ lúc chạy Debug/Release trong bin)
        var dirInfo = new DirectoryInfo(baseDir);
        for (int i = 0; i < 5 && dirInfo != null; i++)
        {
            candidates.Add(Path.Combine(dirInfo.FullName, "SmartPS", "Services", "OcrLisencePlate", "LicensePlateEngine.exe"));
            candidates.Add(Path.Combine(dirInfo.FullName, "Services", "OcrLisencePlate", "LicensePlateEngine.exe"));
            dirInfo = dirInfo.Parent;
        }

        // 3. Theo CurrentDirectory
        var curDir = Directory.GetCurrentDirectory();
        candidates.Add(Path.Combine(curDir, "SmartPS", "Services", "OcrLisencePlate", "LicensePlateEngine.exe"));
        candidates.Add(Path.Combine(curDir, "Services", "OcrLisencePlate", "LicensePlateEngine.exe"));

        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                return Path.GetFullPath(path);
            }
        }

        return null;
    }

    public async Task<bool> EnsureEngineStartedAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning && _readyTcs != null && _readyTcs.Task.IsCompletedSuccessfully)
        {
            return true;
        }

        await _startLock.WaitAsync(cancellationToken);
        try
        {
            if (IsRunning && _readyTcs != null && _readyTcs.Task.IsCompletedSuccessfully)
            {
                return true;
            }

            // Dọn dẹp tiến trình cũ nếu có
            CleanupProcess();

            if (string.IsNullOrEmpty(EngineExecutablePath) || !File.Exists(EngineExecutablePath))
            {
                EngineExecutablePath = ResolveEnginePath();
                if (string.IsNullOrEmpty(EngineExecutablePath) || !File.Exists(EngineExecutablePath))
                {
                    Debug.WriteLine("[LicensePlateOcrService Error]: Không tìm thấy LicensePlateEngine.exe");
                    return false;
                }
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = EngineExecutablePath,
                WorkingDirectory = Path.GetDirectoryName(EngineExecutablePath) ?? string.Empty,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            _readyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

            _process.Exited += (sender, args) =>
            {
                Debug.WriteLine("[LicensePlateOcrService]: Tiến trình OCR đã kết thúc.");
                FailAllPendingRequests("Tiến trình OCR đã kết thúc bất ngờ.");
            };

            if (!_process.Start())
            {
                Debug.WriteLine("[LicensePlateOcrService Error]: Không thể khởi động LicensePlateEngine.exe");
                return false;
            }

            // Lắng nghe luồng StandardOutput và StandardError
            _ = Task.Run(() => ReadOutputLoopAsync(_process));
            _ = Task.Run(() => ReadErrorLoopAsync(_process));

            // Chờ sự kiện 'ready' từ engine với timeout 15 giây
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);
            var completedTask = await Task.WhenAny(_readyTcs.Task, Task.Delay(Timeout.Infinite, linkedCts.Token));

            if (completedTask == _readyTcs.Task && await _readyTcs.Task)
            {
                Debug.WriteLine("[LicensePlateOcrService]: Engine OCR đã sẵn sàng tiếp nhận yêu cầu.");
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LicensePlateOcrService Exception during startup]: {ex.Message}");
            return false;
        }
        finally
        {
            _startLock.Release();
        }
    }

    public async Task<OcrPlateResponse> RecognizePlateAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return CreateErrorResponse("EMPTY_PATH", "Đường dẫn ảnh không được để trống.");
        }

        var fullPath = Path.GetFullPath(imagePath);
        if (!File.Exists(fullPath))
        {
            return CreateErrorResponse("FILE_NOT_FOUND", $"Không tìm thấy tệp ảnh tại: {fullPath}");
        }

        var started = await EnsureEngineStartedAsync(cancellationToken);
        if (!started || _process == null || _process.HasExited)
        {
            return CreateErrorResponse("ENGINE_NOT_READY", "Không thể khởi động Engine OCR nhận diện biển số.");
        }

        var reqId = $"req_{DateTime.UtcNow:yyyyMMddHHmmss}_{Interlocked.Increment(ref _requestCounter)}";
        var tcs = new TaskCompletionSource<OcrPlateResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[reqId] = tcs;

        try
        {
            var requestObj = new
            {
                id = reqId,
                action = "process",
                type = "image",
                path = fullPath
            };

            var jsonLine = JsonSerializer.Serialize(requestObj);
            await _process.StandardInput.WriteLineAsync(jsonLine);
            await _process.StandardInput.FlushAsync();

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);

            var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, linkedCts.Token));
            if (completedTask == tcs.Task)
            {
                return await tcs.Task;
            }

            return CreateErrorResponse("TIMEOUT", "Quá thời gian chờ phản hồi từ Engine OCR (Timeout 20s).");
        }
        catch (Exception ex)
        {
            return CreateErrorResponse("REQUEST_EXCEPTION", $"Lỗi khi gửi yêu cầu nhận diện: {ex.Message}");
        }
        finally
        {
            _pendingRequests.TryRemove(reqId, out _);
        }
    }

    private async Task ReadOutputLoopAsync(Process process)
    {
        try
        {
            while (!process.HasExited && !_isDisposed)
            {
                var line = await process.StandardOutput.ReadLineAsync();
                if (line == null) break;

                line = line.Trim();
                if (string.IsNullOrEmpty(line)) continue;

                ProcessOutputLine(line);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LicensePlateOcrService Output Exception]: {ex.Message}");
        }
    }

    private void ProcessOutputLine(string line)
    {
        try
        {
            var jsonNode = JsonNode.Parse(line);
            if (jsonNode == null) return;

            // Kiểm tra sự kiện khởi động
            if (jsonNode["event"] != null)
            {
                var eventName = jsonNode["event"]?.GetValue<string>();
                if (string.Equals(eventName, "ready", StringComparison.OrdinalIgnoreCase))
                {
                    _readyTcs?.TrySetResult(true);
                }
                return;
            }

            // Kiểm tra phản hồi ping / shutdown
            if (jsonNode["action"] != null || jsonNode["state"] != null)
            {
                return;
            }

            // Phản hồi kết quả xử lý ảnh
            var response = JsonSerializer.Deserialize<OcrPlateResponse>(line);
            if (response != null)
            {
                var reqId = response.RequestId;
                if (!string.IsNullOrEmpty(reqId) && _pendingRequests.TryGetValue(reqId, out var tcs))
                {
                    tcs.TrySetResult(response);
                    return;
                }

                // Nếu không có requestId hoặc định dạng trả về dùng "id"
                if (jsonNode["id"] != null)
                {
                    var altId = jsonNode["id"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(altId) && _pendingRequests.TryGetValue(altId, out var altTcs))
                    {
                        response.RequestId ??= altId;
                        altTcs.TrySetResult(response);
                        return;
                    }
                }

                // Nếu chỉ có 1 request đang chờ xử lý tuần tự
                if (_pendingRequests.Count == 1)
                {
                    var single = _pendingRequests.Values.FirstOrDefault();
                    single?.TrySetResult(response);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LicensePlateOcrService Parse Line Error]: {ex.Message} -> Line: {line}");
        }
    }

    private async Task ReadErrorLoopAsync(Process process)
    {
        try
        {
            while (!process.HasExited && !_isDisposed)
            {
                var line = await process.StandardError.ReadLineAsync();
                if (line == null) break;
                Debug.WriteLine($"[LicensePlateEngine STDERR]: {line}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LicensePlateOcrService Error Loop Exception]: {ex.Message}");
        }
    }

    private void FailAllPendingRequests(string errorMessage)
    {
        foreach (var kvp in _pendingRequests)
        {
            if (_pendingRequests.TryRemove(kvp.Key, out var tcs))
            {
                tcs.TrySetResult(CreateErrorResponse("PROCESS_TERMINATED", errorMessage));
            }
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

    public async Task StopEngineAsync()
    {
        if (!IsRunning || _process == null) return;

        try
        {
            var shutdownPayload = JsonSerializer.Serialize(new
            {
                id = "shutdown",
                action = "shutdown"
            });
            await _process.StandardInput.WriteLineAsync(shutdownPayload);
            await _process.StandardInput.FlushAsync();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await _process.WaitForExitAsync(cts.Token);
        }
        catch
        {
            // Bỏ qua lỗi khi gửi shutdown
        }
        finally
        {
            CleanupProcess();
        }
    }

    private void CleanupProcess()
    {
        try
        {
            if (_process != null)
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
                _process.Dispose();
                _process = null;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LicensePlateOcrService Cleanup Exception]: {ex.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        await StopEngineAsync();
        _startLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
