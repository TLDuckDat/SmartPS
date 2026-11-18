// Điều phối biểu mẫu hồ sơ cá nhân và đổi mật khẩu của người dùng đang đăng nhập.

using SmartPS.DTOs.Auth;
using SmartPS.Services.Auth;
using SmartPS.Services.Dialog;
using SmartPS.Services.Localization;

namespace SmartPS.ViewModels.Dashboard;

public sealed class UserProfileViewModel : ViewModelBase
{
    private readonly IAuthService _authService;
    private readonly IDialogService _dialogService;
    private readonly ILocalizationService _localizationService;
    private string _fullName = string.Empty;
    private string _phoneNumber = string.Empty;
    private string _email = string.Empty;
    private string _currentPassword = string.Empty;
    private string _newPassword = string.Empty;
    private string _confirmPassword = string.Empty;
    private bool _isBusy;

    public UserProfileViewModel(
        IAuthService authService,
        IDialogService dialogService,
        ILocalizationService localizationService)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));

        var user = _authService.CurrentUser ?? throw new InvalidOperationException(
            _localizationService.GetString("Msg_Profile_NotAuthenticated"));

        Username = user.Username;
        _fullName = user.FullName;
        _phoneNumber = user.PhoneNumber ?? string.Empty;
        _email = user.Email ?? string.Empty;

        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
        CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
    }

    public event Action<bool>? RequestClose;

    public string Username { get; }

    public string FullName
    {
        get => _fullName;
        set => SetProperty(ref _fullName, value);
    }

    public string PhoneNumber
    {
        get => _phoneNumber;
        set => SetProperty(ref _phoneNumber, value);
    }

    public string Email
    {
        get => _email;
        set => SetProperty(ref _email, value);
    }

    public string CurrentPassword
    {
        get => _currentPassword;
        set => SetProperty(ref _currentPassword, value);
    }

    public string NewPassword
    {
        get => _newPassword;
        set
        {
            if (SetProperty(ref _newPassword, value))
            {
                OnPropertyChanged(nameof(PasswordStrengthScore));
                OnPropertyChanged(nameof(PasswordStrengthText));
            }
        }
    }

    public string ConfirmPassword
    {
        get => _confirmPassword;
        set => SetProperty(ref _confirmPassword, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                SaveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public int PasswordStrengthScore => CalculatePasswordStrength(NewPassword);

    public string PasswordStrengthText => PasswordStrengthScore switch
    {
        0 => _localizationService.GetString("Str_Profile_PasswordStrengthNone"),
        1 => _localizationService.GetString("Str_Profile_PasswordStrengthWeak"),
        <= 3 => _localizationService.GetString("Str_Profile_PasswordStrengthMedium"),
        _ => _localizationService.GetString("Str_Profile_PasswordStrengthStrong")
    };

    public AsyncRelayCommand SaveCommand { get; }
    public RelayCommand CancelCommand { get; }

    private async Task SaveAsync()
    {
        var passwordRequested = !string.IsNullOrEmpty(CurrentPassword) ||
                                !string.IsNullOrEmpty(NewPassword) ||
                                !string.IsNullOrEmpty(ConfirmPassword);

        if (string.IsNullOrWhiteSpace(FullName))
        {
            ShowWarning("Msg_Profile_RequiredFullName");
            return;
        }

        if (passwordRequested)
        {
            if (string.IsNullOrEmpty(CurrentPassword))
            {
                ShowWarning("Msg_Profile_CurrentPasswordRequired");
                return;
            }

            if (string.IsNullOrEmpty(NewPassword))
            {
                ShowWarning("Msg_Profile_NewPasswordRequired");
                return;
            }

            if (!string.Equals(NewPassword, ConfirmPassword, StringComparison.Ordinal))
            {
                ShowWarning("Msg_Profile_PasswordMismatch");
                return;
            }

            if (NewPassword.Length < 6)
            {
                ShowWarning("Msg_Profile_PasswordTooShort");
                return;
            }

            if (PasswordStrengthScore < 2)
            {
                ShowWarning("Msg_Profile_PasswordWeak");
                return;
            }
        }

        try
        {
            IsBusy = true;
            await _authService.UpdateOwnProfileAsync(new UpdateOwnProfileRequest
            {
                FullName = FullName,
                PhoneNumber = PhoneNumber,
                Email = Email,
                CurrentPassword = passwordRequested ? CurrentPassword : null,
                NewPassword = passwordRequested ? NewPassword : null
            });

            RequestClose?.Invoke(true);
        }
        catch (UnauthorizedAccessException)
        {
            ShowWarning("Msg_Profile_InvalidCurrentPassword");
        }
        catch (ArgumentException ex)
        {
            _dialogService.ShowWarning(ex.Message);
        }
        catch (Exception ex)
        {
            _dialogService.ShowError(_localizationService.GetString("Msg_Profile_SaveError", ex.Message));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ShowWarning(string key)
    {
        _dialogService.ShowWarning(_localizationService.GetString(key));
    }

    private static int CalculatePasswordStrength(string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return 0;
        }

        var score = 0;
        if (password.Length >= 6) score++;
        if (password.Length >= 10) score++;
        if (password.Any(char.IsUpper) && password.Any(char.IsLower)) score++;
        if (password.Any(char.IsDigit)) score++;
        if (password.Any(ch => !char.IsLetterOrDigit(ch))) score++;
        return Math.Min(score, 4);
    }
}
