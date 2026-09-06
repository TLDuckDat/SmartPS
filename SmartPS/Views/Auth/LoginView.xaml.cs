using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using SmartPS.Models.Auth;
using SmartPS.ViewModels.Auth;
using SmartPS.Views.Common;
using SmartPS.Views.Dashboard;

namespace SmartPS.Views.Auth;

/// <summary>
/// Interaction logic for LoginView.xaml
/// </summary>
public partial class LoginView : Window
{
    public LoginViewModel ViewModel { get; }

    public LoginView(LoginViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = ViewModel;

        ViewModel.RequestClose += Close;
        ViewModel.LoginSucceeded += OnLoginSucceeded;
        ViewModel.LoginFailed += OnLoginFailed;

        Loaded += (_, _) => UsernameInput.Focus();

        // Tự động cập nhật ký tự nút Phóng to khi trạng thái cửa sổ thay đổi
        StateChanged += (_, _) =>
        {
            MaximizeButton.Content = WindowState == WindowState.Maximized ? "[=]" : "[ ]";
        };
    }

    private void OnLoginSucceeded(User user)
    {
        NotificationDialog.ShowSuccess(
            $"Xin chào {user.FullName}!\nBạn đã đăng nhập thành công vào Hệ thống Quản lý Bãi đỗ xe Smart Parking System.",
            "Đăng nhập thành công",
            this);

        // Khởi tạo và hiển thị Dashboard chính
        var dashboardView = App.ServiceProvider.GetRequiredService<DashboardView>();
        dashboardView.Show();

        // Đóng màn hình đăng nhập
        Close();
    }

    private void OnLoginFailed(string message)
    {
        NotificationDialog.ShowWarning(
            message,
            "Lỗi đăng nhập",
            this);
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void UsernameInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            PasswordInput.Focus();
            e.Handled = true;
        }
    }

    private void PasswordInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (ViewModel.LoginCommand.CanExecute(PasswordInput.Password))
            {
                ViewModel.LoginCommand.Execute(PasswordInput.Password);
            }
            e.Handled = true;
        }
    }
}
