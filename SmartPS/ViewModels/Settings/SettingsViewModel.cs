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
using SmartPS.Services.Localization;

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

    public string OverstayRangeHint => AppText.Get("Str_SetUi_OverstayRange", OperationalSettings.MinOverstayHours, OperationalSettings.MaxOverstayHours);
    public string VehiclesRangeHint => AppText.Get("Str_SetUi_VehiclesRange", OperationalSettings.MinVehiclesPerHousehold, OperationalSettings.MaxVehiclesPerHouseholdLimit);

    public bool CanManage => _permissionService.HasPermission(Permissions.SettingsManage);

    #endregion

    #region Tình trạng hệ thống

    private bool _isDatabaseOnline;
    public bool IsDatabaseOnline
    {
        get => _isDatabaseOnline;
        set => SetProperty(ref _isDatabaseOnline, value);
    }

    private string _databaseSummary = AppText.Get("Str_SetUi_Checking");
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

    private string _ocrSummary = AppText.Get("Str_SetUi_Checking");
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
        ? AppText.Get("Str_SetUi_PayOsLive")
        : AppText.Get("Str_SetUi_PayOsDemo");

    public string PaymentDetail => IsPaymentLive
        ? AppText.Get("Str_SetUi_PayOsLiveDetail")
        : AppText.Get("Str_SetUi_PayOsDemoDetail");

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

        AppText.Changed += OnLanguageChanged;

        _ = LoadDataAsync();
    }

    private DatabaseStatus? _lastDatabaseStatus;

    private void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(OverstayRangeHint));
        OnPropertyChanged(nameof(VehiclesRangeHint));
        OnPropertyChanged(nameof(PaymentSummary));
        OnPropertyChanged(nameof(PaymentDetail));
        ApplyStatusTexts();
    }

    public async Task LoadDataAsync()
    {
        var settings = await _settingsService.GetOperationalSettingsAsync();
        OverstayThresholdHours = settings.OverstayThresholdHours;
        MaxVehiclesPerHousehold = settings.MaxVehiclesPerHousehold;

        _lastDatabaseStatus = await _settingsService.GetDatabaseStatusAsync();
        IsDatabaseOnline = _lastDatabaseStatus.IsOnline;
        IsOcrAvailable = await _ocrService.EnsureEngineStartedAsync();
        IsWebhookRunning = _webhookServer.IsRunning;
        ApplyStatusTexts();
    }

    private void ApplyStatusTexts()
    {
        var db = _lastDatabaseStatus;
        if (db is null)
            return;

        if (db.IsOnline)
        {
            DatabaseSummary = db.PendingMigrations == 0
                ? AppText.Get("Str_SetUi_DbOnlineApplied", db.AppliedMigrations)
                : AppText.Get("Str_SetUi_DbOnlinePending", db.PendingMigrations);
            DatabaseDetail = AppText.Get("Str_SetUi_DbDetail", db.Server, db.Database, db.ServerVersion, db.LastMigration);
        }
        else
        {
            DatabaseSummary = AppText.Get("Str_SetUi_DbOffline");
            DatabaseDetail = $"{db.Server} / {db.Database} · {db.Error}";
        }

        OcrSummary = IsOcrAvailable ? AppText.Get("Str_SetUi_OcrReady") : AppText.Get("Str_SetUi_OcrMissing");
        OcrDetail = IsOcrAvailable
            ? _ocrService.EngineExecutablePath ?? string.Empty
            : AppText.Get("Str_SetUi_OcrMissingHint");

        WebhookSummary = IsWebhookRunning
            ? AppText.Get("Str_SetUi_WebhookListening", _webhookServer.ListeningUrl)
            : AppText.Get("Str_SetUi_WebhookStopped", _webhookServer.ListeningUrl);
    }

    private async Task SaveAsync()
    {
        var result = await _settingsService.UpdateOperationalSettingsAsync(
            new OperationalSettings(OverstayThresholdHours, MaxVehiclesPerHousehold));

        if (result.Success)
        {
            _dialogService.ShowSuccess(AppText.Get("Msg_SetUi_Saved"));
            await LoadDataAsync();
        }
        else if (result.IsPermissionDenied)
        {
            _dialogService.ShowWarning(AppText.Get("Msg_SetUi_NoPermission"));
        }
        else if (result.Error == OperationError.Validation)
        {
            _dialogService.ShowWarning(result.Message);
        }
        else
        {
            _dialogService.ShowError(AppText.Get("Msg_SetUi_SaveFailed", result.Message));
        }
    }

    private async Task TestOcrAsync()
    {
        var imagePath = _dialogService.ShowOpenFileDialog(title: AppText.Get("Str_SetUi_PickTestImage"));
        if (string.IsNullOrEmpty(imagePath)) return;

        IsOcrTesting = true;
        OcrTestResult = AppText.Get("Str_SetUi_Recognizing");
        try
        {
            var response = await _ocrService.RecognizePlateAsync(imagePath);
            OcrTestResult = response.IsSuccess
                ? AppText.Get("Str_SetUi_OcrResult", response.PrimaryPlateText, response.PrimaryOcrConfidence, response.TotalMilliseconds / 1000)
                : AppText.Get("Str_SetUi_OcrFailed", response.Error?.Message ?? AppText.Get("Str_GateUi_NoPlateDetected"));
        }
        finally
        {
            IsOcrTesting = false;
        }
    }
}
