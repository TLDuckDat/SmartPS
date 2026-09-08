using System.IO;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartPS.Data;
using SmartPS.Services.Auth;
using SmartPS.Services.Authorization;
using SmartPS.Services.Dialog;
using SmartPS.Services.Localization;
using SmartPS.ViewModels.Auth;
using SmartPS.ViewModels.Dashboard;
using SmartPS.Views.Auth;
using SmartPS.Views.Dashboard;

namespace SmartPS;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public static IServiceProvider ServiceProvider { get; private set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 1. Khởi tạo cấu hình ứng dụng từ appsettings.json
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .Build();

        // 2. Cấu hình Service Collection (Dependency Injection)
        var services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(configuration);

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=SmartPS;Username=smartps;Password=smartps;";

        // Cấu hình Npgsql PostgreSQL
        services.AddDbContextFactory<SmartPsDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

        // Đăng ký Business Services
        services.AddSingleton<IAuthService, AuthService>();
        services.AddSingleton<IPermissionService, PermissionService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<ILocalizationService, LocalizationService>();

        // Đăng ký ViewModels
        services.AddTransient<LoginViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<SmartPS.ViewModels.Overview.OverviewViewModel>();
        services.AddTransient<SmartPS.ViewModels.GateControl.GateControlViewModel>();
        services.AddTransient<SmartPS.ViewModels.ParkingMap.ParkingMapViewModel>();
        services.AddTransient<SmartPS.ViewModels.Customers.CustomersViewModel>();
        services.AddTransient<SmartPS.ViewModels.Reports.ReportsViewModel>();
        services.AddTransient<SmartPS.ViewModels.Incidents.IncidentsViewModel>();
        services.AddTransient<SmartPS.ViewModels.Transactions.TransactionsViewModel>();
        services.AddTransient<SmartPS.ViewModels.UserManagement.UserManagementViewModel>();
        services.AddTransient<SmartPS.ViewModels.Pricing.PricingViewModel>();
        services.AddTransient<SmartPS.ViewModels.Settings.SettingsViewModel>();

        // Đăng ký Views
        services.AddTransient<LoginView>();
        services.AddTransient<DashboardView>();

        ServiceProvider = services.BuildServiceProvider();

        // 3. Tự động kiểm tra, tạo Database PostgreSQL (Code First) và nạp dữ liệu mặc định (Seed Data)
        var dbConnected = false;
        try
        {
            var dbContextFactory = ServiceProvider.GetRequiredService<IDbContextFactory<SmartPsDbContext>>();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cts.Token);
            if (await dbContext.Database.CanConnectAsync(cts.Token))
            {
                dbConnected = true;
                await DbInitializer.InitializeAsync(dbContext);
            }
        }
        catch (Exception ex)
        {
            // Bắt lỗi kết nối PostgreSQL nếu máy chủ CSDL chưa bật
            System.Diagnostics.Debug.WriteLine($"[SmartPS PostgreSQL Auto-DB Warning]: {ex.Message}");
        }

        // 4. Khởi tạo và hiển thị màn hình Đăng nhập
        var loginView = ServiceProvider.GetRequiredService<LoginView>();
        loginView.ViewModel.SetInitialDbStatus(dbConnected);
        loginView.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (ServiceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }

        base.OnExit(e);
    }
}
