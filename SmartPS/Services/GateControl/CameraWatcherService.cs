using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;

namespace SmartPS.Services.GateControl;

public class CameraWatcherService : ICameraWatcherService
{
    private FileSystemWatcher? _inWatcher;
    private FileSystemWatcher? _outWatcher;
    private readonly ConcurrentDictionary<string, DateTime> _processedFiles = new();

    public bool IsActive { get; private set; }
    public string InCameraDirectory { get; }
    public string OutCameraDirectory { get; }

    public event Action<string>? ImageArrivedAtInLane;
    public event Action<string>? ImageArrivedAtOutLane;

    public CameraWatcherService()
    {
        var baseDir = AppContext.BaseDirectory;
        InCameraDirectory = Path.Combine(baseDir, "Cameras", "In");
        OutCameraDirectory = Path.Combine(baseDir, "Cameras", "Out");

        Directory.CreateDirectory(InCameraDirectory);
        Directory.CreateDirectory(OutCameraDirectory);
    }

    public void Start()
    {
        if (IsActive) return;

        try
        {
            Directory.CreateDirectory(InCameraDirectory);
            Directory.CreateDirectory(OutCameraDirectory);

            _inWatcher = new FileSystemWatcher(InCameraDirectory)
            {
                Filter = "*.*",
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            _inWatcher.Created += OnInFileCreated;

            _outWatcher = new FileSystemWatcher(OutCameraDirectory)
            {
                Filter = "*.*",
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            _outWatcher.Created += OnOutFileCreated;

            IsActive = true;
            Debug.WriteLine($"[CameraWatcherService]: Đã bật giám sát camera tại:\n  - Làn vào: {InCameraDirectory}\n  - Làn ra: {OutCameraDirectory}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CameraWatcherService Error]: {ex.Message}");
            Stop();
        }
    }

    public void Stop()
    {
        if (!IsActive) return;

        try
        {
            if (_inWatcher != null)
            {
                _inWatcher.EnableRaisingEvents = false;
                _inWatcher.Created -= OnInFileCreated;
                _inWatcher.Dispose();
                _inWatcher = null;
            }

            if (_outWatcher != null)
            {
                _outWatcher.EnableRaisingEvents = false;
                _outWatcher.Created -= OnOutFileCreated;
                _outWatcher.Dispose();
                _outWatcher = null;
            }
        }
        catch { }

        IsActive = false;
        Debug.WriteLine("[CameraWatcherService]: Đã dừng giám sát camera.");
    }

    private void OnInFileCreated(object sender, FileSystemEventArgs e)
    {
        if (!IsImageFile(e.FullPath)) return;
        _ = HandleFileArrivalAsync(e.FullPath, ImageArrivedAtInLane);
    }

    private void OnOutFileCreated(object sender, FileSystemEventArgs e)
    {
        if (!IsImageFile(e.FullPath)) return;
        _ = HandleFileArrivalAsync(e.FullPath, ImageArrivedAtOutLane);
    }

    private async Task HandleFileArrivalAsync(string filePath, Action<string>? handler)
    {
        if (handler == null) return;

        // Chống lặp sự kiện trong vòng 5 giây
        var now = DateTime.UtcNow;
        if (_processedFiles.TryGetValue(filePath, out var lastTime) && (now - lastTime).TotalSeconds < 5)
        {
            return;
        }
        _processedFiles[filePath] = now;

        // Chờ file ghi xong (file copy từ camera)
        var isReady = await WaitForFileReadyAsync(filePath);
        if (!isReady) return;

        handler.Invoke(filePath);
    }

    private static async Task<bool> WaitForFileReadyAsync(string filePath, int maxRetries = 10, int delayMs = 300)
    {
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (stream.Length > 0) return true;
            }
            catch (IOException)
            {
                await Task.Delay(delayMs);
            }
            catch
            {
                return false;
            }
        }
        return false;
    }

    private static bool IsImageFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".jpg" or ".jpeg" or ".png" or ".bmp";
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
