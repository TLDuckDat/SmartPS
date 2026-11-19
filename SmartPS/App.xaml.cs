using System.IO;
using LiveChartsCore.SkiaSharpView;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartPS.Data;
using SmartPS.Services.Auth;
using SmartPS.Services.Authorization;
using SmartPS.Services.Customers;
using SmartPS.Services.Dialog;
using SmartPS.Services.Localization;
using SmartPS.Services.ParkingZones;
using SmartPS.Services.Payment;
using SmartPS.Services.Payment.Mock;
using SmartPS.Services.Payment.PayOS;
using SmartPS.Services.Payment.Webhook;
using SmartPS.Services.Shifts;
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

    private static ImageSource? _cachedWindowIcon;

    private static ImageSource? GetOrCreateWindowIcon()
    {
        if (_cachedWindowIcon != null)
            return _cachedWindowIcon;

        try
        {
            if (Current?.TryFindResource("Illust_CapybaraLogo") is DrawingImage drawingImage && drawingImage.Drawing != null)
            {
                const int size = 64;
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawImage(drawingImage, new Rect(0, 0, size, size));
                }
                var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(visual);
                rtb.Freeze();
                var frame = BitmapFrame.Create(rtb);
                frame.Freeze();
                _cachedWindowIcon = frame;
            }
        }
        catch
        {
            // Ignore icon rendering errors in headless test environments
        }

        return _cachedWindowIcon;
    }

    private static bool _globalFontApplied;

    /// <summary>
    /// Đặt Be Vietnam Pro làm font mặc định cho mọi cửa sổ, menu xổ xuống, chú thích và hộp chọn.
    /// Style ngầm định TargetType=Window trong App.xaml không áp dụng cho lớp con (LoginView, DashboardView...)
    /// nên trước đây một số chỗ hiện Be Vietnam Pro, chỗ khác lại hiện Segoe UI, nhìn lệch nhau.
    /// </summary>
    private void ApplyGlobalFont()
    {
        if (_globalFontApplied || TryFindResource("AppFontFamily") is not FontFamily font)
            return;

        _globalFontApplied = true;
        var options = FrameworkPropertyMetadataOptions.Inherits
            | FrameworkPropertyMetadataOptions.AffectsMeasure
            | FrameworkPropertyMetadataOptions.AffectsRender;

        foreach (var type in new[]
                 {
                     typeof(Window), typeof(System.Windows.Controls.ToolTip), typeof(System.Windows.Controls.ContextMenu),
                     typeof(System.Windows.Controls.Primitives.Popup)
                 })
        {
            try
            {
                if (type == typeof(System.Windows.Controls.Primitives.Popup))
                {
                    System.Windows.Documents.TextElement.FontFamilyProperty.OverrideMetadata(type, new FrameworkPropertyMetadata(font, options));
                }
                else
                {
                    System.Windows.Controls.Control.FontFamilyProperty.OverrideMetadata(type, new FrameworkPropertyMetadata(font, options));
                }
            }
            catch (ArgumentException)
            {
                // Kiểu đã có metadata riêng: bỏ qua, các kiểu còn lại vẫn được áp dụng
            }
        }
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Xử lý lỗi toàn cục: lỗi không được bắt ở đâu khác được ghi log và không làm ứng dụng văng
        DispatcherUnhandledException += (_, args) =>
        {
            SmartPS.Services.Logging.ErrorLog.Error("UI", args.Exception);
            args.Handled = true;
            try
            {
                MessageBox.Show(
                    "Đã xảy ra lỗi không mong muốn. Bạn có thể tiếp tục sử dụng. Nếu lỗi lặp lại, vui lòng liên hệ bộ phận kỹ thuật.",
                    "SmartPS",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch
            {
                // Không có cửa sổ để hiển thị thông báo (ví dụ đang tắt) thì chỉ giữ việc ghi log
            }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            SmartPS.Services.Logging.ErrorLog.Error(
                "Domain",
                args.ExceptionObject as Exception,
                args.ExceptionObject?.ToString());
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            SmartPS.Services.Logging.ErrorLog.Error("Task", args.Exception);
            args.SetObserved();
        };

        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                if (sender is Window w && w.Icon == null)
                {
                    w.Icon = GetOrCreateWindowIcon();
                }
            }));

        // Biểu đồ LiveCharts2: Skia + giao diện sáng; Segoe UI để hiển thị đúng tiếng Việt/Nhật
        LiveChartsCore.LiveCharts.Configure(config => config
            .AddSkiaSharp()
            .AddDefaultMappers()
            .AddLightTheme()
            .HasTextSettings(new TextSettings
            {
                DefaultTypeface = SkiaSharp.SKTypeface.FromFamilyName("Segoe UI")
            }));

        // 1. Khởi tạo cấu hình ứng dụng từ appsettings.json
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables("SMARTPS_")
            .Build();

        SmartPS.Services.Common.AppMode.Load(configuration);
        ApplyGlobalFont();

        // 2. Cấu hình Service Collection (Dependency Injection)
        var services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(configuration);

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? DbConnectionHelper.DefaultConnectionString;

        // Chuỗi kết nối cho code ADO.NET thuần (không qua EF Core)
        services.AddSingleton(new DatabaseConnectionInfo(connectionString));

        // Cấu hình SQL Server (LocalDB mặc định)
        services.AddDbContextFactory<SmartPsDbContext>(options =>
        {
            options.UseSqlServer(connectionString);
        });

        // Đăng ký Business Services
        services.AddSingleton<ICurrentUserContext, CurrentUserContext>();
        services.AddSingleton<SmartPS.Services.Audit.IAuditService, SmartPS.Services.Audit.AuditService>();
        services.AddSingleton<IAuthorizationGuard, AuthorizationGuard>();
        services.AddSingleton<IAuthService, AuthService>();
        services.AddSingleton<ICredentialStore, CredentialStore>();
        services.AddSingleton<IPermissionService, PermissionService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<SmartPS.Services.RolePermissions.IRolePermissionService, SmartPS.Services.RolePermissions.RolePermissionService>();
        services.AddSingleton<SmartPS.Services.Audit.IAuditQueryService, SmartPS.Services.Audit.AuditQueryService>();
        services.AddSingleton<SmartPS.Services.Audit.IAuditIntegrityVerifier, SmartPS.Services.Audit.AuditIntegrityVerifier>();
        services.AddSingleton<SmartPS.Services.Preferences.IUserPreferencesStore, SmartPS.Services.Preferences.UserPreferencesStore>();
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<SmartPS.Services.Audio.IAudioAlertService, SmartPS.Services.Audio.SystemAudioAlertService>();
        services.AddSingleton<SmartPS.Services.Storage.IImageStorageService, SmartPS.Services.Storage.ImageStorageService>();
        services.AddSingleton<SmartPS.Services.Pricing.IPricingEngine, SmartPS.Services.Pricing.PricingEngine>();
        services.AddSingleton<SmartPS.Services.Pricing.IPricingService, SmartPS.Services.Pricing.PricingService>();
        services.AddSingleton<SmartPS.Services.GateControl.IParkingFeeCalculator, SmartPS.Services.GateControl.StandardParkingFeeCalculator>();
        services.AddSingleton<SmartPS.Services.Pricing.IParkingFeeEstimator, SmartPS.Services.Pricing.ParkingFeeEstimator>();
        services.AddSingleton<SmartPS.Services.OcrLisencePlate.IOcrLicensePlateService, SmartPS.Services.OcrLisencePlate.LicensePlateOcrService>();
        services.AddSingleton<SmartPS.Services.GateControl.IPlateOwnershipReader, SmartPS.Services.GateControl.PlateOwnershipReader>();
        services.AddSingleton<SmartPS.Services.GateControl.IGateControlService, SmartPS.Services.GateControl.GateControlService>();
        services.AddSingleton<SmartPS.Services.GateControl.ICameraWatcherService, SmartPS.Services.GateControl.CameraWatcherService>();

        // Cấu hình thanh toán điện tử (VietQR / PayOS)
        services.Configure<PayOSPaymentGatewayOptions>(configuration.GetSection("PayOS"));

        var payOsSection = configuration.GetSection("PayOS");
        var timeoutSeconds = 15;
        if (int.TryParse(payOsSection["TimeoutSeconds"], out var t) && t > 0)
        {
            timeoutSeconds = t;
        }

        services.AddHttpClient("PayOS", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        });

        // Kiểm tra cấu hình chọn PayOS thật hay Mock (UseMock)
        var useMockConfig = payOsSection["UseMock"];
        var clientId = payOsSection["ClientId"];
        var apiKey = payOsSection["ApiKey"];
        var checksumKey = payOsSection["ChecksumKey"];

        var hasPayOsKeys =
            !string.IsNullOrWhiteSpace(clientId) &&
            !string.IsNullOrWhiteSpace(apiKey) &&
            !string.IsNullOrWhiteSpace(checksumKey);

        bool useMock;
        if (!string.IsNullOrWhiteSpace(useMockConfig) && bool.TryParse(useMockConfig, out var parsedUseMock))
        {
            useMock = parsedUseMock;
        }
        else
        {
            useMock = !hasPayOsKeys;
        }

        if (useMock)
        {
            services.AddSingleton<IPaymentGateway, MockPaymentGateway>();
            System.Diagnostics.Debug.WriteLine(
                "[SmartPS Payment] Đang sử dụng DEMO/MOCK theo cấu hình (UseMock = true).");
        }
        else
        {
            if (!hasPayOsKeys)
            {
                System.Diagnostics.Debug.WriteLine(
                    "[SmartPS Payment Cảnh báo] Đang cấu hình dùng PayOS thật (UseMock = false) nhưng thiếu key (ClientId/ApiKey/ChecksumKey).");
            }
            services.AddSingleton<IPaymentGateway, PayOSPaymentGateway>();
        }

        services.AddSingleton<IPaymentService, PaymentService>();
        services.AddSingleton<IShiftService, ShiftService>();
        services.AddSingleton<ICustomerImportService, CustomerImportService>();
        services.AddSingleton<SmartPS.Services.Reports.IReportService, SmartPS.Services.Reports.ReportService>();
        services.AddSingleton<SmartPS.Services.Reports.IReportExportService, SmartPS.Services.Reports.ReportExportService>();
        services.AddSingleton<SmartPS.Services.Dialog.IFileDialogService, SmartPS.Services.Dialog.FileDialogService>();
        services.AddSingleton<IParkingZoneService, ParkingZoneService>();
        services.AddSingleton<SmartPS.Services.Customers.IVehiclesService, SmartPS.Services.Customers.VehiclesService>();
        services.AddSingleton<SmartPS.Services.Customers.ITicketsService, SmartPS.Services.Customers.TicketsService>();
        services.AddSingleton<SmartPS.Services.Customers.ICustomersService, SmartPS.Services.Customers.CustomersService>();
        services.AddSingleton<SmartPS.Services.Incidents.IIncidentService, SmartPS.Services.Incidents.IncidentService>();
        services.AddSingleton<SmartPS.Services.Settings.ISystemSettingsService, SmartPS.Services.Settings.SystemSettingsService>();
        services.AddSingleton<PaymentWebhookServer>();

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
        services.AddTransient<SmartPS.ViewModels.Shifts.ShiftsViewModel>();
        services.AddTransient<SmartPS.ViewModels.UserManagement.UserManagementViewModel>();
        services.AddTransient<SmartPS.ViewModels.Pricing.PricingViewModel>();
        services.AddTransient<SmartPS.ViewModels.Settings.SettingsViewModel>();
        services.AddTransient<SmartPS.ViewModels.RolePermissions.RolePermissionsViewModel>();
        services.AddTransient<SmartPS.ViewModels.Audit.AuditLogViewModel>();

        // Đăng ký Views
        services.AddTransient<LoginView>();
        services.AddTransient<DashboardView>();

        ServiceProvider = services.BuildServiceProvider();

        // 3. Tự động kiểm tra, tạo Database SQL Server (Code First) và nạp dữ liệu mặc định (Seed Data)
        var dbConnected = false;
        try
        {
            var dbContextFactory = ServiceProvider.GetRequiredService<IDbContextFactory<SmartPsDbContext>>();
            // LocalDB khởi động instance ở lần kết nối đầu tiên nên cần chờ lâu hơn máy chủ thường trực
            using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(connectCts.Token);

            // ExistsAsync ném lỗi khi không kết nối được máy chủ, trả false khi máy chủ chạy nhưng chưa có database;
            // khi đó MigrateAsync trong DbInitializer sẽ tự tạo database
            var databaseCreator = dbContext.GetService<IRelationalDatabaseCreator>();
            if (!await databaseCreator.ExistsAsync(connectCts.Token))
            {
                System.Diagnostics.Debug.WriteLine("[SmartPS SQL Server Auto-DB]: Chưa có database, sẽ tạo mới khi áp dụng migration.");
            }

            using var initializeCts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            await DbInitializer.InitializeAsync(dbContext, initializeCts.Token);
            dbConnected = true;
        }
        catch (Exception ex)
        {
            // Bắt lỗi kết nối nếu máy chủ SQL Server chưa cài hoặc chưa bật
            System.Diagnostics.Debug.WriteLine($"[SmartPS SQL Server Auto-DB Warning]: {ex.Message}");
        }

        // 4. Khởi động Webhook HTTP Server nền cho cổng thanh toán
        try
        {
            var webhookServer = ServiceProvider.GetRequiredService<PaymentWebhookServer>();
            _ = webhookServer.StartAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SmartPS Webhook Server Warning]: {ex.Message}");
        }

        // 5. Khởi tạo và hiển thị màn hình Đăng nhập
        var loginView = ServiceProvider.GetRequiredService<LoginView>();
        loginView.ViewModel.SetInitialDbStatus(dbConnected);
        loginView.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            var webhookServer = ServiceProvider?.GetService<PaymentWebhookServer>();
            if (webhookServer != null)
            {
                await webhookServer.StopAsync();
            }
        }
        catch { }

        try
        {
            var ocrService = ServiceProvider?.GetService<SmartPS.Services.OcrLisencePlate.IOcrLicensePlateService>();
            if (ocrService != null)
            {
                await ocrService.StopEngineAsync();
            }
        }
        catch { }

        if (ServiceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }

        base.OnExit(e);
    }
}
