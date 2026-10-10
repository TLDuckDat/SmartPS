using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using SmartPS.Models.GateControl;
using SmartPS.Models.Ocr;
using SmartPS.Models.Parking;
using SmartPS.Models.Payment;
using SmartPS.Services.Audio;
using SmartPS.Services.Auth;
using SmartPS.Services.Dialog;
using SmartPS.Services.GateControl;
using SmartPS.Services.Localization;
using SmartPS.Services.OcrLisencePlate;
using SmartPS.Services.Payment;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SmartPS.ViewModels.GateControl;

public class GateControlViewModel : ViewModelBase
{
    private readonly IOcrLicensePlateService _ocrService;
    private readonly IGateControlService _gateControlService;
    private readonly ICameraWatcherService _cameraWatcherService;
    private readonly IDialogService _dialogService;
    private readonly ILocalizationService _localizationService;
    private readonly IAuthService _authService;
    private readonly IAudioAlertService _audioAlertService;
    private readonly IPaymentService? _paymentService;

    private readonly DispatcherTimer _barrierInTimer;
    private readonly DispatcherTimer _barrierOutTimer;
    private readonly DispatcherTimer _vietQrPollTimer;

    // Danh sách loại xe và phiên gửi xe đang hoạt động trong bãi
    public ObservableCollection<VehicleType> VehicleTypes { get; } = new();
    public ObservableCollection<ParkingSession> ActiveSessions { get; } = new();

    #region Chế Độ Tự Động Hóa (Smart Auto Gate)

    private bool _isAutoModeEnabled = true;
    public bool IsAutoModeEnabled
    {
        get => _isAutoModeEnabled;
        set
        {
            if (SetProperty(ref _isAutoModeEnabled, value))
            {
                OnPropertyChanged(nameof(AutoModeStatusText));
                OnPropertyChanged(nameof(AutoModeBadgeBackground));
                OnPropertyChanged(nameof(AutoModeBadgeBorder));
                OnPropertyChanged(nameof(AutoModeBadgeForeground));
            }
        }
    }

    public string AutoModeStatusText => IsAutoModeEnabled ? "TỰ ĐỘNG HÓA: BẬT" : "TỰ ĐỘNG HÓA: TẮT";
    public string AutoModeBadgeBackground => IsAutoModeEnabled ? "#DCFCE7" : "#F1F5F9";
    public string AutoModeBadgeBorder => IsAutoModeEnabled ? "#86EFAC" : "#CBD5E1";
    public string AutoModeBadgeForeground => IsAutoModeEnabled ? "#15803D" : "#64748B";

    private bool _isAutoCheckoutAllVehicles = true;
    public bool IsAutoCheckoutAllVehicles
    {
        get => _isAutoCheckoutAllVehicles;
        set => SetProperty(ref _isAutoCheckoutAllVehicles, value);
    }

    private bool _isCameraWatcherActive;
    public bool IsCameraWatcherActive
    {
        get => _isCameraWatcherActive;
        set
        {
            if (SetProperty(ref _isCameraWatcherActive, value))
            {
                if (value)
                    _cameraWatcherService.Start();
                else
                    _cameraWatcherService.Stop();

                OnPropertyChanged(nameof(CameraWatcherStatusText));
            }
        }
    }

    public string CameraWatcherStatusText => IsCameraWatcherActive ? "📡 Giám sát Camera: Đang chạy" : "📡 Giám sát Camera: Đã dừng";

    private bool _isSimulationRunning;
    public bool IsSimulationRunning
    {
        get => _isSimulationRunning;
        set => SetProperty(ref _isSimulationRunning, value);
    }

    #endregion

    #region Làn Vào (In Gate) Properties

    private string? _inImagePath;
    public string? InImagePath
    {
        get => _inImagePath;
        set => SetProperty(ref _inImagePath, value);
    }

    private string? _inAnnotatedImagePath;
    public string? InAnnotatedImagePath
    {
        get => _inAnnotatedImagePath;
        set => SetProperty(ref _inAnnotatedImagePath, value);
    }

    private string? _inCropImagePath;
    public string? InCropImagePath
    {
        get => _inCropImagePath;
        set => SetProperty(ref _inCropImagePath, value);
    }

    private string _inPlateText = string.Empty;
    public string InPlateText
    {
        get => _inPlateText;
        set
        {
            if (SetProperty(ref _inPlateText, value))
            {
                _ = CheckMonthlyPassInAsync(value);
            }
        }
    }

    private double _inConfidence;
    public double InConfidence
    {
        get => _inConfidence;
        set
        {
            if (SetProperty(ref _inConfidence, value))
            {
                OnPropertyChanged(nameof(InConfidenceFormatted));
            }
        }
    }
    public string InConfidenceFormatted => _inConfidence > 0 ? $"{_inConfidence * 100:0.0}%" : "0%";

    private double _inProcessingTimeMs;
    public double InProcessingTimeMs
    {
        get => _inProcessingTimeMs;
        set => SetProperty(ref _inProcessingTimeMs, value);
    }

    private VehicleType? _inSelectedVehicleType;
    public VehicleType? InSelectedVehicleType
    {
        get => _inSelectedVehicleType;
        set
        {
            if (SetProperty(ref _inSelectedVehicleType, value))
            {
                _ = SuggestSlotInAsync();
            }
        }
    }

    private string? _inSuggestedSlotCode;
    public string? InSuggestedSlotCode
    {
        get => _inSuggestedSlotCode;
        set => SetProperty(ref _inSuggestedSlotCode, value);
    }

    private string? _inCustomerBadge = "Khách vãng lai";
    public string? InCustomerBadge
    {
        get => _inCustomerBadge;
        set => SetProperty(ref _inCustomerBadge, value);
    }

    private bool _isMonthlyTicketIn;
    public bool IsMonthlyTicketIn
    {
        get => _isMonthlyTicketIn;
        set => SetProperty(ref _isMonthlyTicketIn, value);
    }

    private bool _isBarrierInOpen;
    public bool IsBarrierInOpen
    {
        get => _isBarrierInOpen;
        set => SetProperty(ref _isBarrierInOpen, value);
    }

    private bool _isInOcrBusy;
    public bool IsInOcrBusy
    {
        get => _isInOcrBusy;
        set => SetProperty(ref _isInOcrBusy, value);
    }

    private string _inStatusMessage = "Sẵn sàng đón xe vào";
    public string InStatusMessage
    {
        get => _inStatusMessage;
        set => SetProperty(ref _inStatusMessage, value);
    }

