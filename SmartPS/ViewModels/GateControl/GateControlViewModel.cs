using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using SmartPS.Models.GateControl;
using SmartPS.Models.Ocr;
using SmartPS.Models.Parking;
using SmartPS.Services.Audio;
using SmartPS.Services.Auth;
using SmartPS.Services.Dialog;
using SmartPS.Services.GateControl;
using SmartPS.Services.Localization;
using SmartPS.Services.OcrLisencePlate;

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

    private readonly DispatcherTimer _barrierInTimer;
    private readonly DispatcherTimer _barrierOutTimer;

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

    #endregion

    public GateControlViewModel(
        IOcrLicensePlateService ocrService,
        IGateControlService gateControlService,
        ICameraWatcherService cameraWatcherService,
        IDialogService dialogService,
        ILocalizationService localizationService,
        IAuthService authService,
        IAudioAlertService? audioAlertService = null)
    {
        _ocrService = ocrService ?? throw new ArgumentNullException(nameof(ocrService));
        _gateControlService = gateControlService ?? throw new ArgumentNullException(nameof(gateControlService));
        _cameraWatcherService = cameraWatcherService ?? throw new ArgumentNullException(nameof(cameraWatcherService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _audioAlertService = audioAlertService ?? new SystemAudioAlertService();

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

        var ticket = await _gateControlService.FindActiveMonthlyTicketAsync(plate);
        if (ticket != null)
        {
            IsMonthlyTicketIn = true;
            InCustomerBadge = $"👑 VÉ THÁNG: {ticket.Customer?.FullName ?? "VIP"} (Hiệu lực: {ticket.EndDate:dd/MM/yyyy})";
            if (ticket.VehicleTypeId > 0 && VehicleTypes.Any(v => v.VehicleTypeId == ticket.VehicleTypeId))
            {
                InSelectedVehicleType = VehicleTypes.First(v => v.VehicleTypeId == ticket.VehicleTypeId);
            }
        }
        else
        {
            IsMonthlyTicketIn = false;
            InCustomerBadge = "Khách vãng lai";
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
                    if (IsMonthlyTicketOut || IsAutoCheckoutAllVehicles)
                    {
                        OutStatusMessage = $"⚡ [TỰ ĐỘNG] Khớp biển số {OutPlateText} -> Tự động hoàn tất xuất bãi & Mở Barrier...";
                        await Task.Delay(200); // Đệm mượt
                        var checkOutSuccess = await PerformCheckOutAsync(silent: true);
                        return checkOutSuccess;
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
            MatchedSession = null;
            IsPlateMatched = null;
            DurationFormatted = "--:--";
            CalculatedFee = 0;
            return;
        }

        var calcResult = await _gateControlService.CalculateCheckOutAsync(plate);
        if (calcResult.Success && calcResult.ActiveSession != null)
        {
            MatchedSession = calcResult.ActiveSession;
            DurationFormatted = calcResult.DurationFormatted;
            CalculatedFee = calcResult.TotalFee;
            IsMonthlyTicketOut = calcResult.IsMonthlyTicket;
            OutStatusMessage = calcResult.Message;
        }
        else
        {
            MatchedSession = null;
            IsPlateMatched = false;
            DurationFormatted = "--:--";
            CalculatedFee = 0;
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

    private void LoadSessionToOutLane(ParkingSession session)
    {
        MatchedSession = session;
        OutPlateText = session.LicensePlate;
        _ = MatchSessionOutAsync(session.LicensePlate);
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

        var targetPlate = MatchedSession.LicensePlate;
        var inTimeStr = MatchedSession.CheckInTimeLocal.ToString("dd/MM/yyyy HH:mm:ss");
        var outTimeStr = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
        var durationStr = DurationFormatted;
        var feeStr = CalculatedFeeFormatted;
        var paymentStr = SelectedPaymentMethod.ToString();

        var request = new GateCheckOutRequest
        {
            SessionId = MatchedSession.SessionId,
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
            var p1 = Path.Combine(dirInfo.FullName, "SmartPS", "Services", "OcrLisencePlate", "output", "requests", "interactive_check", "annotated.jpg");
            if (File.Exists(p1)) return p1;

            var p2 = Path.Combine(dirInfo.FullName, "SmartPS", "Services", "OcrLisencePlate", "output", "requests", "test_image_1", "annotated.jpg");
            if (File.Exists(p2)) return p2;

            dirInfo = dirInfo.Parent;
        }

        var cur1 = Path.Combine(Directory.GetCurrentDirectory(), "Services", "OcrLisencePlate", "output", "requests", "interactive_check", "annotated.jpg");
        if (File.Exists(cur1)) return cur1;

        var cur2 = Path.Combine(Directory.GetCurrentDirectory(), "SmartPS", "Services", "OcrLisencePlate", "output", "requests", "interactive_check", "annotated.jpg");
        if (File.Exists(cur2)) return cur2;

        return null;
    }
}
