using System.Collections.ObjectModel;
using System.Windows.Controls;
using SmartPS.DTOs.Auth;
using SmartPS.Models.Auth;
using SmartPS.Services.Auth;

namespace SmartPS.ViewModels.Dashboard;

public class DashboardViewModel : ViewModelBase
{
    private readonly IAuthService _authService;

    // Thông tin người đăng nhập
    public string CurrentUserFullName => _authService.CurrentUser?.FullName ?? "Quản trị viên Hệ thống";
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

    private string _currentRoleFilter = "Tất cả";
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

    // Form tạo tài khoản mới
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
        set => SetProperty(ref _isBusy, value);
    }

    // Các lệnh
    public AsyncRelayCommand CreateUserCommand { get; }
    public RelayCommand FilterRoleCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand LogoutCommand { get; }

    // Sự kiện tương tác
    public event Action? LogoutRequested;
    public event Action<string>? OperationSucceeded;
    public event Action<string>? OperationFailed;

    public DashboardViewModel(IAuthService authService)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));

        CreateUserCommand = new AsyncRelayCommand(ExecuteCreateUserAsync, _ => !IsBusy);
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
            OperationFailed?.Invoke($"Không thể tải dữ liệu: {ex.Message}");
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
            "Nhân viên" => _allUsers.Where(u => u.Role?.RoleName == "Operator" || u.Role?.RoleName == "Staff"),
            _ => _allUsers
        };

        foreach (var u in query)
        {
            DisplayedUsers.Add(u);
        }
    }

    private async Task ExecuteCreateUserAsync(object? parameter)
    {
        // 1. Kiểm tra đầu vào
        if (string.IsNullOrWhiteSpace(NewUsername))
        {
            OperationFailed?.Invoke("Vui lòng nhập tên đăng nhập.");
            return;
        }

        if (string.IsNullOrWhiteSpace(NewFullName))
        {
            OperationFailed?.Invoke("Vui lòng nhập họ và tên.");
            return;
        }

        if (SelectedRole is null)
        {
            OperationFailed?.Invoke("Vui lòng chọn vai trò cho tài khoản.");
            return;
        }

        // Lấy password và confirm password từ tham số
        string password = string.Empty;
        string confirmPassword = string.Empty;

        if (parameter is object[] boxes && boxes.Length >= 2)
        {
            if (boxes[0] is PasswordBox p1) password = p1.Password;
            if (boxes[1] is PasswordBox p2) confirmPassword = p2.Password;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            OperationFailed?.Invoke("Vui lòng nhập mật khẩu.");
            return;
        }

        if (password.Length < 6)
        {
            OperationFailed?.Invoke("Mật khẩu phải có độ dài tối thiểu 6 ký tự.");
            return;
        }

        if (password != confirmPassword)
        {
            OperationFailed?.Invoke("Mật khẩu xác nhận không khớp. Vui lòng nhập lại.");
            return;
        }

        try
        {
            IsBusy = true;

            var request = new RegisterRequest
            {
                Username = NewUsername.Trim(),
                FullName = NewFullName.Trim(),
                Password = password,
                RoleId = SelectedRole.RoleId
            };

            var success = await _authService.RegisterAsync(request);

            if (success)
            {
                OperationSucceeded?.Invoke($"Tạo tài khoản '{request.Username}' ({SelectedRole.RoleName}) thành công!");

                // Xóa dữ liệu form
                NewUsername = string.Empty;
                NewFullName = string.Empty;
                if (parameter is object[] pBoxes)
                {
                    if (pBoxes[0] is PasswordBox pb1) pb1.Password = string.Empty;
                    if (pBoxes[1] is PasswordBox pb2) pb2.Password = string.Empty;
                }

                // Tải lại danh sách
                await LoadDataAsync();
            }
            else
            {
                OperationFailed?.Invoke("Tạo tài khoản không thành công. Vui lòng thử lại.");
            }
        }
        catch (ArgumentException ex)
        {
            OperationFailed?.Invoke(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            OperationFailed?.Invoke(ex.Message);
        }
        catch (Exception ex)
        {
            OperationFailed?.Invoke($"Đã xảy ra lỗi khi tạo tài khoản: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }
}
