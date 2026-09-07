using System.Collections.ObjectModel;
using SmartPS.Data;
using SmartPS.DTOs.Auth;
using SmartPS.Models.Auth;
using SmartPS.Services.Auth;
using SmartPS.Services.Dialog;
using SmartPS.Services.Localization;

namespace SmartPS.ViewModels.Dashboard;

public class DashboardViewModel : ViewModelBase
{
    private readonly IAuthService _authService;
    private readonly IDialogService _dialogService;
    private readonly ILocalizationService _localizationService;

    public string CurrentLanguage => _localizationService.CurrentLanguage;

    // Thông tin người đăng nhập
    public string CurrentUserFullName => _authService.CurrentUser?.FullName ?? _localizationService.GetString("Str_Dash_DefaultAdminName");
    public string CurrentUserRole => _authService.CurrentUser?.Role?.RoleName ?? "Admin";

    // Số liệu KPI tài khoản
    private int _totalUsers;
    public int TotalUsers
    {
        get => _totalUsers;
        set => SetProperty(ref _totalUsers, value);
    }

    private int _adminCount;
    public int AdminCount
    {
        get => _adminCount;
        set => SetProperty(ref _adminCount, value);
    }

    private int _managerCount;
    public int ManagerCount
    {
        get => _managerCount;
        set => SetProperty(ref _managerCount, value);
    }

    private int _staffCount;
    public int StaffCount
    {
        get => _staffCount;
        set => SetProperty(ref _staffCount, value);
    }

    // Danh sách tài khoản & bộ lọc
    private readonly List<User> _allUsers = new();
    public ObservableCollection<User> DisplayedUsers { get; } = new();
    public ObservableCollection<Role> AvailableRoles { get; } = new();

