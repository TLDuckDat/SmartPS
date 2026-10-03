using System.Media;

namespace SmartPS.Services.Audio;

/// <summary>
/// Triển khai phát âm thanh dựa trên Windows SystemSounds.
/// </summary>
public class SystemAudioAlertService : IAudioAlertService
{
    public void PlaySuccessAlert()
    {
        try
        {
            SystemSounds.Asterisk.Play();
        }
        catch { }
    }

    public void PlayWarningAlert()
    {
        try
        {
            SystemSounds.Exclamation.Play();
        }
        catch { }
    }

    public void PlayErrorAlert()
    {
        try
        {
            SystemSounds.Hand.Play();
        }
        catch { }
    }
}
