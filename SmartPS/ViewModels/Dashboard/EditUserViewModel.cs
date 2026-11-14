using System.Collections.ObjectModel;
using SmartPS.Data;
using SmartPS.DTOs.Auth;
using SmartPS.Models.Auth;
using SmartPS.Services.Auth;
using SmartPS.Services.Authorization;
using SmartPS.Services.Dialog;
using SmartPS.Services.Localization;

namespace SmartPS.ViewModels.Dashboard;

public class EditUserViewModel : ValidatableViewModelBase
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
        set
        {
            if (SetProperty(ref _fullName, value))
            {
                ValidateFullName();
            }
        }
    }

    private Role? _selectedRole;
    public Role? SelectedRole
    {
        get => _selectedRole;
        set
        {
            if (SetProperty(ref _selectedRole, value))
            {
                ValidateRole();
            }
        }
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
        set
        {
            if (SetProperty(ref _newPassword, value))
            {
                ValidatePasswords();
            }
        }
    }

    private string _confirmPassword = string.Empty;
    public string ConfirmPassword
    {
        get => _confirmPassword;
        set
        {
            if (SetProperty(ref _confirmPassword, value))
            {
                ValidatePasswords();
            }
        }
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

        SaveCommand = new AsyncRelayCommand(ExecuteSaveAsync, () => !IsBusy && !HasErrors);
        ErrorsChanged += (_, _) => SaveCommand.RaiseCanExecuteChanged();
        ValidateAll();
        CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
    }

    private async Task ExecuteSaveAsync()
    {
        ErrorMessage = null;

        ValidateAll();
        if (HasErrors)
        {
            return;
        }

        var trimmedFullName = FullName.Trim();

        try
        {
            IsBusy = true;

            var request = new UpdateUserRequest
            {
                UserId = _user.UserId,
                FullName = trimmedFullName,
                RoleId = SelectedRole!.RoleId,
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
        catch (PermissionDeniedException ex)
        {
            SetError(_localizationService.GetString(
                ex.Reason == "AdminRoleRequired" ? "Msg_User_AdminRoleRequiresAdmin" : "Msg_Auth_PermissionDenied"));
        }
        catch (AdminProtectionException ex)
        {
            SetError(_localizationService.GetString(ex.Reason switch
            {
                AdminProtectionReason.LastActiveAdmin => "Msg_User_LastAdminProtected",
                AdminProtectionReason.SelfDeactivate => "Msg_User_CannotLockSelf",
                _ => "Msg_User_CannotChangeOwnRole"
            }));
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

    private void ValidateAll()
    {
        ValidateFullName();
        ValidateRole();
        ValidatePasswords();
    }

    private void ValidateFullName()
        => ValidateProperty(nameof(FullName),
            string.IsNullOrWhiteSpace(FullName) ? AppText.Get("Msg_EditUser_RequiredFullName") : null);

    private void ValidateRole()
        => ValidateProperty(nameof(SelectedRole),
            SelectedRole is null ? AppText.Get("Msg_EditUser_RequiredRole") : null);

    /// <summary>Chỉ kiểm tra khi có nhập mật khẩu mới hoặc xác nhận: tối thiểu 6 ký tự và hai ô khớp nhau.</summary>
    private void ValidatePasswords()
    {
        var entered = !string.IsNullOrEmpty(NewPassword) || !string.IsNullOrEmpty(ConfirmPassword);
        ValidateProperty(nameof(NewPassword),
            entered && NewPassword.Length < 6 ? AppText.Get("Msg_EditUser_PasswordMinLength") : null);
        ValidateProperty(nameof(ConfirmPassword),
            entered && NewPassword != ConfirmPassword ? AppText.Get("Msg_EditUser_PasswordMismatch") : null);
    }

    private void SetError(string message)
    {
        ErrorMessage = message;
        _dialogService.ShowWarning(message);
    }
}


