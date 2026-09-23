using System.Collections.ObjectModel;
using SmartPS.Models.GateControl;
using SmartPS.Models.Parking;
using SmartPS.Services.GateControl;

namespace SmartPS.ViewModels.Overview;

public class OverviewViewModel : ViewModelBase
{
    private readonly IGateControlService _gateControlService;

    private int _totalParkedVehicles;
    public int TotalParkedVehicles
    {
        get => _totalParkedVehicles;
        set => SetProperty(ref _totalParkedVehicles, value);
    }

    private int _totalSlots = 20;
    public int TotalSlots
    {
        get => _totalSlots;
        set => SetProperty(ref _totalSlots, value);
    }

    private int _availableSlots = 20;
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

    private int _todayCheckIns;
    public int TodayCheckIns
    {
        get => _todayCheckIns;
        set => SetProperty(ref _todayCheckIns, value);
    }

    private int _todayCheckOuts;
    public int TodayCheckOuts
    {
        get => _todayCheckOuts;
        set => SetProperty(ref _todayCheckOuts, value);
    }

    private decimal _todayRevenue;
    public decimal TodayRevenue
    {
        get => _todayRevenue;
        set
        {
            if (SetProperty(ref _todayRevenue, value))
            {
                OnPropertyChanged(nameof(TodayRevenueFormatted));
            }
        }
    }
    public string TodayRevenueFormatted => $"{TodayRevenue:N0} đ";

    private int _motorbikeParkedCount;
    public int MotorbikeParkedCount
    {
        get => _motorbikeParkedCount;
        set => SetProperty(ref _motorbikeParkedCount, value);
    }

    private int _carParkedCount;
    public int CarParkedCount
    {
        get => _carParkedCount;
        set => SetProperty(ref _carParkedCount, value);
    }

    private int _monthlyParkedCount;
    public int MonthlyParkedCount
    {
        get => _monthlyParkedCount;
        set => SetProperty(ref _monthlyParkedCount, value);
    }

    private int _regularParkedCount;
    public int RegularParkedCount
    {
        get => _regularParkedCount;
        set => SetProperty(ref _regularParkedCount, value);
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public ObservableCollection<ParkingSession> RecentSessions { get; } = new();

    public AsyncRelayCommand RefreshCommand { get; }

    public OverviewViewModel(IGateControlService gateControlService)
    {
        _gateControlService = gateControlService ?? throw new ArgumentNullException(nameof(gateControlService));
        RefreshCommand = new AsyncRelayCommand(LoadDataAsync);
        _ = LoadDataAsync();
    }

    public async Task LoadDataAsync()
    {
        if (IsLoading) return;
        try
        {
            IsLoading = true;
            var kpi = await _gateControlService.GetOverviewKpiAsync();

            TotalParkedVehicles = kpi.TotalParkedVehicles;
            TotalSlots = kpi.TotalSlots;
            AvailableSlots = kpi.AvailableSlots;
            OccupancyRate = kpi.OccupancyRate;
            TodayCheckIns = kpi.TodayCheckIns;
            TodayCheckOuts = kpi.TodayCheckOuts;
            TodayRevenue = kpi.TodayRevenue;
            MotorbikeParkedCount = kpi.MotorbikeParkedCount;
            CarParkedCount = kpi.CarParkedCount;
            MonthlyParkedCount = kpi.MonthlyParkedCount;
            RegularParkedCount = kpi.RegularParkedCount;

            var history = await _gateControlService.GetAllSessionsHistoryAsync();
            RecentSessions.Clear();
            foreach (var session in history.Take(15))
            {
                RecentSessions.Add(session);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OverviewViewModel Error]: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }
}