using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using SmartPS.Services.Preferences;
using SmartPS.ViewModels.Dashboard;
using SmartPS.Views.Auth;

namespace SmartPS.Views.Dashboard;

/// <summary>
/// Interaction logic for DashboardView.xaml
/// Được tối giản hoàn toàn theo chuẩn MVVM: Toàn bộ nghiệp vụ, logic và commands nằm trong DashboardViewModel
/// </summary>
public partial class DashboardView : Window
{
    // Dưới độ rộng này thanh trên cùng không đủ chỗ cho nhãn chữ của các nút hệ thống
    private const double CompactTopBarWidth = 1120;

    // Đồng hồ trên thanh trạng thái (chỉ là hiển thị nên đặt ở View)
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    public DashboardViewModel ViewModel { get; }

    private readonly IUserPreferencesStore? _preferences;

    public DashboardView(DashboardViewModel viewModel, IUserPreferencesStore? preferences = null)
    {
        InitializeComponent();
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = ViewModel;
        _preferences = preferences;

        RestorePlacement();
        Closing += (_, _) => SavePlacement();

        ViewModel.LogoutRequested += OnLogoutRequested;
        ViewModel.ExitRequested += OnExitRequested;

        _clockTimer.Tick += (_, _) => UpdateClock();
        UpdateClock();
        _clockTimer.Start();
        Closed += (_, _) => _clockTimer.Stop();

        SizeChanged += (_, e) => Tag = e.NewSize.Width < CompactTopBarWidth ? "Compact" : null;
    }

    /// <summary>Mở lại cửa sổ đúng kích thước / vị trí lần trước, nếu vị trí đó vẫn nằm trên màn hình hiện có.</summary>
    private void RestorePlacement()
    {
        var saved = _preferences?.Current.MainWindow;
        if (saved == null || saved.Width < MinWidth || saved.Height < MinHeight) return;

        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var bounds = new Rect(saved.Left, saved.Top, saved.Width, saved.Height);
        if (!screen.IntersectsWith(bounds)) return;

        WindowStartupLocation = WindowStartupLocation.Manual;
        WindowState = WindowState.Normal;
        Left = saved.Left;
        Top = saved.Top;
        Width = saved.Width;
        Height = saved.Height;
        if (saved.IsMaximized)
            WindowState = WindowState.Maximized;
    }

    private void SavePlacement()
    {
        if (_preferences == null) return;

        // Khi đang phóng to, RestoreBounds là kích thước cửa sổ thường để lần sau bỏ phóng to vẫn đúng
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (bounds.IsEmpty) return;

        var isMaximized = WindowState == WindowState.Maximized;
        _preferences.Update(p => p.MainWindow = new WindowPlacement
        {
            Left = bounds.Left,
            Top = bounds.Top,
            Width = bounds.Width,
            Height = bounds.Height,
            IsMaximized = isMaximized
        });
    }

    private void UpdateClock()
    {
        ClockText.Text = DateTime.Now.ToString("HH:mm:ss  dd/MM/yyyy");
    }

    private void OnLogoutRequested()
    {
        var loginView = App.ServiceProvider.GetRequiredService<LoginView>();
        loginView.Show();
        Close();
    }

    private void OnExitRequested()
    {
        Application.Current.Shutdown();
    }
}
