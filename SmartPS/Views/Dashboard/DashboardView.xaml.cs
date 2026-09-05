using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SmartPS.ViewModels.Dashboard;
using SmartPS.Views.Auth;

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

        StateChanged += (_, _) =>
        {
            MaximizeButton.Content = WindowState == WindowState.Maximized ? "[=]" : "[ ]";
        };
    }

    private void CreateUserButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CreateUserCommand.CanExecute(null))
        {
            ViewModel.CreateUserCommand.Execute(new object[] { NewPasswordInput, ConfirmPasswordInput });
        }
    }

    private void OnOperationSucceeded(string message)
    {
        MessageBox.Show(message,
                        "Thành công",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
    }

    private void OnOperationFailed(string message)
    {
        MessageBox.Show(message,
                        "Thông báo",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
    }

    private void OnLogoutRequested()
    {
        var loginView = App.ServiceProvider.GetRequiredService<LoginView>();
        loginView.Show();
        Close();
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
        Application.Current.Shutdown();
    }
}