    private string? _lastInTimeFormatted;
    public string? LastInTimeFormatted
    {
        get => _lastInTimeFormatted;
        set => SetProperty(ref _lastInTimeFormatted, value);
    }

    private string? _lastInTicketCode;
    public string? LastInTicketCode
    {
        get => _lastInTicketCode;
        set => SetProperty(ref _lastInTicketCode, value);
    }

    private string? _lastInSlotCode;
    public string? LastInSlotCode
    {
        get => _lastInSlotCode;
        set => SetProperty(ref _lastInSlotCode, value);
    }

    #endregion

    #region Làn Ra (Out Gate) Properties

    private string? _outImagePath;
    public string? OutImagePath
    {
        get => _outImagePath;
        set => SetProperty(ref _outImagePath, value);
    }

    private string? _outAnnotatedImagePath;
    public string? OutAnnotatedImagePath
    {
        get => _outAnnotatedImagePath;
        set => SetProperty(ref _outAnnotatedImagePath, value);
    }

    private string? _outCropImagePath;
    public string? OutCropImagePath
    {
        get => _outCropImagePath;
        set => SetProperty(ref _outCropImagePath, value);
    }

    private string _outPlateText = string.Empty;
    public string OutPlateText
    {
        get => _outPlateText;
        set
        {
            if (SetProperty(ref _outPlateText, value))
            {
                _ = MatchSessionOutAsync(value);
            }
        }
    }

    private double _outConfidence;
    public double OutConfidence
    {
        get => _outConfidence;
        set
        {
            if (SetProperty(ref _outConfidence, value))
            {
                OnPropertyChanged(nameof(OutConfidenceFormatted));
            }
        }
    }
    public string OutConfidenceFormatted => _outConfidence > 0 ? $"{_outConfidence * 100:0.0}%" : "0%";

    private double _outProcessingTimeMs;
    public double OutProcessingTimeMs
    {
        get => _outProcessingTimeMs;
        set => SetProperty(ref _outProcessingTimeMs, value);
    }

    private ParkingSession? _matchedSession;
    public ParkingSession? MatchedSession
    {
        get => _matchedSession;
        set
        {
            if (SetProperty(ref _matchedSession, value))
            {
                EvaluatePlateMatch();
            }
        }
    }

    private bool? _isPlateMatched;
    public bool? IsPlateMatched
    {
        get => _isPlateMatched;
        set
        {
            if (SetProperty(ref _isPlateMatched, value))
            {
                OnPropertyChanged(nameof(PlateMatchStatusText));
                OnPropertyChanged(nameof(PlateMatchBadgeColor));
            }
        }
    }

    public string PlateMatchStatusText => IsPlateMatched switch
    {
        true => "TRÙNG KHỚP BIỂN SỐ VÀO/RA",
        false => "CẢNH BÁO: BIỂN SỐ KHÔNG KHỚP!",
        _ => "Chưa đối soát"
    };

    public string PlateMatchBadgeColor => IsPlateMatched switch
    {
        true => "#16A34A",   // Green
        false => "#DC2626",  // Red
        _ => "#64748B"       // Slate Gray
    };

    private string _durationFormatted = "--:--";
    public string DurationFormatted
    {
        get => _durationFormatted;
        set => SetProperty(ref _durationFormatted, value);
    }

    private decimal _calculatedFee;
    public decimal CalculatedFee
    {
        get => _calculatedFee;
        set
        {
            if (SetProperty(ref _calculatedFee, value))
            {
                OnPropertyChanged(nameof(CalculatedFeeFormatted));
            }
        }
    }
    public string CalculatedFeeFormatted => $"{CalculatedFee:N0} đ";

    private string _feeDetails = string.Empty;
    public string FeeDetails
    {
        get => _feeDetails;
        set => SetProperty(ref _feeDetails, value);
    }

    private bool _isMonthlyTicketOut;
    public bool IsMonthlyTicketOut
    {
        get => _isMonthlyTicketOut;
        set => SetProperty(ref _isMonthlyTicketOut, value);
    }

    private PaymentMethod _selectedPaymentMethod = PaymentMethod.VietQR;
    public PaymentMethod SelectedPaymentMethod
    {
        get => _selectedPaymentMethod;
        set => SetProperty(ref _selectedPaymentMethod, value);
    }

    public ObservableCollection<PaymentMethod> AvailablePaymentMethods { get; } = new()
    {
        PaymentMethod.Cash,
        PaymentMethod.VietQR,
        PaymentMethod.Card
    };

    private bool _isBarrierOutOpen;
    public bool IsBarrierOutOpen
    {
        get => _isBarrierOutOpen;
        set => SetProperty(ref _isBarrierOutOpen, value);
    }

    private bool _isOutOcrBusy;
    public bool IsOutOcrBusy
    {
        get => _isOutOcrBusy;
        set => SetProperty(ref _isOutOcrBusy, value);
    }

    private string _outStatusMessage = "Sẵn sàng đối soát xe ra";
    public string OutStatusMessage
    {
        get => _outStatusMessage;
        set => SetProperty(ref _outStatusMessage, value);
    }

    #endregion

    #region VietQR Payment Modal Properties

    private bool _isVietQrModalOpen;
    public bool IsVietQrModalOpen
    {
        get => _isVietQrModalOpen;
        set => SetProperty(ref _isVietQrModalOpen, value);
    }

    private bool _isVietQrLoading;
    public bool IsVietQrLoading
    {
        get => _isVietQrLoading;
        set => SetProperty(ref _isVietQrLoading, value);
    }

    private bool _isVietQrPaidSuccess;
    public bool IsVietQrPaidSuccess
    {
        get => _isVietQrPaidSuccess;
        set => SetProperty(ref _isVietQrPaidSuccess, value);
    }

    private int _vietQrPaymentId;
    public int VietQrPaymentId
    {
        get => _vietQrPaymentId;
        set => SetProperty(ref _vietQrPaymentId, value);
    }

    private string _vietQrTransactionReference = string.Empty;
    public string VietQrTransactionReference
    {
        get => _vietQrTransactionReference;
        set => SetProperty(ref _vietQrTransactionReference, value);
    }

    private decimal _vietQrAmount;
    public decimal VietQrAmount
    {
        get => _vietQrAmount;
        set
        {
            if (SetProperty(ref _vietQrAmount, value))
            {
                OnPropertyChanged(nameof(VietQrAmountFormatted));
            }
        }
    }
    public string VietQrAmountFormatted => $"{VietQrAmount:N0} đ";

