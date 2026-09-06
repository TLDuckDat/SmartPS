using System.Windows;
using System.Windows.Input;
using SmartPS.DTOs.Auth;
using SmartPS.Models.Auth;
using SmartPS.Services.Auth;
using SmartPS.Views.Common;

namespace SmartPS.Views.Dashboard;

/// <summary>
/// Interaction logic for EditUserDialog.xaml
/// </summary>
public partial class EditUserDialog : Window
{
    private readonly User _user;
    private readonly IAuthService _authService;

    public EditUserDialog(User user, IEnumerable<Role> availableRoles, IAuthService authService)
    {
        InitializeComponent();

        _user = user ?? throw new ArgumentNullException(nameof(user));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));

        // Nạp dữ liệu vào form
        UserIdInput.Text = _user.UserId.ToString();
        UsernameInput.Text = _user.Username;
        FullNameInput.Text = _user.FullName;

        RoleComboBox.ItemsSource = availableRoles.ToList();
        RoleComboBox.SelectedItem = availableRoles.FirstOrDefault(r => r.RoleId == _user.RoleId);

        StatusComboBox.SelectedIndex = _user.IsActive ? 0 : 1;

        Loaded += (_, _) => FullNameInput.Focus();
    }


    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var fullName = FullNameInput.Text?.Trim();
        if (string.IsNullOrWhiteSpace(fullName))
        {
            NotificationDialog.ShowWarning("Vui lòng nhập họ và tên của người dùng.", "Thiếu thông tin", this);
            FullNameInput.Focus();
            return;
        }

        if (RoleComboBox.SelectedItem is not Role selectedRole)
        {
            NotificationDialog.ShowWarning("Vui lòng chọn vai trò cho người dùng.", "Thiếu thông tin", this);
            RoleComboBox.Focus();
            return;
        }

        string newPassword = NewPasswordInput.Password;
        string confirmPassword = ConfirmPasswordInput.Password;

        // Nếu admin muốn đổi mật khẩu
        if (!string.IsNullOrEmpty(newPassword) || !string.IsNullOrEmpty(confirmPassword))
        {
            if (newPassword.Length < 6)
            {
                NotificationDialog.ShowWarning("Mật khẩu mới phải có độ dài tối thiểu 6 ký tự.", "Mật khẩu không hợp lệ", this);
                NewPasswordInput.Focus();
                return;
            }

            if (newPassword != confirmPassword)
            {
                NotificationDialog.ShowWarning("Mật khẩu xác nhận không trùng khớp. Vui lòng kiểm tra lại.", "Mật khẩu không khớp", this);
                ConfirmPasswordInput.Focus();
                return;
            }
        }

        try
        {
            SaveButton.IsEnabled = false;

            var request = new UpdateUserRequest
            {
                UserId = _user.UserId,
                FullName = fullName,
                RoleId = selectedRole.RoleId,
                IsActive = StatusComboBox.SelectedIndex == 0,
                NewPassword = string.IsNullOrWhiteSpace(newPassword) ? null : newPassword
            };

            var success = await _authService.UpdateUserAsync(request);

            if (success)
            {
                DialogResult = true;
                Close();
            }
            else
            {
                NotificationDialog.ShowWarning("Không thể cập nhật thông tin. Vui lòng thử lại.", "Thất bại", this);
            }
        }
        catch (Exception ex)
        {
            NotificationDialog.ShowError($"Đã xảy ra lỗi: {ex.Message}", "Lỗi cập nhật", this);
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
            e.Handled = true;
        }
    }
}
