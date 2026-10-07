using System.Collections.ObjectModel;
using SmartPS.Models.Parking;
using SmartPS.Models.Reports;
using SmartPS.Services.Localization;
using SmartPS.Services.Reports;

namespace SmartPS.ViewModels.Overview;

public class OverviewViewModel : ViewModelBase
{
    private const int RecentSessionCount = 15;

    private readonly IReportService _reportService;
    private readonly ILocalizationService _localization;

    private int _totalParkedVehicles;
    public int TotalParkedVehicles
    {
        get => _totalParkedVehicles;
        set => SetProperty(ref _totalParkedVehicles, value);
    }

    private int _totalSlots;
    public int TotalSlots
    {
        get => _totalSlots;
        set => SetProperty(ref _totalSlots, value);
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
    public string OccupancyRateFormatted => ReportFormat.Percent(OccupancyRate);

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
    public string TodayRevenueFormatted => ReportFormat.Currency(TodayRevenue);

    private int _residentParkedCount;
    public int ResidentParkedCount
    {
        get => _residentParkedCount;
        set => SetProperty(ref _residentParkedCount, value);
    }

    private int _monthlyParkedCount;
    public int MonthlyParkedCount
    {
        get => _monthlyParkedCount;
        set => SetProperty(ref _monthlyParkedCount, value);
    }

    // Visitors: neither residents nor monthly-pass holders.
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

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrEmpty(_errorMessage);

    public ObservableCollection<VehicleTypeCount> ParkedByVehicleType { get; } = new();

    public ObservableCollection<ParkingSession> RecentSessions { get; } = new();

    public AsyncRelayCommand RefreshCommand { get; }

    public OverviewViewModel(IReportService reportService, ILocalizationService localization)
    {
        _reportService = reportService ?? throw new ArgumentNullException(nameof(reportService));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        RefreshCommand = new AsyncRelayCommand(LoadDataAsync);
    }

    public async Task LoadDataAsync()
    {
        if (IsLoading) return;
        try
        {
            IsLoading = true;
            var snapshot = await _reportService.GetOverviewSnapshotAsync();
            var recent = await _reportService.GetRecentSessionsAsync(RecentSessionCount);

            TotalParkedVehicles = snapshot.OccupiedNow;
            TotalSlots = snapshot.TotalSlots;
            AvailableSlots = snapshot.AvailableSlots;
            OccupancyRate = snapshot.OccupancyPercent;
            TodayCheckIns = snapshot.TodayCheckIns;
            TodayCheckOuts = snapshot.TodayCheckOuts;
            TodayRevenue = snapshot.TodayNetRevenue;
            ResidentParkedCount = snapshot.ParkedResidents;
            MonthlyParkedCount = snapshot.ParkedMonthlyPass;
            RegularParkedCount = snapshot.ParkedVisitors;

            ParkedByVehicleType.Clear();
            foreach (var item in snapshot.ParkedByVehicleType)
            {
                ParkedByVehicleType.Add(item);
            }

            RecentSessions.Clear();
            foreach (var session in recent)
            {
                RecentSessions.Add(session);
            }

            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OverviewViewModel Error]: {ex.Message}");
            ErrorMessage = _localization.GetString(ReportTextKeys.MsgOvLoadError);
        }
        finally
        {
            IsLoading = false;
        }
    }
}
