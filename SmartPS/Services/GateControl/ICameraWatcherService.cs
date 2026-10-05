namespace SmartPS.Services.GateControl;

public interface ICameraWatcherService : IDisposable
{
    bool IsActive { get; }
    string InCameraDirectory { get; }
    string OutCameraDirectory { get; }

    event Action<string>? ImageArrivedAtInLane;
    event Action<string>? ImageArrivedAtOutLane;

    void Start();
    void Stop();
}
