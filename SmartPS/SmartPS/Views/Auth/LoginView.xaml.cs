using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using SmartPS.Models.Auth;
using SmartPS.ViewModels.Auth;
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

        Loaded += (_, _) => UsernameInput.Focus();

        // Tự động cập nhật ký tự nút Phóng to khi trạng thái cửa sổ thay đổi
        StateChanged += (_, _) =>
        {
            MaximizeButton.Content = WindowState == WindowState.Maximized ? "[=]" : "[ ]";
        };

        CommandBindings.Add(new CommandBinding(SystemCommands.CloseWindowCommand, (_, _) => SystemCommands.CloseWindow(this)));
        CommandBindings.Add(new CommandBinding(SystemCommands.MinimizeWindowCommand, (_, _) => SystemCommands.MinimizeWindow(this)));
        CommandBindings.Add(new CommandBinding(SystemCommands.MaximizeWindowCommand, (_, _) =>
        {
            if (WindowState == WindowState.Maximized)
                SystemCommands.RestoreWindow(this);
            else
                SystemCommands.MaximizeWindow(this);
        }));
    }

    private void OnLoginSucceeded(User user)
    {
        // Khởi tạo và hiển thị Dashboard chính
        var dashboardView = App.ServiceProvider.GetRequiredService<DashboardView>();
        dashboardView.Show();

        // Đóng màn hình đăng nhập
        Close();
    }
}