    private string _vietQrStatusText = "Đang tạo mã VietQR...";
    public string VietQrStatusText
    {
        get => _vietQrStatusText;
        set => SetProperty(ref _vietQrStatusText, value);
    }

    private ImageSource? _vietQrImageSource;
    public ImageSource? VietQrImageSource
    {
        get => _vietQrImageSource;
        set => SetProperty(ref _vietQrImageSource, value);
    }

    private string _vietQrDescription = string.Empty;
    public string VietQrDescription
    {
        get => _vietQrDescription;
        set => SetProperty(ref _vietQrDescription, value);
    }

    #endregion

    private ParkingSession? _selectedActiveSession;
    public ParkingSession? SelectedActiveSession
    {
        get => _selectedActiveSession;
        set
        {
            if (SetProperty(ref _selectedActiveSession, value) && value != null)
            {
                LoadSessionToOutLane(value);
            }
        }
    }

    #region Commands

    public RelayCommand ToggleAutoModeCommand { get; }
    public RelayCommand ToggleCameraWatcherCommand { get; }
    public AsyncRelayCommand RunAutoSimulationCommand { get; }

    public RelayCommand SelectInImageCommand { get; }
    public AsyncRelayCommand QuickTestInCommand { get; }
    public AsyncRelayCommand RunInOcrCommand { get; }
    public AsyncRelayCommand ConfirmCheckInCommand { get; }
    public RelayCommand ToggleBarrierInCommand { get; }

    public RelayCommand SelectOutImageCommand { get; }
    public AsyncRelayCommand QuickTestOutCommand { get; }
    public AsyncRelayCommand RunOutOcrCommand { get; }
    public AsyncRelayCommand ConfirmCheckOutCommand { get; }
    public RelayCommand ToggleBarrierOutCommand { get; }

    public AsyncRelayCommand RefreshActiveSessionsCommand { get; }

    public AsyncRelayCommand CancelVietQrPaymentCommand { get; }
    public AsyncRelayCommand RefreshVietQrStatusCommand { get; }

    #endregion

