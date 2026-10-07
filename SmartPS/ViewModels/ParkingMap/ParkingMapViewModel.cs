using System.Collections.ObjectModel;
using SmartPS.Constants;
using SmartPS.Models.Parking;
using SmartPS.Services.Authorization;
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
        set => SetProperty(ref _selectedSlot, value);
    }

    /// <summary>Chỉ người có Parking.Configure mới đổi được đối tượng của khu.</summary>
    public bool CanConfigure => _permissions.HasPermission(Permissions.ParkingConfigure);

    public ObservableCollection<ParkingZoneGroupViewModel> Zones { get; } = new();

    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand<ParkingSlotItemViewModel> SelectSlotCommand { get; }

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

        _localization.LanguageChanged += () => _ = LoadDataAsync();

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

            _groups = ParkingMapBuilder.BuildGroups(slots, activeSessions, zones);

            // Chỉ số thống kê thực tế: ô bảo trì không được tính là còn trống
            TotalSlots = _groups.Sum(g => g.TotalCount);
            OccupiedSlots = _groups.Sum(g => g.OccupiedCount);
            AvailableSlots = _groups.Sum(g => g.AvailableCount);
            MaintenanceSlots = _groups.Sum(g => g.MaintenanceCount);
            OccupancyRate = TotalSlots > 0 ? Math.Round((double)OccupiedSlots / TotalSlots * 100, 1) : 0;
            ZonesSummary = string.Join(" · ", _groups.Select(g => string.IsNullOrWhiteSpace(g.ZoneName) ? g.ZoneCode : g.ZoneName));

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
}
