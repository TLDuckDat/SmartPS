using System.Windows.Controls;
using SmartPS.DTOs.Auth;
using SmartPS.Models.Auth;
using SmartPS.Services.Auth;

namespace SmartPS.ViewModels.Auth;

public class LoginViewModel : ViewModelBase
{
    private readonly IAuthService _authService;

    private string _username = string.Empty;
    public string Username
    {
        get => _username;
        set => SetProperty(ref _username, value);
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
            }
        }
    }

    public bool CanInteract => !IsLoading;

    public AsyncRelayCommand LoginCommand { get; }
    public RelayCommand CloseCommand { get; }

    public event Action<User>? LoginSucceeded;
    public event Action<string>? LoginFailed;
    public event Action? RequestClose;

    public LoginViewModel(IAuthService authService)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));

        LoginCommand = new AsyncRelayCommand(ExecuteLoginAsync, _ => !IsLoading);
        CloseCommand = new RelayCommand(() => RequestClose?.Invoke());
    }

    private async Task ExecuteLoginAsync(object? parameter)
    {
        ErrorMessage = null;

        // 1. Kiểm tra rỗng
        if (string.IsNullOrWhiteSpace(Username))
        {
            SetError("Vui lòng nhập tên đăng nhập.");
            return;
        }

        string password = string.Empty;
        if (parameter is PasswordBox passwordBox)
        {
            password = passwordBox.Password;
        }
        else if (parameter is string passStr)
        {
            password = passStr;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            SetError("Vui lòng nhập mật khẩu.");
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
                LoginSucceeded?.Invoke(user);
            }
            else
            {
                SetError("Đăng nhập không thành công. Vui lòng kiểm tra lại tài khoản hoặc mật khẩu.");
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
        catch (TimeoutException)
        {
            SetError("Quá thời gian kết nối tới máy chủ cơ sở dữ liệu. Vui lòng thử lại sau.");
        }
        catch (Exception ex)
        {
            var typeName = ex.GetType().FullName ?? string.Empty;
            if (typeName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
                typeName.Contains("Postgres", StringComparison.OrdinalIgnoreCase) ||
                typeName.Contains("Sql", StringComparison.OrdinalIgnoreCase) ||
                typeName.Contains("Socket", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("5432", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("network", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("server", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("connection", StringComparison.OrdinalIgnoreCase))
            {
                SetError("Không thể kết nối đến máy chủ cơ sở dữ liệu (PostgreSQL - Docker Port 5432).\nVui lòng kiểm tra container 'my-postgres' và chuỗi kết nối.");
            }
            else
            {
                SetError($"Đã xảy ra lỗi hệ thống: {ex.Message}");
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
        LoginFailed?.Invoke(message);
    }
}
