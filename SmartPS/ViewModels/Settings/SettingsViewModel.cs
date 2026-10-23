using System.IO;
using System.Reflection;
using SmartPS.Constants;
using SmartPS.Services.Authorization;
using SmartPS.Services.Common;
using SmartPS.Services.Dialog;
using SmartPS.Services.OcrLisencePlate;
using SmartPS.Services.Payment;
using SmartPS.Services.Payment.Mock;
using SmartPS.Services.Payment.Webhook;
using SmartPS.Services.Settings;

namespace SmartPS.ViewModels.Settings;

public class SettingsViewModel : ViewModelBase
{
    private readonly ISystemSettingsService _settingsService;
    private readonly IOcrLicensePlateService _ocrService;
    private readonly IPaymentGateway _paymentGateway;
    private readonly PaymentWebhookServer _webhookServer;
    private readonly IDialogService _dialogService;
    private readonly IPermissionService _permissionService;

    #region Tham số vận hành

    private int _overstayThresholdHours;
    public int OverstayThresholdHours
    {
        get => _overstayThresholdHours;
        set => SetProperty(ref _overstayThresholdHours, value);
    }

    private int _maxVehiclesPerHousehold;
    public int MaxVehiclesPerHousehold
    {
        get => _maxVehiclesPerHousehold;
        set => SetProperty(ref _maxVehiclesPerHousehold, value);
    }

    public string OverstayRangeHint => $"Từ {OperationalSettings.MinOverstayHours} đến {OperationalSettings.MaxOverstayHours} giờ";
    public string VehiclesRangeHint => $"Từ {OperationalSettings.MinVehiclesPerHousehold} đến {OperationalSettings.MaxVehiclesPerHouseholdLimit} xe";

    public bool CanManage => _permissionService.HasPermission(Permissions.SettingsManage);

    #endregion

    #region Tình trạng hệ thống

    private bool _isDatabaseOnline;
    public bool IsDatabaseOnline
    {
        get => _isDatabaseOnline;
        set => SetProperty(ref _isDatabaseOnline, value);
    }

    private string _databaseSummary = "Đang kiểm tra...";
    public string DatabaseSummary
    {
        get => _databaseSummary;
        set => SetProperty(ref _databaseSummary, value);
    }

    private string _databaseDetail = string.Empty;
    public string DatabaseDetail
    {
        get => _databaseDetail;
        set => SetProperty(ref _databaseDetail, value);
    }

    private bool _isOcrAvailable;
    public bool IsOcrAvailable
    {
        get => _isOcrAvailable;
        set => SetProperty(ref _isOcrAvailable, value);
    }

    private string _ocrSummary = "Đang kiểm tra...";
    public string OcrSummary
    {
        get => _ocrSummary;
        set => SetProperty(ref _ocrSummary, value);
    }

    private string _ocrDetail = string.Empty;
    public string OcrDetail
    {
        get => _ocrDetail;
        set => SetProperty(ref _ocrDetail, value);
    }

    private string _ocrTestResult = string.Empty;
    public string OcrTestResult
    {
        get => _ocrTestResult;
        set => SetProperty(ref _ocrTestResult, value);
    }

    private bool _isOcrTesting;
    public bool IsOcrTesting
    {
        get => _isOcrTesting;
        set => SetProperty(ref _isOcrTesting, value);
    }

    public bool IsPaymentLive => _paymentGateway is not MockPaymentGateway;

    public string PaymentSummary => IsPaymentLive
        ? "PayOS (VietQR thật)"
        : "Chế độ DEMO (chưa cấu hình khóa PayOS)";

    public string PaymentDetail => IsPaymentLive
        ? "Thanh toán VietQR được tạo qua cổng PayOS."
        : "Thêm ClientId, ApiKey, ChecksumKey vào mục PayOS trong appsettings.local.json để bật thanh toán thật (xem PAYMENT_SETUP.md).";

    private bool _isWebhookRunning;
    public bool IsWebhookRunning
    {
        get => _isWebhookRunning;
        set => SetProperty(ref _isWebhookRunning, value);
    }

    private string _webhookSummary = string.Empty;
    public string WebhookSummary
    {
        get => _webhookSummary;
        set => SetProperty(ref _webhookSummary, value);
    }

    public string CaptureStoragePath { get; } = Path.Combine(AppContext.BaseDirectory, "Storage", "Captures");

    public string AppVersion { get; } = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0";

