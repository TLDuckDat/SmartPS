using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SmartPS.ViewModels.Dashboard;
using SmartPS.Views.Auth;

namespace SmartPS.Views.Dashboard;

/// <summary>
/// Interaction logic for DashboardView.xaml
/// Được tối giản hoàn toàn theo chuẩn MVVM: Toàn bộ nghiệp vụ, logic và commands nằm trong DashboardViewModel
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
    }

    private void OnLogoutRequested()
    {
        var loginView = App.ServiceProvider.GetRequiredService<LoginView>();
        loginView.Show();
        Close();
    }
}