    public GateControlViewModel(
        IOcrLicensePlateService ocrService,
        IGateControlService gateControlService,
        ICameraWatcherService cameraWatcherService,
        IDialogService dialogService,
        ILocalizationService localizationService,
        IAuthService authService,
        IAudioAlertService? audioAlertService = null,
        IPaymentService? paymentService = null)
    {
        _ocrService = ocrService ?? throw new ArgumentNullException(nameof(ocrService));
        _gateControlService = gateControlService ?? throw new ArgumentNullException(nameof(gateControlService));
        _cameraWatcherService = cameraWatcherService ?? throw new ArgumentNullException(nameof(cameraWatcherService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _audioAlertService = audioAlertService ?? new SystemAudioAlertService();
        _paymentService = paymentService;

        // Lắng nghe sự kiện kết thúc phiên gửi xe (qua Webhook hoặc thanh toán)
        _gateControlService.SessionCompleted += OnSessionCompleted;

        // Timer polling kiểm tra trạng thái thanh toán VietQR
        _vietQrPollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _vietQrPollTimer.Tick += OnVietQrPollTick;

        // Lắng nghe sự kiện ảnh từ Camera Watcher
        _cameraWatcherService.ImageArrivedAtInLane += OnCameraImageIn;
        _cameraWatcherService.ImageArrivedAtOutLane += OnCameraImageOut;

        // Timer tự động đóng barrier sau 3 giây
        _barrierInTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _barrierInTimer.Tick += (s, e) =>
        {
            IsBarrierInOpen = false;
            _barrierInTimer.Stop();
        };

        _barrierOutTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _barrierOutTimer.Tick += (s, e) =>
        {
            IsBarrierOutOpen = false;
            _barrierOutTimer.Stop();
        };

        // Commands Tự động
        ToggleAutoModeCommand = new RelayCommand(() => IsAutoModeEnabled = !IsAutoModeEnabled);
        ToggleCameraWatcherCommand = new RelayCommand(() => IsCameraWatcherActive = !IsCameraWatcherActive);
        RunAutoSimulationCommand = new AsyncRelayCommand(ExecuteAutoSimulationAsync);

        // Commands Làn Vào
        SelectInImageCommand = new RelayCommand(ExecuteSelectInImage);
        QuickTestInCommand = new AsyncRelayCommand(ExecuteQuickTestInAsync);
        RunInOcrCommand = new AsyncRelayCommand(ExecuteRunInOcrAsync);
        ConfirmCheckInCommand = new AsyncRelayCommand(ExecuteConfirmCheckInAsync);
        ToggleBarrierInCommand = new RelayCommand(() => IsBarrierInOpen = !IsBarrierInOpen);

        // Commands Làn Ra
        SelectOutImageCommand = new RelayCommand(ExecuteSelectOutImage);
        QuickTestOutCommand = new AsyncRelayCommand(ExecuteQuickTestOutAsync);
        RunOutOcrCommand = new AsyncRelayCommand(ExecuteRunOutOcrAsync);
        ConfirmCheckOutCommand = new AsyncRelayCommand(ExecuteConfirmCheckOutAsync);
        ToggleBarrierOutCommand = new RelayCommand(() => IsBarrierOutOpen = !IsBarrierOutOpen);

        // Commands Thanh Toán VietQR
        CancelVietQrPaymentCommand = new AsyncRelayCommand(ExecuteCancelVietQrPaymentAsync);
        RefreshVietQrStatusCommand = new AsyncRelayCommand(ExecuteRefreshVietQrStatusAsync);

        RefreshActiveSessionsCommand = new AsyncRelayCommand(LoadActiveSessionsAsync);

        _ = InitializeDataAsync();
    }

    private void OnCameraImageIn(string filePath)
    {
        Application.Current?.Dispatcher.InvokeAsync(async () =>
        {
            InImagePath = filePath;
            InAnnotatedImagePath = null;
            InCropImagePath = null;
            await ProcessInLanePipelineAsync(isAutoTrigger: true);
        });
    }

    private void OnCameraImageOut(string filePath)
    {
        Application.Current?.Dispatcher.InvokeAsync(async () =>
        {
            OutImagePath = filePath;
            OutAnnotatedImagePath = null;
            OutCropImagePath = null;
            await ProcessOutLanePipelineAsync(isAutoTrigger: true);
        });
    }

    private async Task InitializeDataAsync()
    {
        try
        {
            var types = await _gateControlService.GetVehicleTypesAsync();
            VehicleTypes.Clear();
            foreach (var t in types)
            {
                VehicleTypes.Add(t);
            }
            InSelectedVehicleType = VehicleTypes.FirstOrDefault();

            await LoadActiveSessionsAsync();

            // Khởi động trước Engine OCR ngầm để sẵn sàng nhận diện tức thì
            _ = Task.Run(async () =>
            {
                try
                {
                    await _ocrService.EnsureEngineStartedAsync();
                }
                catch { }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GateControlViewModel Init Error]: {ex.Message}");
        }
    }

    public async Task LoadActiveSessionsAsync()
    {
        try
        {
            var sessions = await _gateControlService.GetActiveSessionsAsync();
            ActiveSessions.Clear();
            foreach (var s in sessions)
            {
                ActiveSessions.Add(s);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GateControlViewModel LoadActiveSessions Error]: {ex.Message}");
        }
    }

    #region Làn Vào Methods & Auto Pipeline

    private void ExecuteSelectInImage()
    {
        var filePath = _dialogService.ShowOpenFileDialog(
            filter: "Tệp hình ảnh (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp|Tất cả tệp (*.*)|*.*",
            title: "Chọn ảnh phương tiện vào bãi");

        if (!string.IsNullOrEmpty(filePath))
        {
            InImagePath = filePath;
            InAnnotatedImagePath = null;
            InCropImagePath = null;
            _ = ProcessInLanePipelineAsync(isAutoTrigger: IsAutoModeEnabled);
        }
    }

    private async Task ExecuteQuickTestInAsync()
    {
        var sampleImage = FindSampleImage();
        if (string.IsNullOrEmpty(sampleImage) || !File.Exists(sampleImage))
        {
            _dialogService.ShowWarning("Chưa tìm thấy ảnh mẫu kiểm thử trong thư mục dịch vụ.");
            return;
        }

        InImagePath = sampleImage;
        InAnnotatedImagePath = null;
        InCropImagePath = null;
        await ProcessInLanePipelineAsync(isAutoTrigger: IsAutoModeEnabled);
    }

    private async Task ExecuteRunInOcrAsync()
    {
        await ProcessInLanePipelineAsync(isAutoTrigger: false);
    }

    /// <summary>
    /// Pipeline Làn Vào: Tự động chạy OCR -> Nhận diện biển số -> Check-in -> Mở barrier nếu bật Auto Mode
    /// </summary>
    public async Task<bool> ProcessInLanePipelineAsync(bool isAutoTrigger)
    {
        if (string.IsNullOrEmpty(InImagePath) || !File.Exists(InImagePath))
        {
            if (!isAutoTrigger) _dialogService.ShowWarning("Vui lòng chọn ảnh xe vào trước khi nhận diện OCR.");
            return false;
        }

        try
        {
            IsInOcrBusy = true;
            InStatusMessage = "⚡ Đang chạy AI OCR nhận diện biển số xe vào...";

            var response = await _ocrService.RecognizePlateAsync(InImagePath);
            if (response.IsSuccess && response.Plates.Any())
            {
                var plate = response.Plates.First();
                InPlateText = plate.PlateText;
                InConfidence = plate.OcrConfidence > 0 ? plate.OcrConfidence : plate.DetectionConfidence;
                InProcessingTimeMs = response.TotalMilliseconds;

                InAnnotatedImagePath = !string.IsNullOrEmpty(response.OutputImage) && File.Exists(response.OutputImage)
                    ? response.OutputImage
                    : InImagePath;

                InCropImagePath = !string.IsNullOrEmpty(plate.CropPath) && File.Exists(plate.CropPath)
                    ? plate.CropPath
                    : null;

                InStatusMessage = $"Đã nhận diện: {InPlateText} ({InConfidenceFormatted} - {InProcessingTimeMs:0.0}ms)";

                // NẾU BẬT CHẾ ĐỘ TỰ ĐỘNG -> TỰ ĐỘNG XÁC NHẬN XE VÀO & MỞ BARRIER!
                if (isAutoTrigger || IsAutoModeEnabled)
                {
                    InStatusMessage = $"⚡ [TỰ ĐỘNG] Biển số: {InPlateText} -> Đang tự động lưu phiên & Mở Barrier...";
                    await Task.Delay(200); // Đệm mượt hiệu ứng
                    var checkInSuccess = await PerformCheckInAsync(silent: true);
                    return checkInSuccess;
                }
                return true;
            }
            else
            {
                InStatusMessage = $"Không nhận diện được biển số: {response.Error?.Message ?? "Không phát hiện biển số"}";
                if (!isAutoTrigger) _dialogService.ShowWarning(InStatusMessage);
                return false;
            }
        }
        catch (Exception ex)
        {
            InStatusMessage = $"Lỗi OCR Làn vào: {ex.Message}";
            if (!isAutoTrigger) _dialogService.ShowError(InStatusMessage);
            return false;
        }
        finally
        {
            IsInOcrBusy = false;
        }
    }

    private async Task CheckMonthlyPassInAsync(string plate)
    {
        if (string.IsNullOrWhiteSpace(plate))
        {
            InCustomerBadge = "Khách vãng lai";
            IsMonthlyTicketIn = false;
            return;
        }

        var blacklisted = await _gateControlService.CheckBlacklistAsync(plate);
        if (blacklisted != null)
        {
            IsMonthlyTicketIn = false;
            InCustomerBadge = $"⛔ XE BỊ CẤM: {blacklisted.Reason}";
            return;
        }

        var ticket = await _gateControlService.FindActiveMonthlyTicketAsync(plate);
        if (ticket != null)
        {
            IsMonthlyTicketIn = true;
            InCustomerBadge = $"👑 VÉ THÁNG: {ticket.Customer?.FullName ?? "Cư dân"} (Hiệu lực: {ticket.EndDate:dd/MM/yyyy})";
            if (ticket.VehicleTypeId > 0 && VehicleTypes.Any(v => v.VehicleTypeId == ticket.VehicleTypeId))
            {
                InSelectedVehicleType = VehicleTypes.First(v => v.VehicleTypeId == ticket.VehicleTypeId);
            }
        }
        else
        {
            IsMonthlyTicketIn = false;
            var vehicle = await _gateControlService.FindVehicleOwnerAsync(plate);
            if (vehicle != null && (vehicle.OwnerCustomer?.Type == CustomerType.Resident || vehicle.OwnerCustomer?.HouseholdId.HasValue == true))
            {
                var apt = !string.IsNullOrEmpty(vehicle.OwnerCustomer?.Household?.ApartmentCode) ? $" - P.{vehicle.OwnerCustomer.Household.ApartmentCode}" : "";
                InCustomerBadge = $"🏠 CƯ DÂN (Chưa có vé tháng): {vehicle.OwnerCustomer?.FullName}{apt}";
                if (vehicle.VehicleTypeId > 0 && VehicleTypes.Any(v => v.VehicleTypeId == vehicle.VehicleTypeId))
                {
                    InSelectedVehicleType = VehicleTypes.First(v => v.VehicleTypeId == vehicle.VehicleTypeId);
                }
            }
            else
            {
                InCustomerBadge = "Khách vãng lai";
            }
        }
    }

    private async Task SuggestSlotInAsync()
    {
        if (InSelectedVehicleType == null) return;
        var slot = await _gateControlService.SuggestAvailableSlotAsync(InSelectedVehicleType.VehicleTypeId);
        InSuggestedSlotCode = slot?.SlotCode ?? "Tự do";
    }

    private async Task ExecuteConfirmCheckInAsync()
    {
        await PerformCheckInAsync(silent: false);
    }

    private async Task<bool> PerformCheckInAsync(bool silent)
    {
        if (string.IsNullOrWhiteSpace(InPlateText))
        {
            if (!silent) _dialogService.ShowWarning("Vui lòng nhập hoặc chụp ảnh nhận diện biển số xe vào.");
            return false;
        }

        var request = new GateCheckInRequest
        {
            LicensePlate = InPlateText.Trim(),
            ImagePath = InAnnotatedImagePath ?? InImagePath,
            CropImagePath = InCropImagePath,
            VehicleTypeId = InSelectedVehicleType?.VehicleTypeId ?? 1,
            CreatedByUserId = _authService.CurrentUser?.UserId
        };

        var result = await _gateControlService.ProcessCheckInAsync(request);
        if (result.Success)
        {
            var checkInLocalTime = result.Session?.CheckInTimeLocal ?? DateTime.Now;
            LastInTimeFormatted = checkInLocalTime.ToString("dd/MM/yyyy HH:mm:ss");
            LastInTicketCode = result.Session?.TicketCode ?? string.Empty;
            LastInSlotCode = result.AssignedSlotCode ?? "Tự do";

            InStatusMessage = $"🟢 [VÀO THÀNH CÔNG] Biển số: {InPlateText} | Thời gian vào: {LastInTimeFormatted} | Mã vé: {LastInTicketCode} | Ô đỗ: {LastInSlotCode}";

            // Mở barrier làn vào 3 giây
            IsBarrierInOpen = true;
            _barrierInTimer.Stop();
            _barrierInTimer.Start();

            // Phát âm thanh báo hiệu xe vào
            PlayNotificationSound();

            await LoadActiveSessionsAsync();

            if (!silent)
            {
                _dialogService.ShowSuccess(result.Message);
            }
            return true;
        }
        else
        {
            InStatusMessage = $"🔴 [TỪ CHỐI VÀO] {result.Message}";
            if (!silent) _dialogService.ShowError(result.Message);
            return false;
        }
    }

    #endregion

    #region Làn Ra Methods & Auto Pipeline

    private void ExecuteSelectOutImage()
    {
        var filePath = _dialogService.ShowOpenFileDialog(
            filter: "Tệp hình ảnh (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp|Tất cả tệp (*.*)|*.*",
            title: "Chọn ảnh phương tiện xuất bãi");

        if (!string.IsNullOrEmpty(filePath))
        {
            OutImagePath = filePath;
            OutAnnotatedImagePath = null;
            OutCropImagePath = null;
            _ = ProcessOutLanePipelineAsync(isAutoTrigger: IsAutoModeEnabled);
        }
    }

    private async Task ExecuteQuickTestOutAsync()
    {
        var sampleImage = FindSampleImage();
        if (string.IsNullOrEmpty(sampleImage) || !File.Exists(sampleImage))
        {
            _dialogService.ShowWarning("Chưa tìm thấy ảnh mẫu kiểm thử trong thư mục dịch vụ.");
            return;
        }

        OutImagePath = sampleImage;
        OutAnnotatedImagePath = null;
        OutCropImagePath = null;
        await ProcessOutLanePipelineAsync(isAutoTrigger: IsAutoModeEnabled);
    }

    private async Task ExecuteRunOutOcrAsync()
    {
        await ProcessOutLanePipelineAsync(isAutoTrigger: false);
    }

    /// <summary>
    /// Pipeline Làn Ra: Tự động chạy OCR -> Khớp phiên vào -> Tính cước -> Tự động xuất bãi & Mở barrier nếu bật Auto Mode
    /// </summary>
    public async Task<bool> ProcessOutLanePipelineAsync(bool isAutoTrigger)
    {
        if (string.IsNullOrEmpty(OutImagePath) || !File.Exists(OutImagePath))
        {
            if (!isAutoTrigger) _dialogService.ShowWarning("Vui lòng chọn ảnh xe ra trước khi nhận diện OCR.");
            return false;
        }

        try
        {
            IsOutOcrBusy = true;
            OutStatusMessage = "⚡ Đang nhận diện biển số xe xuất bãi...";

            var response = await _ocrService.RecognizePlateAsync(OutImagePath);
            if (response.IsSuccess && response.Plates.Any())
            {
                var plate = response.Plates.First();
                OutPlateText = plate.PlateText;
                OutConfidence = plate.OcrConfidence > 0 ? plate.OcrConfidence : plate.DetectionConfidence;
                OutProcessingTimeMs = response.TotalMilliseconds;

                OutAnnotatedImagePath = !string.IsNullOrEmpty(response.OutputImage) && File.Exists(response.OutputImage)
                    ? response.OutputImage
                    : OutImagePath;

                OutCropImagePath = !string.IsNullOrEmpty(plate.CropPath) && File.Exists(plate.CropPath)
                    ? plate.CropPath
                    : null;

                OutStatusMessage = $"Đã nhận diện ra: {OutPlateText} ({OutConfidenceFormatted} - {OutProcessingTimeMs:0.0}ms)";

                // Đối soát phiên gửi
                await MatchSessionOutAsync(OutPlateText);

                // NẾU BẬT CHẾ ĐỘ TỰ ĐỘNG:
                // 1. Nếu là Vé Tháng (Cước = 0đ): TỰ ĐỘNG CHO RA KHÔNG DỪNG!
                // 2. Hoặc nếu IsAutoCheckoutAllVehicles == true: TỰ ĐỘNG CHO RA TOÀN BỘ!
                if ((isAutoTrigger || IsAutoModeEnabled) && MatchedSession != null)
                {
                    var requiresPayment = SelectedPaymentMethod == PaymentMethod.VietQR &&
                                          CalculatedFee > 0 &&
                                          !IsMonthlyTicketOut;

                    if ((IsMonthlyTicketOut || IsAutoCheckoutAllVehicles) && !requiresPayment)
                    {
                        OutStatusMessage = $"⚡ [TỰ ĐỘNG] Khớp biển số {OutPlateText} -> Tự động hoàn tất xuất bãi & Mở Barrier...";
                        await Task.Delay(200); // Đệm mượt
                        var checkOutSuccess = await PerformCheckOutAsync(silent: true);
                        return checkOutSuccess;
                    }

                    if (requiresPayment)
                    {
                        OutStatusMessage = $"🟡 [CHỜ THANH TOÁN] Khớp biển số {OutPlateText} | Cước {CalculatedFee:N0} đ. Vui lòng xác nhận VietQR.";
                    }
                }
                return true;
            }
            else
            {
                OutStatusMessage = $"Không nhận diện được biển số ra: {response.Error?.Message ?? "Không phát hiện biển số"}";
                if (!isAutoTrigger) _dialogService.ShowWarning(OutStatusMessage);
                return false;
            }
        }
        catch (Exception ex)
        {
            OutStatusMessage = $"Lỗi OCR Làn ra: {ex.Message}";
            if (!isAutoTrigger) _dialogService.ShowError(OutStatusMessage);
            return false;
        }
        finally
        {
            IsOutOcrBusy = false;
        }
    }

    private async Task MatchSessionOutAsync(string plate)
    {
        if (string.IsNullOrWhiteSpace(plate))
        {
            IsPlateMatched = MatchedSession == null ? null : false;
            if (MatchedSession == null)
            {
                DurationFormatted = "--:--";
                CalculatedFee = 0;
            }
            return;
        }

        // Nếu người dùng đã chọn một xe trong danh sách, KHÔNG thay MatchedSession
        // bằng kết quả OCR. Khi đó biển OCR phải được so sánh với xe đã chọn.
        if (MatchedSession != null && SelectedActiveSession != null &&
            MatchedSession.SessionId == SelectedActiveSession.SessionId)
        {
            EvaluatePlateMatch();
            OutStatusMessage = IsPlateMatched == true
                ? $"Khớp biển số: {MatchedSession.LicensePlate}"
                : $"CẢNH BÁO: Biển số vào '{MatchedSession.LicensePlate}' khác biển số ra '{plate}'!";
            return;
        }

        // Chưa chọn xe thủ công: dùng biển OCR để tìm phiên đang gửi.
        var calcResult = await _gateControlService.CalculateCheckOutAsync(plate);
        if (calcResult.Success && calcResult.ActiveSession != null)
        {
            MatchedSession = calcResult.ActiveSession;
            DurationFormatted = calcResult.DurationFormatted;
            CalculatedFee = calcResult.TotalFee;
            IsMonthlyTicketOut = calcResult.IsMonthlyTicket;
            FeeDetails = calcResult.FeeDetails;
            EvaluatePlateMatch();
            OutStatusMessage = calcResult.Message;
        }
        else
        {
            MatchedSession = null;
            IsPlateMatched = false;
            DurationFormatted = "--:--";
            CalculatedFee = 0;
            FeeDetails = string.Empty;
            OutStatusMessage = calcResult.Message;
        }
    }

    private void EvaluatePlateMatch()
    {
        if (MatchedSession == null || string.IsNullOrWhiteSpace(OutPlateText))
        {
            IsPlateMatched = null;
            return;
        }

        var normIn = Normalize(MatchedSession.LicensePlate);
        var normOut = Normalize(OutPlateText);

        IsPlateMatched = string.Equals(normIn, normOut, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return System.Text.RegularExpressions.Regex.Replace(text, @"[^a-zA-Z0-9]", "").ToUpperInvariant();
    }

    private async void LoadSessionToOutLane(ParkingSession session)
    {
        // Chỉ nạp xe được chọn làm xe cần đối soát.
        // Không ghi đè OutPlateText vì đây phải là biển số thực tế nhận từ OCR làn ra.
        MatchedSession = session;

        var calcResult = await _gateControlService.CalculateCheckOutAsync(session.LicensePlate);
        if (calcResult.Success && calcResult.ActiveSession != null)
        {
            MatchedSession = calcResult.ActiveSession;
            DurationFormatted = calcResult.DurationFormatted;
            CalculatedFee = calcResult.TotalFee;
            IsMonthlyTicketOut = calcResult.IsMonthlyTicket;
            FeeDetails = calcResult.FeeDetails;
        }

        EvaluatePlateMatch();

        if (string.IsNullOrWhiteSpace(OutPlateText))
        {
            OutStatusMessage = $"Đã chọn xe {session.LicensePlate}. Vui lòng nhận diện biển số xe đang ra để đối soát.";
        }
        else if (IsPlateMatched == true)
        {
            OutStatusMessage = $"Khớp biển số: {session.LicensePlate}";
        }
        else
        {
            OutStatusMessage = $"CẢNH BÁO: Biển số vào '{session.LicensePlate}' khác biển số ra '{OutPlateText}'!";
        }
    }

    private async Task ExecuteConfirmCheckOutAsync()
    {
        await PerformCheckOutAsync(silent: false);
    }

    private async Task<bool> PerformCheckOutAsync(bool silent)
    {
        if (MatchedSession == null)
        {
            if (!silent) _dialogService.ShowWarning("Chưa có phiên gửi xe hợp lệ để xuất bãi.");
            return false;
        }

        // Nếu đã có biển số OCR thì bắt buộc phải khớp với biển số lúc vào.
        if (!string.IsNullOrWhiteSpace(OutPlateText) && IsPlateMatched != true)
        {
            var message = $"Không thể xuất bãi: biển số vào '{MatchedSession.LicensePlate}' không khớp biển số ra '{OutPlateText}'.";
            OutStatusMessage = $"🔴 [TỪ CHỐI RA] {message}";
            if (!silent) _dialogService.ShowWarning(message);
            return false;
        }

        var targetPlate = MatchedSession.LicensePlate;
        var inTimeStr = MatchedSession.CheckInTimeLocal.ToString("dd/MM/yyyy HH:mm:ss");
        var outTimeStr = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
        var durationStr = DurationFormatted;
        var feeStr = CalculatedFeeFormatted;
        var paymentStr = SelectedPaymentMethod.ToString();

        if (SelectedPaymentMethod == PaymentMethod.VietQR && CalculatedFee > 0 && !IsMonthlyTicketOut)
        {
            return await InitiateVietQrPaymentAsync(silent);
        }

        var request = new GateCheckOutRequest
        {
            SessionId = MatchedSession.SessionId,
            ActorUserId = _authService.CurrentUser?.UserId ?? 0,
            CheckOutImagePath = OutAnnotatedImagePath ?? OutImagePath,
            PaymentMethod = SelectedPaymentMethod,
            TotalFee = CalculatedFee
        };

        var result = await _gateControlService.CompleteCheckOutAsync(request);
        if (result.Success)
        {
            OutStatusMessage = $"🟢 [RA THÀNH CÔNG] Biển số: {targetPlate} | Vào: {inTimeStr} | Ra: {outTimeStr} | Gửi: {durationStr} | Cước: {feeStr} ({paymentStr})";

            // Mở barrier làn ra 3 giây
            IsBarrierOutOpen = true;
            _barrierOutTimer.Stop();
            _barrierOutTimer.Start();

            // Âm báo xuất bãi
            PlayNotificationSound();

            await LoadActiveSessionsAsync();

            if (!silent)
            {
                _dialogService.ShowSuccess($"Xuất bãi thành công cho xe {targetPlate}!\nCước phí: {CalculatedFeeFormatted}");
            }

            // Reset Làn Ra
            MatchedSession = null;
            OutPlateText = string.Empty;
            OutImagePath = null;
            OutAnnotatedImagePath = null;
            OutCropImagePath = null;
            IsPlateMatched = null;
            CalculatedFee = 0;
            DurationFormatted = "--:--";

            return true;
        }
        else
        {
            OutStatusMessage = $"🔴 [TỪ CHỐI RA] {result.Message}";
            if (!silent) _dialogService.ShowError(result.Message);
            return false;
        }
    }

    private async Task<bool> InitiateVietQrPaymentAsync(bool silent)
    {
        if (MatchedSession == null) return false;
        if (_paymentService == null)
        {
            if (!silent) _dialogService.ShowError("Dịch vụ thanh toán VietQR chưa được cấu hình.");
            return false;
        }

        IsVietQrModalOpen = true;
        IsVietQrLoading = true;
        IsVietQrPaidSuccess = false;
        VietQrImageSource = null;
        VietQrStatusText = "Đang kết nối cổng thanh toán để tạo mã QR...";
        VietQrAmount = CalculatedFee;

        try
        {
            var req = new CreatePaymentRequest
            {
                SessionId = MatchedSession.SessionId,
                ActorUserId = _authService.CurrentUser?.UserId ?? 0,
                CheckoutImagePath = OutAnnotatedImagePath ?? OutImagePath
            };

            var result = await _paymentService.CreatePaymentAsync(req);
            if (!result.Success)
            {
                IsVietQrModalOpen = false;
                IsVietQrLoading = false;
                OutStatusMessage = $"🔴 [LỖI TẠO VIETQR] {result.Message}";
                if (!silent) _dialogService.ShowError(result.Message);
                return false;
            }

            VietQrPaymentId = result.PaymentId;
            VietQrTransactionReference = result.TransactionReference;
            VietQrAmount = result.Amount;
            VietQrDescription = result.Description;
            VietQrStatusText = "Đang chờ thanh toán... Khách vui lòng quét mã VietQR.";

            if (result.QrImagePng != null && result.QrImagePng.Length > 0)
            {
                VietQrImageSource = LoadBitmapFromBytes(result.QrImagePng);
            }

            IsVietQrLoading = false;
            _vietQrPollTimer.Stop();
            _vietQrPollTimer.Start();
            return true;
        }
        catch (Exception ex)
        {
            IsVietQrModalOpen = false;
            IsVietQrLoading = false;
            OutStatusMessage = $"🔴 [LỖI THANH TOÁN] {ex.Message}";
            if (!silent) _dialogService.ShowError(ex.Message);
            return false;
        }
    }

    private void OnSessionCompleted(object? sender, ParkingSession session)
    {
        if (session == null) return;

        Application.Current?.Dispatcher?.InvokeAsync(async () =>
        {
            if (MatchedSession != null && MatchedSession.SessionId == session.SessionId)
            {
                if (IsVietQrModalOpen)
                {
                    _vietQrPollTimer.Stop();
                    IsVietQrPaidSuccess = true;
                    VietQrStatusText = "Thanh toán VietQR thành công! Đang mở barrier xuất bãi...";

                    IsBarrierOutOpen = true;
                    _barrierOutTimer.Stop();
                    _barrierOutTimer.Start();
                    PlayNotificationSound();

                    OutStatusMessage = $"🟢 [RA THÀNH CÔNG] Biển số: {session.LicensePlate} | Đã thanh toán VietQR: {session.TotalFee:N0} đ";

                    await Task.Delay(1500);
                    IsVietQrModalOpen = false;

                    MatchedSession = null;
                    OutPlateText = string.Empty;
                    OutImagePath = null;
                    OutAnnotatedImagePath = null;
                    OutCropImagePath = null;
                    IsPlateMatched = null;
                    CalculatedFee = 0;
                    DurationFormatted = "--:--";
                }
            }

            await LoadActiveSessionsAsync();
        });
    }

    private async void OnVietQrPollTick(object? sender, EventArgs e)
    {
        if (!IsVietQrModalOpen || VietQrPaymentId <= 0 || _paymentService == null)
        {
            _vietQrPollTimer.Stop();
            return;
        }

        try
        {
            var status = await _paymentService.GetPaymentStatusAsync(VietQrPaymentId);
            if (status.Success && status.Status == PaymentStatus.Paid && !IsVietQrPaidSuccess)
            {
                _vietQrPollTimer.Stop();
                IsVietQrPaidSuccess = true;
                VietQrStatusText = "Xác nhận thanh toán thành công qua VietQR!";

                IsBarrierOutOpen = true;
                _barrierOutTimer.Stop();
                _barrierOutTimer.Start();
                PlayNotificationSound();

                if (MatchedSession != null)
                {
                    OutStatusMessage = $"🟢 [RA THÀNH CÔNG] Biển số: {MatchedSession.LicensePlate} | Đã thanh toán: {status.Amount:N0} đ";
                }

                await LoadActiveSessionsAsync();

                await Task.Delay(1500);
                IsVietQrModalOpen = false;

                MatchedSession = null;
                OutPlateText = string.Empty;
                OutImagePath = null;
                OutAnnotatedImagePath = null;
                OutCropImagePath = null;
                IsPlateMatched = null;
                CalculatedFee = 0;
                DurationFormatted = "--:--";
            }
        }
        catch
        {
            // Bỏ qua lỗi mạng trong quá trình polling
        }
    }

    private async Task ExecuteRefreshVietQrStatusAsync()
    {
        if (VietQrPaymentId <= 0 || _paymentService == null) return;
        VietQrStatusText = "Đang kiểm tra đối soát với cổng...";

        var status = await _paymentService.RefreshFromGatewayAsync(VietQrPaymentId);
        if (status.Success && status.Status == PaymentStatus.Paid && !IsVietQrPaidSuccess)
        {
            _vietQrPollTimer.Stop();
            IsVietQrPaidSuccess = true;
            VietQrStatusText = "Cổng xác nhận đã thanh toán thành công!";
            IsBarrierOutOpen = true;
            _barrierOutTimer.Stop();
            _barrierOutTimer.Start();
            PlayNotificationSound();

            await LoadActiveSessionsAsync();
            await Task.Delay(1500);
            IsVietQrModalOpen = false;

            MatchedSession = null;
            OutPlateText = string.Empty;
            OutImagePath = null;
            OutAnnotatedImagePath = null;
            OutCropImagePath = null;
            IsPlateMatched = null;
            CalculatedFee = 0;
            DurationFormatted = "--:--";
        }
        else
        {
            VietQrStatusText = "Chưa nhận được giao dịch từ khách. Vui lòng thử lại sau khi chuyển tiền.";
        }
    }

    private async Task ExecuteCancelVietQrPaymentAsync()
    {
        _vietQrPollTimer.Stop();
        if (VietQrPaymentId > 0 && _paymentService != null)
        {
            await _paymentService.CancelPaymentAsync(VietQrPaymentId);
        }

        IsVietQrModalOpen = false;
        IsVietQrLoading = false;
        OutStatusMessage = "Đã hủy giao dịch thanh toán VietQR.";
    }

    private static BitmapImage? LoadBitmapFromBytes(byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0) return null;
        try
        {
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    #endregion

    #region Mô Phỏng Tự Động Toàn Diện (Auto Simulation Loop)

    private async Task ExecuteAutoSimulationAsync()
    {
        if (IsSimulationRunning) return;

        try
        {
            IsSimulationRunning = true;
            _dialogService.ShowInfo("Bắt đầu kịch bản mô phỏng tự động:\n1. Xe vào bãi (Auto Check-In)\n2. Xe đỗ trong bãi\n3. Xe xuất bãi (Auto Check-Out).");

            // BẬT CHẾ ĐỘ TỰ ĐỘNG
            IsAutoModeEnabled = true;

            var sampleImage = FindSampleImage();
            if (string.IsNullOrEmpty(sampleImage) || !File.Exists(sampleImage))
            {
                _dialogService.ShowWarning("Không tìm thấy ảnh mẫu để chạy mô phỏng.");
                return;
            }

            // --- BƯỚC 1: XE TIẾN VÀO LÀN VÀO ---
            InStatusMessage = "🎬 [MÔ PHỎNG] Xe ô tô 30K-555.55 tiến vào Làn Vào...";
            InImagePath = sampleImage;
            InAnnotatedImagePath = null;
            InCropImagePath = null;

            await Task.Delay(1000);
            var inSuccess = await ProcessInLanePipelineAsync(isAutoTrigger: true);
            if (!inSuccess)
            {
                InStatusMessage = "Mô phỏng dừng do bước xe vào chưa thành công.";
                return;
            }

            // --- BƯỚC 2: XE ĐANG ĐỖ TRONG BÃI ---
            await Task.Delay(2500);

            // --- BƯỚC 3: XE TIẾN ĐẾN LÀN RA ---
            OutStatusMessage = "🎬 [MÔ PHỎNG] Xe ô tô 30K-555.55 tiến đến Làn Ra đối soát...";
            OutImagePath = sampleImage;
            OutAnnotatedImagePath = null;
            OutCropImagePath = null;

            await Task.Delay(1000);
            var outSuccess = await ProcessOutLanePipelineAsync(isAutoTrigger: true);

            if (outSuccess)
            {
                _dialogService.ShowSuccess("🎉 Chu trình Mô Phỏng Tự Động Ra/Vào đã hoàn tất thành công 100%!");
            }
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"Lỗi mô phỏng: {ex.Message}");
        }
        finally
        {
            IsSimulationRunning = false;
        }
    }

    private void PlayNotificationSound()
    {
        _audioAlertService.PlaySuccessAlert();
    }

    #endregion

    private static string? FindSampleImage()
    {
        var baseDir = AppContext.BaseDirectory;
        var dirInfo = new DirectoryInfo(baseDir);
        for (int i = 0; i < 5 && dirInfo != null; i++)
        {
            var p1 = Path.Combine(dirInfo.FullName, "Services", "OcrLisencePlate", "output", "requests", "interactive_check", "annotated.jpg");
            if (File.Exists(p1)) return p1;

            var p2 = Path.Combine(dirInfo.FullName, "Services", "OcrLisencePlate", "output", "requests", "test_image_1", "annotated.jpg");
            if (File.Exists(p2)) return p2;

            dirInfo = dirInfo.Parent;
        }

        var cur1 = Path.Combine(Directory.GetCurrentDirectory(), "Services", "OcrLisencePlate", "output", "requests", "interactive_check", "annotated.jpg");
        if (File.Exists(cur1)) return cur1;

        var cur2 = Path.Combine(Directory.GetCurrentDirectory(), "Services", "OcrLisencePlate", "output", "requests", "test_image_1", "annotated.jpg");
        if (File.Exists(cur2)) return cur2;

        return null;
    }
}
