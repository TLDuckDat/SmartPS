namespace SmartPS.Services.Audio;

/// <summary>
/// Trừu tượng hóa dịch vụ phát âm thanh thông báo của hệ thống.
/// Tuân thủ Dependency Inversion Principle (DIP) và Single Responsibility Principle (SRP),
/// giúp tầng ViewModel không bị ràng buộc trực tiếp vào System.Media hay win32 sound APIs.
/// </summary>
public interface IAudioAlertService
{
    void PlaySuccessAlert();
    void PlayWarningAlert();
    void PlayErrorAlert();
}
