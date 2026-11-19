using System.Collections.ObjectModel;
using System.Globalization;
using SmartPS.Constants;
using SmartPS.Models.Parking;
using SmartPS.Services.Authorization;
using SmartPS.Services.Common;
using SmartPS.Services.Dialog;
using SmartPS.Services.GateControl;
using SmartPS.Services.Localization;
using SmartPS.Services.ParkingZones;

namespace SmartPS.ViewModels.ParkingMap;

public class ParkingMapViewModel : ViewModelBase
{
    private readonly IGateControlService _gateControlService;
    private readonly IParkingZoneService _zoneService;
    private readonly IPermissionService _permissions;
    private readonly IDialogService _dialog;
    private readonly ILocalizationService _localization;

    private IReadOnlyList<ZoneGroupData> _groups = Array.Empty<ZoneGroupData>();
    private IReadOnlyList<ParkingZoneInfo> _zoneInfos = Array.Empty<ParkingZoneInfo>();
    private IReadOnlyList<ParkingSlot> _allSlots = Array.Empty<ParkingSlot>();

    private int _totalSlots;
    public int TotalSlots
    {
        get => _totalSlots;
        set => SetProperty(ref _totalSlots, value);
    }

    private int _occupiedSlots;
    public int OccupiedSlots
    {
        get => _occupiedSlots;
        set => SetProperty(ref _occupiedSlots, value);
    }

    private int _availableSlots;
    public int AvailableSlots
    {
        get => _availableSlots;
        set => SetProperty(ref _availableSlots, value);
    }

    private int _maintenanceSlots;
    public int MaintenanceSlots
    {
        get => _maintenanceSlots;
        set
        {
            if (SetProperty(ref _maintenanceSlots, value))
            {
                OnPropertyChanged(nameof(MaintenanceText));
            }
        }
    }

    public string MaintenanceText => _localization.GetString("Str_Map_MaintenanceCount", MaintenanceSlots);

    private double _occupancyRate;
    public double OccupancyRate
    {
        get => _occupancyRate;
        set
        {
            if (SetProperty(ref _occupancyRate, value))
            {
                OnPropertyChanged(nameof(OccupancyRateFormatted));
            }
        }
    }
    public string OccupancyRateFormatted => $"{OccupancyRate:0.0}%";

