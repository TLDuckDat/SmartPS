using System.Windows.Controls;
using SmartPS.Data;
using SmartPS.DTOs.Auth;
using SmartPS.Models.Auth;
using SmartPS.Services.Auth;
using SmartPS.Services.Dialog;
using SmartPS.Services.Localization;

namespace SmartPS.ViewModels.Auth;

public class LoginViewModel : ViewModelBase
{
    private readonly IAuthService _authService;
    private readonly IDialogService _dialogService;
    private readonly ILocalizationService _localizationService;

    public string CurrentLanguage => _localizationService.CurrentLanguage;

    private string _username = string.Empty;
    public string Username
    {
        get => _username;
        set => SetProperty(ref _username, value);
    }

    private string _password = string.Empty;
    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value);
    }

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (SetProperty(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(CanInteract));
                LoginCommand.RaiseCanExecuteChanged();
                RetryConnectionCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private bool _isDatabaseConnected = true;
    public bool IsDatabaseConnected
    {
        get => _isDatabaseConnected;
        set => SetProperty(ref _isDatabaseConnected, value);
    }

    private bool _isCheckingConnection;
    public bool IsCheckingConnection
    {
        get => _isCheckingConnection;
        set
        {
            if (SetProperty(ref _isCheckingConnection, value))
            {
                OnPropertyChanged(nameof(CanInteract));
                RetryConnectionCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanInteract => !IsLoading && !IsCheckingConnection;

    public AsyncRelayCommand LoginCommand { get; }
    public AsyncRelayCommand RetryConnectionCommand { get; }
    public RelayCommand CloseCommand { get; }
    public RelayCommand SetLanguageCommand { get; }

    public event Action<User>? LoginSucceeded;
    public event Action? RequestClose;

    public LoginViewModel(IAuthService authService, IDialogService dialogService, ILocalizationService localizationService)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));

        _localizationService.LanguageChanged += () => OnPropertyChanged(nameof(CurrentLanguage));

        LoginCommand = new AsyncRelayCommand(ExecuteLoginAsync, _ => !IsLoading && !IsCheckingConnection);
        RetryConnectionCommand = new AsyncRelayCommand(ExecuteRetryConnectionAsync, () => !IsCheckingConnection && !IsLoading);
        CloseCommand = new RelayCommand(() => RequestClose?.Invoke());
        SetLanguageCommand = new RelayCommand(param =>
        {
            if (param is string langCode)
            {
                _localizationService.SetLanguage(langCode);
            }
        });
    }

    public void SetInitialDbStatus(bool isConnected)
    {
        IsDatabaseConnected = isConnected;
    }

    public async Task ExecuteRetryConnectionAsync()
    {
        try
        {
            IsCheckingConnection = true;
            ErrorMessage = null;

            var canConnect = await _authService.CanConnectToDatabaseAsync();
            if (canConnect)
            {
                await _authService.EnsureDatabaseInitializedAsync();
                IsDatabaseConnected = true;
                _dialogService.ShowSuccess(
                    _localizationService.GetString("Msg_Db_RetrySuccess"),
                    _localizationService.GetString("Str_Dialog_Title_Success"));
            }
            else
            {
                IsDatabaseConnected = false;
                _dialogService.ShowError(
                    _localizationService.GetString("Msg_Db_ConnectionLost"),
                    _localizationService.GetString("Str_Dialog_Title_Error"));
            }
        }
        finally
        {
            IsCheckingConnection = false;
        }
    }

    private async Task ExecuteLoginAsync(object? parameter)
    {
        ErrorMessage = null;

        // 1. Kiểm tra rỗng
        if (string.IsNullOrWhiteSpace(Username))
        {
            SetError(_localizationService.GetString("Msg_Login_RequiredUsername"));
            return;
        }

        string password = string.Empty;
        if (parameter is PasswordBox passwordBox)
        {
            password = passwordBox.Password;
        }
        else if (parameter is string passStr && !string.IsNullOrEmpty(passStr))
        {
            password = passStr;
        }
        else
        {
            password = Password;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            SetError(_localizationService.GetString("Msg_Login_RequiredPassword"));
            return;
        }

        try
        {
            IsLoading = true;

            var request = new LoginRequest
            {
                Username = Username,
                Password = password
            };

            var user = await _authService.LoginAsync(request);

            if (user is not null)
            {
                IsDatabaseConnected = true;
                _dialogService.ShowSuccess(
                    _localizationService.GetString("Msg_Login_Success", user.FullName),
                    _localizationService.GetString("Str_Dialog_Title_Success"));
                LoginSucceeded?.Invoke(user);
            }
            else
            {
                SetError(_localizationService.GetString("Msg_Login_Failed"));
            }
        }
        catch (ArgumentException ex)
        {
            SetError(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            SetError(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            SetError(ex.Message);
        }
        catch (Exception ex)
        {
            if (DbConnectionHelper.IsConnectionException(ex))
            {
                IsDatabaseConnected = false;
                SetError(_localizationService.GetString("Msg_Db_ConnectionLost"));
            }
            else
            {
                SetError(_localizationService.GetString("Msg_Login_SystemError", ex.Message));
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void SetError(string message)
    {
        ErrorMessage = message;
        _dialogService.ShowWarning(message, _localizationService.GetString("Str_Dialog_Title_Warning"));
    }
}
