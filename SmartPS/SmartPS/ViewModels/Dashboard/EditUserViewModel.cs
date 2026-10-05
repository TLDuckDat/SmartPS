using System.Collections.ObjectModel;
using SmartPS.Data;
using SmartPS.DTOs.Auth;
using SmartPS.Models.Auth;
using SmartPS.Services.Auth;
using SmartPS.Services.Dialog;
using SmartPS.Services.Localization;

namespace SmartPS.ViewModels.Dashboard;

public class EditUserViewModel : ViewModelBase
{
    private readonly User _user;
    private readonly IAuthService _authService;
    private readonly IDialogService _dialogService;
    private readonly ILocalizationService _localizationService;

    public int UserId => _user.UserId;
    public string Username => _user.Username;

    private string _fullName = string.Empty;
    public string FullName
    {
        get => _fullName;
        set => SetProperty(ref _fullName, value);
    }

    private Role? _selectedRole;
    public Role? SelectedRole
    {
        get => _selectedRole;
        set => SetProperty(ref _selectedRole, value);
    }

    public ObservableCollection<Role> AvailableRoles { get; } = new();

    private int _statusIndex;
    public int StatusIndex
    {
        get => _statusIndex;
        set => SetProperty(ref _statusIndex, value);
    }

    private string _newPassword = string.Empty;
    public string NewPassword
    {
        get => _newPassword;
        set => SetProperty(ref _newPassword, value);
    }

    private string _confirmPassword = string.Empty;
    public string ConfirmPassword
    {
        get => _confirmPassword;
        set => SetProperty(ref _confirmPassword, value);
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                SaveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public AsyncRelayCommand SaveCommand { get; }
    public RelayCommand CancelCommand { get; }

    public event Action<bool>? RequestClose;

    public EditUserViewModel(
        User user,
        IEnumerable<Role> availableRoles,
        IAuthService authService,
        IDialogService dialogService,
        ILocalizationService localizationService)
    {
        _user = user ?? throw new ArgumentNullException(nameof(user));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));

        _fullName = _user.FullName;
        _statusIndex = _user.IsActive ? 0 : 1;

        foreach (var role in availableRoles)
        {
            AvailableRoles.Add(role);
        }

        _selectedRole = AvailableRoles.FirstOrDefault(r => r.RoleId == _user.RoleId);

        SaveCommand = new AsyncRelayCommand(ExecuteSaveAsync, () => !IsBusy);
        CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
    }

    private async Task ExecuteSaveAsync()
    {
        ErrorMessage = null;

        var trimmedFullName = FullName?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedFullName))
        {
            SetError(_localizationService.GetString("Msg_EditUser_RequiredFullName"));
            return;
        }

        if (SelectedRole is null)
        {
            SetError(_localizationService.GetString("Msg_EditUser_RequiredRole"));
            return;
        }

        // Kiểm tra nếu admin nhập mật khẩu mới
        if (!string.IsNullOrEmpty(NewPassword) || !string.IsNullOrEmpty(ConfirmPassword))
        {
            if (NewPassword.Length < 6)
            {
                SetError(_localizationService.GetString("Msg_EditUser_PasswordMinLength"));
                return;
            }

            if (NewPassword != ConfirmPassword)
            {
                SetError(_localizationService.GetString("Msg_EditUser_PasswordMismatch"));
                return;
            }
        }

        try
        {
            IsBusy = true;

            var request = new UpdateUserRequest
            {
                UserId = _user.UserId,
                FullName = trimmedFullName,
                RoleId = SelectedRole.RoleId,
                IsActive = StatusIndex == 0,
                NewPassword = string.IsNullOrWhiteSpace(NewPassword) ? null : NewPassword
            };

            var success = await _authService.UpdateUserAsync(request);

            if (success)
            {
                RequestClose?.Invoke(true);
            }
            else
            {
                SetError(_localizationService.GetString("Msg_EditUser_Failed"));
            }
        }
        catch (Exception ex)
        {
            if (DbConnectionHelper.IsConnectionException(ex))
            {
                SetError(_localizationService.GetString("Msg_Db_ConnectionLost"));
            }
            else
            {
                SetError(_localizationService.GetString("Msg_EditUser_Error", ex.Message));
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SetError(string message)
    {
        ErrorMessage = message;
        _dialogService.ShowWarning(message);
    }
}