    private string _zonesSummary = string.Empty;
    public string ZonesSummary
    {
        get => _zonesSummary;
        set => SetProperty(ref _zonesSummary, value);
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    private string _searchKeyword = string.Empty;
    public string SearchKeyword
    {
        get => _searchKeyword;
        set
        {
            if (SetProperty(ref _searchKeyword, value))
            {
                RebuildZones();
            }
        }
    }

    private ParkingSlotItemViewModel? _selectedSlot;
    public ParkingSlotItemViewModel? SelectedSlot
    {
        get => _selectedSlot;
        set
        {
            if (SetProperty(ref _selectedSlot, value))
            {
                OnPropertyChanged(nameof(MaintenanceActionText));
                OnPropertyChanged(nameof(HasSelectedSlot));
                ToggleSlotMaintenanceCommand.RaiseCanExecuteChanged();
                DeleteSlotCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasSelectedSlot => SelectedSlot is not null;

    /// <summary>Chỉ người có Parking.Configure mới đổi được đối tượng khu, tạo khu, thêm / bảo trì / xoá ô.</summary>
    public bool CanConfigure => _permissions.HasPermission(Permissions.ParkingConfigure);

    public ObservableCollection<ParkingZoneGroupViewModel> Zones { get; } = new();

    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand<ParkingSlotItemViewModel> SelectSlotCommand { get; }

    // ------------------------------------------------------------------ Quản lý khu và ô đỗ

    private bool _isManagePanelOpen;
    public bool IsManagePanelOpen
    {
        get => _isManagePanelOpen;
        set
        {
            if (SetProperty(ref _isManagePanelOpen, value))
            {
                OnPropertyChanged(nameof(ManageToggleText));
            }
        }
    }

    public string ManageToggleText => _localization.GetString(IsManagePanelOpen ? "Str_Map_Manage_Hide" : "Str_Map_Manage_Toggle");

    public ObservableCollection<OptionItem<int?>> ZoneVehicleTypeOptions { get; } = new();
    public ObservableCollection<OptionItem<int>> VehicleTypeOptions { get; } = new();
    public ObservableCollection<OptionItem<ZoneAudience>> AudienceOptions { get; } = new();
    public ObservableCollection<OptionItem<int>> SlotZoneOptions { get; } = new();

    private string _newZoneCode = string.Empty;
    public string NewZoneCode
    {
        get => _newZoneCode;
        set
        {
            if (SetProperty(ref _newZoneCode, value))
            {
                CreateZoneCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private string _newZoneName = string.Empty;
    public string NewZoneName
    {
        get => _newZoneName;
        set
        {
            if (SetProperty(ref _newZoneName, value))
            {
                CreateZoneCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private int? _newZoneVehicleTypeId;
    public int? NewZoneVehicleTypeId
    {
        get => _newZoneVehicleTypeId;
        set => SetProperty(ref _newZoneVehicleTypeId, value);
    }

    private ZoneAudience _newZoneAudience = ZoneAudience.Mixed;
    public ZoneAudience NewZoneAudience
    {
        get => _newZoneAudience;
        set => SetProperty(ref _newZoneAudience, value);
    }

    private int? _slotZoneId;
    public int? SlotZoneId
    {
        get => _slotZoneId;
        set
        {
            if (SetProperty(ref _slotZoneId, value))
            {
                ApplyZoneDefaults();
            }
        }
    }

    private int? _slotVehicleTypeId;
    public int? SlotVehicleTypeId
    {
        get => _slotVehicleTypeId;
        set
        {
            if (SetProperty(ref _slotVehicleTypeId, value))
            {
                UpdateSlotPreview();
            }
        }
    }

    private bool _isSlotVehicleTypeLocked;
    /// <summary>Khu chỉ dành cho một loại xe: không cho đổi loại xe của ô mới.</summary>
    public bool IsSlotVehicleTypeLocked
    {
        get => _isSlotVehicleTypeLocked;
        private set
        {
            if (SetProperty(ref _isSlotVehicleTypeLocked, value))
            {
                OnPropertyChanged(nameof(CanChooseSlotVehicleType));
            }
        }
    }

    public bool CanChooseSlotVehicleType => !IsSlotVehicleTypeLocked;

    private string _slotPrefix = string.Empty;
    public string SlotPrefix
    {
        get => _slotPrefix;
        set
        {
            if (SetProperty(ref _slotPrefix, value))
            {
                UpdateSlotPreview();
            }
        }
    }

    private string _slotCountText = "10";
    public string SlotCountText
    {
        get => _slotCountText;
        set
        {
            if (SetProperty(ref _slotCountText, value))
            {
                UpdateSlotPreview();
            }
        }
    }

    private string _slotPreviewText = string.Empty;
    public string SlotPreviewText
    {
        get => _slotPreviewText;
        private set => SetProperty(ref _slotPreviewText, value);
    }

    private bool _isSlotInputValid;
    public bool IsSlotInputValid
    {
        get => _isSlotInputValid;
        private set
        {
            if (SetProperty(ref _isSlotInputValid, value))
            {
                AddSlotsCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string MaintenanceActionText => _localization.GetString(
        SelectedSlot?.IsMaintenance == true ? "Str_Map_SetAvailable" : "Str_Map_SetMaintenance");

    public RelayCommand ToggleManagePanelCommand { get; }
    public AsyncRelayCommand CreateZoneCommand { get; }
    public AsyncRelayCommand AddSlotsCommand { get; }
    public RelayCommand<ParkingZoneGroupViewModel> PrepareAddSlotsCommand { get; }
    public AsyncRelayCommand ToggleSlotMaintenanceCommand { get; }
    public AsyncRelayCommand DeleteSlotCommand { get; }

    public ParkingMapViewModel(
        IGateControlService gateControlService,
        IParkingZoneService zoneService,
        IPermissionService permissions,
        IDialogService dialogService,
        ILocalizationService localization)
    {
        _gateControlService = gateControlService ?? throw new ArgumentNullException(nameof(gateControlService));
        _zoneService = zoneService ?? throw new ArgumentNullException(nameof(zoneService));
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _dialog = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));

        RefreshCommand = new AsyncRelayCommand(LoadDataAsync);
        SelectSlotCommand = new RelayCommand<ParkingSlotItemViewModel>(slot =>
        {
            SelectedSlot = slot;
        });

        ToggleManagePanelCommand = new RelayCommand(() => IsManagePanelOpen = !IsManagePanelOpen, () => CanConfigure);
        CreateZoneCommand = new AsyncRelayCommand(CreateZoneAsync,
            () => CanConfigure && !string.IsNullOrWhiteSpace(NewZoneCode) && !string.IsNullOrWhiteSpace(NewZoneName));
        AddSlotsCommand = new AsyncRelayCommand(AddSlotsAsync, () => CanConfigure && IsSlotInputValid);
        PrepareAddSlotsCommand = new RelayCommand<ParkingZoneGroupViewModel>(zone =>
        {
            IsManagePanelOpen = true;
            if (zone?.ZoneId is int id)
            {
                SlotZoneId = id;
            }
        }, zone => CanConfigure && zone?.ZoneId is not null);
        ToggleSlotMaintenanceCommand = new AsyncRelayCommand(ToggleSlotMaintenanceAsync,
            () => CanConfigure && SelectedSlot is { IsOccupied: false });
        DeleteSlotCommand = new AsyncRelayCommand(DeleteSlotAsync,
            () => CanConfigure && SelectedSlot is { IsOccupied: false });

        BuildStaticOptions();

        _localization.LanguageChanged += () =>
        {
            BuildStaticOptions();
            OnPropertyChanged(nameof(ManageToggleText));
            OnPropertyChanged(nameof(MaintenanceActionText));
            _ = LoadDataAsync();
        };

        _ = LoadDataAsync();
    }

    public async Task LoadDataAsync()
    {
        if (IsLoading) return;
        try
        {
            IsLoading = true;

            var slots = await _gateControlService.GetAllSlotsAsync();
            var activeSessions = await _gateControlService.GetActiveSessionsAsync();
            var zones = await _zoneService.GetZonesAsync();
            var vehicleTypes = await _gateControlService.GetVehicleTypesAsync();

            _allSlots = slots;
            _zoneInfos = zones;
            _groups = ParkingMapBuilder.BuildGroups(slots, activeSessions, zones);

            // Chỉ số thống kê thực tế: ô bảo trì không được tính là còn trống
            TotalSlots = _groups.Sum(g => g.TotalCount);
            OccupiedSlots = _groups.Sum(g => g.OccupiedCount);
            AvailableSlots = _groups.Sum(g => g.AvailableCount);
            MaintenanceSlots = _groups.Sum(g => g.MaintenanceCount);
            OccupancyRate = TotalSlots > 0 ? Math.Round((double)OccupiedSlots / TotalSlots * 100, 1) : 0;
            ZonesSummary = string.Join(" · ", _groups.Select(g => string.IsNullOrWhiteSpace(g.ZoneName) ? g.ZoneCode : g.ZoneName));

            BuildDynamicOptions(vehicleTypes, zones);

            var selectedId = SelectedSlot?.SlotId;
            RebuildZones();

            // Nếu ô đang chọn vẫn tồn tại thì cập nhật lại tham chiếu
            var allSlots = Zones.SelectMany(z => z.Slots).ToList();
            SelectedSlot = (selectedId.HasValue ? allSlots.FirstOrDefault(s => s.SlotId == selectedId.Value) : null)
                           ?? allSlots.FirstOrDefault(s => s.IsOccupied)
                           ?? allSlots.FirstOrDefault();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ParkingMapViewModel Error]: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void RebuildZones()
    {
        var kw = SearchKeyword?.Trim().ToUpperInvariant() ?? string.Empty;
        var canConfigure = CanConfigure;

        Zones.Clear();
        foreach (var group in _groups)
        {
            var slotViewModels = group.Slots
                .Where(s => string.IsNullOrEmpty(kw)
                            || s.SlotCode.ToUpperInvariant().Contains(kw)
                            || (!string.IsNullOrEmpty(s.CurrentLicensePlate) && s.CurrentLicensePlate.ToUpperInvariant().Contains(kw)))
                .Select(s => ToSlotViewModel(group, s))
                .ToList();

            // Khi tìm kiếm, chỉ giữ các khu còn ô khớp
            if (!string.IsNullOrEmpty(kw) && slotViewModels.Count == 0)
            {
                continue;
            }

            Zones.Add(new ParkingZoneGroupViewModel(group, slotViewModels, canConfigure, _localization, SaveZoneAudienceAsync));
        }
    }

    private ParkingSlotItemViewModel ToSlotViewModel(ZoneGroupData group, SlotMapData slot)
        => new()
        {
            SlotId = slot.SlotId,
            SlotCode = slot.SlotCode,
            ZoneName = group.ZoneName,
            VehicleTypeId = slot.VehicleTypeId,
            VehicleTypeName = string.IsNullOrEmpty(slot.VehicleTypeName) ? (slot.VehicleTypeId == 1 ? "Xe máy" : "Ô tô con") : slot.VehicleTypeName,
            Status = slot.EffectiveStatus,
            StatusText = _localization.GetString($"Str_Map_Status_{slot.EffectiveStatus}"),
            CurrentLicensePlate = slot.CurrentLicensePlate,
            CheckInTime = slot.CheckInTimeLocal,
            TicketCode = slot.TicketCode,
            CustomerName = slot.CustomerName,
            IsResidentSession = slot.SessionCustomerType == CustomerType.Resident
        };

    private async Task SaveZoneAudienceAsync(ParkingZoneGroupViewModel zone)
    {
        if (!zone.ZoneId.HasValue)
        {
            return;
        }

        var result = await _zoneService.UpdateAudienceAsync(zone.ZoneId.Value, zone.SelectedAudience);
        if (result.Success)
        {
            _dialog.ShowSuccess(_localization.GetString("Msg_Map_AudienceSaved"));
            await LoadDataAsync();
            return;
        }

        if (result.IsPermissionDenied)
        {
            _dialog.ShowWarning(_localization.GetString("Msg_Auth_PermissionDenied"));
            return;
        }

        _dialog.ShowError(_localization.GetString("Msg_Map_AudienceSaveError", _localization.GetString($"Msg_Op_{result.Error}")));
    }

    // ------------------------------------------------------------------ Quản lý khu và ô đỗ

    private void BuildStaticOptions()
    {
        AudienceOptions.Clear();
        foreach (var audience in Enum.GetValues<ZoneAudience>())
        {
            AudienceOptions.Add(new OptionItem<ZoneAudience>(audience, _localization.GetString($"Str_Map_Audience_{audience}")));
        }

        // Gán lại để ComboBox chọn đúng mục sau khi danh sách được dựng lại
        var audienceValue = NewZoneAudience;
        _newZoneAudience = (ZoneAudience)(-1);
        NewZoneAudience = audienceValue;
    }

    private void BuildDynamicOptions(IReadOnlyList<VehicleType> vehicleTypes, IReadOnlyList<ParkingZoneInfo> zones)
    {
        var keepZoneVehicleType = NewZoneVehicleTypeId;
        var keepSlotZone = SlotZoneId;

        VehicleTypeOptions.Clear();
        ZoneVehicleTypeOptions.Clear();
        ZoneVehicleTypeOptions.Add(new OptionItem<int?>(null, _localization.GetString("Str_Map_AnyVehicleType")));
        foreach (var vt in vehicleTypes.OrderBy(v => v.VehicleTypeId))
        {
            VehicleTypeOptions.Add(new OptionItem<int>(vt.VehicleTypeId, vt.TypeName));
            ZoneVehicleTypeOptions.Add(new OptionItem<int?>(vt.VehicleTypeId, vt.TypeName));
        }

        SlotZoneOptions.Clear();
        foreach (var zone in zones.OrderBy(z => z.ZoneCode, StringComparer.Ordinal))
        {
            SlotZoneOptions.Add(new OptionItem<int>(zone.ZoneId, $"{zone.ZoneCode} - {zone.ZoneName}"));
        }

        _newZoneVehicleTypeId = -1;
        NewZoneVehicleTypeId = ZoneVehicleTypeOptions.Any(o => o.Value == keepZoneVehicleType) ? keepZoneVehicleType : null;

        _slotZoneId = -1;
        SlotZoneId = keepSlotZone.HasValue && SlotZoneOptions.Any(o => o.Value == keepSlotZone.Value)
            ? keepSlotZone
            : SlotZoneOptions.FirstOrDefault()?.Value;
    }

    /// <summary>Khi chọn khu: khoá loại xe theo khu (nếu có) và gợi ý tiền tố từ các mã ô đã có trong khu.</summary>
    private void ApplyZoneDefaults()
    {
        var zone = _zoneInfos.FirstOrDefault(z => z.ZoneId == SlotZoneId);
        if (zone is null)
        {
            IsSlotVehicleTypeLocked = false;
            UpdateSlotPreview();
            return;
        }

        IsSlotVehicleTypeLocked = zone.VehicleTypeId.HasValue;
        var vehicleTypeId = zone.VehicleTypeId
                            ?? (SlotVehicleTypeId.HasValue && VehicleTypeOptions.Any(o => o.Value == SlotVehicleTypeId.Value) ? SlotVehicleTypeId : null)
                            ?? VehicleTypeOptions.FirstOrDefault()?.Value;
        _slotVehicleTypeId = vehicleTypeId;
        OnPropertyChanged(nameof(SlotVehicleTypeId));

        var zoneCodes = _allSlots
            .Where(s => s.ZoneId == zone.ZoneId)
            .OrderBy(s => s.VehicleTypeId == vehicleTypeId ? 0 : 1)
            .ThenBy(s => s.SlotCode, StringComparer.Ordinal)
            .Select(s => s.SlotCode);
        _slotPrefix = SlotCodeGenerator.SuggestPrefix(zone.ZoneCode, zoneCodes);
        OnPropertyChanged(nameof(SlotPrefix));

        UpdateSlotPreview();
    }

    private bool TryGetSlotCount(out int count)
        => int.TryParse(SlotCountText?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out count)
           && count >= 1 && count <= SlotCodeGenerator.MaxSlotsPerBatch;

    /// <summary>Xem trước các mã ô sẽ tạo (dịch vụ vẫn tính lại khi lưu).</summary>
    private void UpdateSlotPreview()
    {
        var prefix = SlotCodeGenerator.NormalizePrefix(SlotPrefix);
        if (!SlotZoneId.HasValue || !SlotVehicleTypeId.HasValue)
        {
            SlotPreviewText = string.Empty;
            IsSlotInputValid = false;
            return;
        }

        if (!TryGetSlotCount(out var count))
        {
            SlotPreviewText = _localization.GetString("Msg_Map_CountInvalid", SlotCodeGenerator.MaxSlotsPerBatch);
            IsSlotInputValid = false;
            return;
        }

        if (!SlotCodeGenerator.IsValidPrefix(prefix))
        {
            SlotPreviewText = _localization.GetString("Msg_Slot_PrefixInvalid");
            IsSlotInputValid = false;
            return;
        }

        var codes = SlotCodeGenerator.NextCodes(prefix, _allSlots.Select(s => s.SlotCode), count);
        SlotPreviewText = codes.Count == 1
            ? _localization.GetString("Str_Map_PreviewOne", codes[0])
            : _localization.GetString("Str_Map_Preview", codes.Count, codes[0], codes[^1]);
        IsSlotInputValid = true;
    }

    private async Task CreateZoneAsync()
    {
        var code = NewZoneCode.Trim().ToUpperInvariant();
        var result = await _zoneService.CreateZoneAsync(new CreateZoneRequest(code, NewZoneName, NewZoneVehicleTypeId, NewZoneAudience));
        if (!ShowFailure(result))
        {
            return;
        }

        _dialog.ShowSuccess(_localization.GetString("Msg_Map_ZoneCreated", code));
        NewZoneCode = string.Empty;
        NewZoneName = string.Empty;
        await LoadDataAsync();

        // Chọn luôn khu vừa tạo ở phần thêm ô
        var created = _zoneInfos.FirstOrDefault(z => string.Equals(z.ZoneCode, code, StringComparison.Ordinal));
        if (created is not null)
        {
            SlotZoneId = created.ZoneId;
        }
    }

    private async Task AddSlotsAsync()
    {
        if (!SlotZoneId.HasValue || !SlotVehicleTypeId.HasValue || !TryGetSlotCount(out var count))
        {
            return;
        }

        var result = await _zoneService.AddSlotsAsync(new AddSlotsRequest(SlotZoneId.Value, SlotVehicleTypeId.Value, SlotPrefix, count));
        if (!ShowFailure(result.Result))
        {
            return;
        }

        _dialog.ShowSuccess(_localization.GetString("Msg_Map_SlotsAdded", result.SlotCodes.Count, result.SlotCodes[0], result.SlotCodes[^1]));
        await LoadDataAsync();
    }

    private async Task ToggleSlotMaintenanceAsync()
    {
        var slot = SelectedSlot;
        if (slot is null || slot.IsOccupied)
        {
            return;
        }

        var toMaintenance = !slot.IsMaintenance;
        var result = await _zoneService.SetSlotMaintenanceAsync(slot.SlotId, toMaintenance, null);
        if (!ShowFailure(result))
        {
            return;
        }

        _dialog.ShowSuccess(_localization.GetString(toMaintenance ? "Msg_Map_SlotMaintenanceOn" : "Msg_Map_SlotMaintenanceOff", slot.SlotCode));
        await LoadDataAsync();
    }

    private async Task DeleteSlotAsync()
    {
        var slot = SelectedSlot;
        if (slot is null || slot.IsOccupied)
        {
            return;
        }

        if (!_dialog.ShowConfirm(_localization.GetString("Msg_Map_ConfirmDeleteSlot", slot.SlotCode)))
        {
            return;
        }

        var result = await _zoneService.DeleteSlotAsync(slot.SlotId);
        if (!ShowFailure(result))
        {
            return;
        }

        _dialog.ShowSuccess(_localization.GetString("Msg_Map_SlotDeleted", slot.SlotCode));
        SelectedSlot = null;
        await LoadDataAsync();
    }

    /// <summary>Hiện thông báo khi thất bại; trả về true nếu thành công.</summary>
    private bool ShowFailure(OperationResult result)
    {
        if (result.Success)
        {
            return true;
        }

        if (result.IsPermissionDenied)
        {
            _dialog.ShowWarning(_localization.GetString("Msg_Auth_PermissionDenied"));
        }
        else if (result.Error == OperationError.Validation || result.Error == OperationError.NotFound)
        {
            _dialog.ShowWarning(string.IsNullOrWhiteSpace(result.Message)
                ? _localization.GetString($"Msg_Op_{result.Error}")
                : result.Message);
        }
        else
        {
            _dialog.ShowError(_localization.GetString("Msg_Map_SaveError", _localization.GetString($"Msg_Op_{result.Error}")));
        }

        return false;
    }
}
