using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using SmartPS.Models.Auth;
using SmartPS.Services.Auth;
using SmartPS.ViewModels.Dashboard;
using SmartPS.Views.Auth;
using SmartPS.Views.Common;

namespace SmartPS.Views.Dashboard;

/// <summary>
/// Interaction logic for DashboardView.xaml
/// </summary>
public partial class DashboardView : Window
{
    public DashboardViewModel ViewModel { get; }

    public DashboardView(DashboardViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = ViewModel;

        ViewModel.LogoutRequested += OnLogoutRequested;
        ViewModel.OperationSucceeded += OnOperationSucceeded;
        ViewModel.OperationFailed += OnOperationFailed;
    }

    private void CreateUserButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CreateUserCommand.CanExecute(null))
        {
            ViewModel.CreateUserCommand.Execute(new object[] { NewPasswordInput, ConfirmPasswordInput });
        }
    }

    private async void EditUserButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is User user)
        {
            await OpenEditUserDialogAsync(user);
        }
    }

    private async void DeleteUserButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is User user)
        {
            await ConfirmAndDeleteUserAsync(user);
        }
    }

    private async Task ConfirmAndDeleteUserAsync(User user)
    {
        if (ViewModel.IsBusy) return;

        var authService = App.ServiceProvider.GetRequiredService<IAuthService>();

        // 1. Kiểm tra tài khoản đang đăng nhập
        if (authService.CurrentUser != null && authService.CurrentUser.UserId == user.UserId)
        {
            NotificationDialog.ShowWarning(
                "Bạn không thể tự xóa tài khoản hiện đang đăng nhập vào hệ thống.",
                "Không thể xóa tài khoản",
                this);
            return;
        }

        // 2. Hiển thị Dialog xác nhận với lựa chọn Yes / No
        var confirmed = NotificationDialog.ShowYesNo(
            $"Bạn có chắc chắn muốn xóa tài khoản '{user.Username}' ({user.FullName}) khỏi hệ thống không?\n\nThao tác này sẽ xóa vĩnh viễn và không thể hoàn tác.",
            "Xác nhận xóa tài khoản",
            NotificationType.Warning,
            this,
            "Có",
            "Không");

        if (!confirmed) return;

        // 3. Tiến hành xóa
        var success = await ViewModel.DeleteUserAsync(user.UserId);
        if (success)
        {
            NotificationDialog.ShowSuccess(
                $"Đã xóa tài khoản '{user.Username}' thành công.",
                "Xóa thành công",
                this);
        }
    }

    private async void UserDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (UserDataGrid.SelectedItem is User user)
        {
            await OpenEditUserDialogAsync(user);
        }
    }

    private async Task OpenEditUserDialogAsync(User user)
    {
        var authService = App.ServiceProvider.GetRequiredService<IAuthService>();
        var editDialog = new EditUserDialog(user, ViewModel.AvailableRoles, authService)
        {
            Owner = this
        };

        if (editDialog.ShowDialog() == true)
        {
            NotificationDialog.ShowSuccess(
                $"Cập nhật thành công tài khoản '{user.Username}'.",
                "Cập nhật thành công",
                this);

            await ViewModel.LoadDataAsync();
        }
    }

    private void OnOperationSucceeded(string message)
    {
        NotificationDialog.ShowSuccess(message, "Thành công", this);
    }

    private void OnOperationFailed(string message)
    {
        NotificationDialog.ShowWarning(message, "Thông báo", this);
    }

    private void OnLogoutRequested()
    {
        var loginView = App.ServiceProvider.GetRequiredService<LoginView>();
        loginView.Show();
        Close();
    }
}
