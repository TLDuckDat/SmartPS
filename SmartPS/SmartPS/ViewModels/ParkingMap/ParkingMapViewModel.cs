using System.Collections.ObjectModel;
using SmartPS.Models.Parking;
using SmartPS.Services.GateControl;

namespace SmartPS.ViewModels.ParkingMap;

public class ParkingMapViewModel : ViewModelBase
{
    private readonly IGateControlService _gateControlService;

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

    private int _motorbikeOccupiedCount;
    public int MotorbikeOccupiedCount
    {
        get => _motorbikeOccupiedCount;
        set => SetProperty(ref _motorbikeOccupiedCount, value);
    }

    private int _motorbikeTotalCount = 10;
    public int MotorbikeTotalCount
    {
        get => _motorbikeTotalCount;
        set => SetProperty(ref _motorbikeTotalCount, value);
    }

    private int _carOccupiedCount;
    public int CarOccupiedCount
    {
        get => _carOccupiedCount;
        set => SetProperty(ref _carOccupiedCount, value);
    }

    private int _carTotalCount = 10;
    public int CarTotalCount
    {
        get => _carTotalCount;
        set => SetProperty(ref _carTotalCount, value);
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
                FilterSlots();
            }
        }
    }

    private ParkingSlotItemViewModel? _selectedSlot;
    public ParkingSlotItemViewModel? SelectedSlot
    {
        get => _selectedSlot;
        set => SetProperty(ref _selectedSlot, value);
    }

    public ObservableCollection<ParkingSlotItemViewModel> AllSlots { get; } = new();
    public ObservableCollection<ParkingSlotItemViewModel> MotorbikeSlots { get; } = new();
    public ObservableCollection<ParkingSlotItemViewModel> CarSlots { get; } = new();

    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand<ParkingSlotItemViewModel> SelectSlotCommand { get; }

    public ParkingMapViewModel(IGateControlService gateControlService)
    {
        _gateControlService = gateControlService ?? throw new ArgumentNullException(nameof(gateControlService));
        RefreshCommand = new AsyncRelayCommand(LoadDataAsync);
        SelectSlotCommand = new RelayCommand<ParkingSlotItemViewModel>(slot =>
        {
            SelectedSlot = slot;
        });

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

            var slotViewModels = new List<ParkingSlotItemViewModel>();

            foreach (var s in slots)
            {
                var activeSession = activeSessions.FirstOrDefault(sess =>
                    (sess.SlotId.HasValue && sess.SlotId.Value == s.SlotId) ||
                    (!string.IsNullOrEmpty(s.CurrentLicensePlate) &&
                     string.Equals(sess.LicensePlate, s.CurrentLicensePlate, StringComparison.OrdinalIgnoreCase)));

                var isOccupied = s.Status == SlotStatus.Occupied || activeSession != null;
                var currentPlate = s.CurrentLicensePlate ?? activeSession?.LicensePlate;

                var vm = new ParkingSlotItemViewModel
                {
                    SlotId = s.SlotId,
                    SlotCode = s.SlotCode,
                    ZoneName = s.ZoneName,
                    VehicleTypeId = s.VehicleTypeId,
                    VehicleTypeName = s.VehicleType?.TypeName ?? (s.VehicleTypeId == 1 ? "Xe máy" : "Ô tô con"),
                    Status = isOccupied ? SlotStatus.Occupied : SlotStatus.Available,
                    CurrentLicensePlate = currentPlate,
                    CheckInTime = activeSession?.CheckInTimeLocal,
                    TicketCode = activeSession?.TicketCode,
                    CustomerName = activeSession?.Customer?.FullName
                };

                slotViewModels.Add(vm);
            }

            AllSlots.Clear();
            foreach (var vm in slotViewModels)
            {
                AllSlots.Add(vm);
            }

            // Tính toán chỉ số thống kê thực tế
            TotalSlots = AllSlots.Count;
            OccupiedSlots = AllSlots.Count(s => s.IsOccupied);
            AvailableSlots = Math.Max(0, TotalSlots - OccupiedSlots);
            OccupancyRate = TotalSlots > 0 ? Math.Round((double)OccupiedSlots / TotalSlots * 100, 1) : 0;

            MotorbikeTotalCount = AllSlots.Count(s => s.VehicleTypeId == 1 || s.SlotCode.StartsWith("A"));
            MotorbikeOccupiedCount = AllSlots.Count(s => (s.VehicleTypeId == 1 || s.SlotCode.StartsWith("A")) && s.IsOccupied);

            CarTotalCount = AllSlots.Count(s => s.VehicleTypeId == 2 || s.SlotCode.StartsWith("B"));
            CarOccupiedCount = AllSlots.Count(s => (s.VehicleTypeId == 2 || s.SlotCode.StartsWith("B")) && s.IsOccupied);

            FilterSlots();

            // Nếu slot đang chọn vẫn tồn tại thì cập nhật lại tham chiếu
            if (SelectedSlot != null)
            {
                SelectedSlot = AllSlots.FirstOrDefault(s => s.SlotId == SelectedSlot.SlotId);
            }
            else
            {
                SelectedSlot = AllSlots.FirstOrDefault(s => s.IsOccupied) ?? AllSlots.FirstOrDefault();
            }
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

    private void FilterSlots()
    {
        var kw = SearchKeyword?.Trim().ToUpperInvariant() ?? string.Empty;

        MotorbikeSlots.Clear();
        CarSlots.Clear();

        foreach (var s in AllSlots)
        {
            var matchKw = string.IsNullOrEmpty(kw) ||
                          s.SlotCode.ToUpperInvariant().Contains(kw) ||
                          (!string.IsNullOrEmpty(s.CurrentLicensePlate) && s.CurrentLicensePlate.ToUpperInvariant().Contains(kw));

            if (!matchKw) continue;

            if (s.VehicleTypeId == 1 || s.SlotCode.StartsWith("A"))
            {
                MotorbikeSlots.Add(s);
            }
            else
            {
                CarSlots.Add(s);
            }
        }
    }
}