    #endregion

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand SaveCommand { get; }
    public AsyncRelayCommand TestOcrCommand { get; }

    public SettingsViewModel(
        ISystemSettingsService settingsService,
        IOcrLicensePlateService ocrService,
        IPaymentGateway paymentGateway,
        PaymentWebhookServer webhookServer,
        IDialogService dialogService,
        IPermissionService permissionService)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _ocrService = ocrService ?? throw new ArgumentNullException(nameof(ocrService));
        _paymentGateway = paymentGateway ?? throw new ArgumentNullException(nameof(paymentGateway));
        _webhookServer = webhookServer ?? throw new ArgumentNullException(nameof(webhookServer));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _permissionService = permissionService ?? throw new ArgumentNullException(nameof(permissionService));

        RefreshCommand = new AsyncRelayCommand(LoadDataAsync);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => CanManage);
        TestOcrCommand = new AsyncRelayCommand(TestOcrAsync, () => IsOcrAvailable && !IsOcrTesting);

        _ = LoadDataAsync();
    }

    public async Task LoadDataAsync()
    {
        var settings = await _settingsService.GetOperationalSettingsAsync();
        OverstayThresholdHours = settings.OverstayThresholdHours;
        MaxVehiclesPerHousehold = settings.MaxVehiclesPerHousehold;

        var db = await _settingsService.GetDatabaseStatusAsync();
        IsDatabaseOnline = db.IsOnline;
        if (db.IsOnline)
        {
            DatabaseSummary = db.PendingMigrations == 0
                ? $"Đang kết nối · {db.AppliedMigrations} migration đã áp dụng"
                : $"Đang kết nối · còn {db.PendingMigrations} migration chưa áp dụng";
            DatabaseDetail = $"{db.Host}:{db.Port} / {db.Database} · PostgreSQL {db.ServerVersion} · Migration mới nhất: {db.LastMigration}";
        }
        else
        {
            DatabaseSummary = "Không kết nối được máy chủ PostgreSQL";
            DatabaseDetail = $"{db.Host}:{db.Port} / {db.Database} · {db.Error}";
        }

        IsOcrAvailable = await _ocrService.EnsureEngineStartedAsync();
        OcrSummary = IsOcrAvailable ? "Sẵn sàng" : "Không tìm thấy LicensePlateEngine.exe";
        OcrDetail = IsOcrAvailable
            ? _ocrService.EngineExecutablePath ?? string.Empty
            : "Đặt LicensePlateEngine.exe vào thư mục Services\\OcrLisencePlate cạnh ứng dụng.";

        IsWebhookRunning = _webhookServer.IsRunning;
        WebhookSummary = IsWebhookRunning
            ? $"Đang lắng nghe tại {_webhookServer.ListeningUrl}"
            : $"Chưa chạy ({_webhookServer.ListeningUrl})";
    }

    private async Task SaveAsync()
    {
        var result = await _settingsService.UpdateOperationalSettingsAsync(
            new OperationalSettings(OverstayThresholdHours, MaxVehiclesPerHousehold));

        if (result.Success)
        {
            _dialogService.ShowSuccess("Đã lưu tham số vận hành.");
            await LoadDataAsync();
        }
        else if (result.IsPermissionDenied)
        {
            _dialogService.ShowWarning("Bạn không có quyền thay đổi cài đặt hệ thống.");
        }
        else if (result.Error == OperationError.Validation)
        {
            _dialogService.ShowWarning(result.Message);
        }
        else
        {
            _dialogService.ShowError($"Không thể lưu cài đặt: {result.Message}");
        }
    }

    private async Task TestOcrAsync()
    {
        var imagePath = _dialogService.ShowOpenFileDialog(title: "Chọn ảnh xe để chạy thử nhận diện biển số");
        if (string.IsNullOrEmpty(imagePath)) return;

        IsOcrTesting = true;
        OcrTestResult = "⚡ Đang nhận diện...";
        try
        {
            var response = await _ocrService.RecognizePlateAsync(imagePath);
            OcrTestResult = response.IsSuccess
                ? $"✅ {response.PrimaryPlateText} · độ tin cậy {response.PrimaryOcrConfidence:P0} · {response.TotalMilliseconds / 1000:0.0}s"
                : $"⚠️ {response.Error?.Message ?? "Không phát hiện biển số"}";
        }
        finally
        {
            IsOcrTesting = false;
        }
    }
}