    private string _currentRoleFilter = "All";
    public string CurrentRoleFilter
    {
        get => _currentRoleFilter;
        set
        {
            if (SetProperty(ref _currentRoleFilter, value))
            {
                ApplyRoleFilter();
            }
        }
    }

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyRoleFilter();
            }
        }
    }

    private bool _isCreatePanelVisible;
    public bool IsCreatePanelVisible
    {
        get => _isCreatePanelVisible;
        set => SetProperty(ref _isCreatePanelVisible, value);
    }

    // Form tạo tài khoản mới (Data Binding 2 chiều chuẩn MVVM)
    private string _newUsername = string.Empty;
    public string NewUsername
    {
        get => _newUsername;
        set => SetProperty(ref _newUsername, value);
    }

    private string _newFullName = string.Empty;
    public string NewFullName
    {
        get => _newFullName;
        set => SetProperty(ref _newFullName, value);
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

    private Role? _selectedRole;
    public Role? SelectedRole
    {
        get => _selectedRole;
        set => SetProperty(ref _selectedRole, value);
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                CreateUserCommand.RaiseCanExecuteChanged();
                EditUserCommand.RaiseCanExecuteChanged();
                DeleteUserCommand.RaiseCanExecuteChanged();
            }
        }
    }

    // Commands thuần MVVM
    public AsyncRelayCommand CreateUserCommand { get; }
    public AsyncRelayCommand EditUserCommand { get; }
    public AsyncRelayCommand DeleteUserCommand { get; }
    public RelayCommand FilterRoleCommand { get; }
    public RelayCommand ToggleCreatePanelCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand LogoutCommand { get; }
    public RelayCommand SetLanguageCommand { get; }

    // Sự kiện tương tác cửa sổ
    public event Action? LogoutRequested;

    public DashboardViewModel(IAuthService authService, IDialogService dialogService, ILocalizationService localizationService)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));

        _localizationService.LanguageChanged += () =>
        {
            void UpdateUiLanguage()
            {
                OnPropertyChanged(nameof(CurrentLanguage));
                OnPropertyChanged(nameof(CurrentUserFullName));
                OnPropertyChanged(nameof(CurrentUserRole));
                ApplyRoleFilter();

                var currentRole = SelectedRole;
                var tempRoles = AvailableRoles.ToList();
                AvailableRoles.Clear();
                foreach (var r in tempRoles)
                {
                    AvailableRoles.Add(r);
                }
                SelectedRole = currentRole;
            }

            if (System.Windows.Application.Current?.Dispatcher.CheckAccess() == false)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(UpdateUiLanguage);
            }
            else
            {
                UpdateUiLanguage();
            }
        };

        CreateUserCommand = new AsyncRelayCommand(ExecuteCreateUserAsync, () => !IsBusy);
        EditUserCommand = new AsyncRelayCommand(ExecuteEditUserAsync, _ => !IsBusy);
        DeleteUserCommand = new AsyncRelayCommand(ExecuteDeleteUserAsync, _ => !IsBusy);

        ToggleCreatePanelCommand = new RelayCommand(() => IsCreatePanelVisible = !IsCreatePanelVisible);
        FilterRoleCommand = new RelayCommand(param =>
        {
            if (param is string role)
            {
                CurrentRoleFilter = role;
            }
        });
        RefreshCommand = new AsyncRelayCommand(LoadDataAsync);
        LogoutCommand = new RelayCommand(() =>
        {
            _authService.Logout();
            LogoutRequested?.Invoke();
        });
        SetLanguageCommand = new RelayCommand(param =>
        {
            if (param is string langCode)
            {
                _localizationService.SetLanguage(langCode);
            }
        });

        _ = LoadDataAsync();
    }

    public async Task LoadDataAsync(object? parameter = null)
    {
        try
        {
            IsBusy = true;

            // 1. Tải danh sách vai trò
            var roles = await _authService.GetRolesAsync();
            AvailableRoles.Clear();
            foreach (var r in roles)
            {
                AvailableRoles.Add(r);
            }

            if (SelectedRole is null && AvailableRoles.Count > 0)
            {
                SelectedRole = AvailableRoles[0];
            }

            // 2. Tải danh sách người dùng
            var users = await _authService.GetUsersAsync();
            _allUsers.Clear();
            _allUsers.AddRange(users);

            UpdateKpiStatistics();
            ApplyRoleFilter();
        }
        catch (Exception ex)
        {
            if (DbConnectionHelper.IsConnectionException(ex))
            {
                _dialogService.ShowError(_localizationService.GetString("Msg_Db_ConnectionLost"));
            }
            else
            {
                _dialogService.ShowWarning(_localizationService.GetString("Msg_LoadData_Error", ex.Message));
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void UpdateKpiStatistics()
    {
        TotalUsers = _allUsers.Count;
        AdminCount = _allUsers.Count(u => u.Role?.RoleName == "Admin");
        ManagerCount = _allUsers.Count(u => u.Role?.RoleName == "Manager");
        StaffCount = _allUsers.Count(u => u.Role?.RoleName == "Operator" || u.Role?.RoleName == "Staff");
    }

    private void ApplyRoleFilter()
    {
        DisplayedUsers.Clear();
        var query = CurrentRoleFilter switch
        {
            "Admin" => _allUsers.Where(u => u.Role?.RoleName == "Admin"),
            "Manager" => _allUsers.Where(u => u.Role?.RoleName == "Manager"),
            "Staff" => _allUsers.Where(u => u.Role?.RoleName == "Operator" || u.Role?.RoleName == "Staff"),
            _ => _allUsers
        };

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var search = SearchText.Trim();
            query = query.Where(u =>
                u.Username.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                u.FullName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                u.UserId.ToString().Contains(search) ||
                (u.Role != null && u.Role.RoleName.Contains(search, StringComparison.OrdinalIgnoreCase)));
        }

        foreach (var u in query)
        {
            DisplayedUsers.Add(u);
        }
    }

    private async Task ExecuteCreateUserAsync()
    {
        if (string.IsNullOrWhiteSpace(NewUsername))
        {
            _dialogService.ShowWarning(_localizationService.GetString("Msg_CreateUser_RequiredUsername"));
            return;
        }

        if (string.IsNullOrWhiteSpace(NewFullName))
        {
            _dialogService.ShowWarning(_localizationService.GetString("Msg_CreateUser_RequiredFullName"));
            return;
        }

        if (SelectedRole is null)
        {
            _dialogService.ShowWarning(_localizationService.GetString("Msg_CreateUser_RequiredRole"));
            return;
        }

        if (string.IsNullOrWhiteSpace(NewPassword))
        {
            _dialogService.ShowWarning(_localizationService.GetString("Msg_CreateUser_RequiredPassword"));
            return;
        }

        if (NewPassword.Length < 6)
        {
            _dialogService.ShowWarning(_localizationService.GetString("Msg_CreateUser_PasswordMinLength"));
            return;
        }

        if (NewPassword != ConfirmPassword)
        {
            _dialogService.ShowWarning(_localizationService.GetString("Msg_CreateUser_PasswordMismatch"));
            return;
        }

        try
        {
            IsBusy = true;

            var request = new RegisterRequest
            {
                Username = NewUsername.Trim(),
                FullName = NewFullName.Trim(),
                Password = NewPassword,
                RoleId = SelectedRole.RoleId
            };

            var success = await _authService.RegisterAsync(request);

            if (success)
            {
                _dialogService.ShowSuccess(
                    _localizationService.GetString("Msg_CreateUser_Success", request.Username, SelectedRole.RoleName),
                    _localizationService.GetString("Str_Dialog_Title_Success"));

                // Xóa form
                NewUsername = string.Empty;
                NewFullName = string.Empty;
                NewPassword = string.Empty;
                ConfirmPassword = string.Empty;

                await LoadDataAsync();
            }
            else
            {
                _dialogService.ShowWarning(_localizationService.GetString("Msg_CreateUser_Failed"));
            }
        }
        catch (ArgumentException ex)
        {
            _dialogService.ShowWarning(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            _dialogService.ShowWarning(ex.Message);
        }
        catch (Exception ex)
        {
            if (DbConnectionHelper.IsConnectionException(ex))
            {
                _dialogService.ShowError(_localizationService.GetString("Msg_Db_ConnectionLost"));
            }
            else
            {
                _dialogService.ShowWarning(_localizationService.GetString("Msg_CreateUser_Error", ex.Message));
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteEditUserAsync(object? parameter)
    {
        if (parameter is not User user) return;

        var updated = await _dialogService.ShowEditUserDialogAsync(user, AvailableRoles);
        if (updated)
        {
            await LoadDataAsync();
            _dialogService.ShowSuccess(
                _localizationService.GetString("Msg_EditUser_Success", user.Username),
                _localizationService.GetString("Str_Dialog_Title_Success"));
        }
    }

    private async Task ExecuteDeleteUserAsync(object? parameter)
    {
        if (parameter is not User user) return;

        // Chống tự xóa chính mình
        if (_authService.CurrentUser != null && _authService.CurrentUser.UserId == user.UserId)
        {
            _dialogService.ShowWarning(_localizationService.GetString("Msg_DeleteUser_PreventSelfDelete"));
            return;
        }

        // Yêu cầu xác nhận từ DialogService
        var confirmed = _dialogService.ShowYesNo(
            _localizationService.GetString("Msg_DeleteUser_ConfirmPrompt", user.Username, user.FullName),
            _localizationService.GetString("Msg_DeleteUser_ConfirmTitle"));
        if (!confirmed) return;

        try
        {
            IsBusy = true;
            var success = await _authService.DeleteUserAsync(user.UserId);
            if (success)
            {
                _dialogService.ShowSuccess(
                    _localizationService.GetString("Msg_DeleteUser_Success", user.Username),
                    _localizationService.GetString("Str_Dialog_Title_Success"));
                await LoadDataAsync();
            }
            else
            {
                _dialogService.ShowWarning(_localizationService.GetString("Msg_DeleteUser_Failed"));
            }
        }
        catch (Exception ex)
        {
            if (DbConnectionHelper.IsConnectionException(ex))
            {
                _dialogService.ShowError(_localizationService.GetString("Msg_Db_ConnectionLost"));
            }
            else
            {
                _dialogService.ShowWarning(ex.Message);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }
}
