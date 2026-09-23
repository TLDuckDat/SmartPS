using SmartPS.Models.Ocr;

namespace SmartPS.Services.OcrLisencePlate;

public interface IOcrLicensePlateService : IAsyncDisposable
{
    bool IsRunning { get; }
    string? EngineExecutablePath { get; }

    Task<bool> EnsureEngineStartedAsync(CancellationToken cancellationToken = default);
    Task<OcrPlateResponse> RecognizePlateAsync(string imagePath, CancellationToken cancellationToken = default);
    Task StopEngineAsync();
}
