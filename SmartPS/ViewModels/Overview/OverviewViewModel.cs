using System.Collections.ObjectModel;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using SmartPS.Models.Parking;
using SmartPS.Models.Reports;
using SmartPS.Services.Localization;
using SmartPS.Services.Reports;
using SmartPS.ViewModels.Reports;

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
                OnPropertyChanged(nameof(OccupancyRateValue));
            }
        }
    }
    public string OccupancyRateFormatted => ReportFormat.Percent(OccupancyRate);

    // Phần số tách khỏi đơn vị "%" để thẻ KPI đặt số lớn và đơn vị nhỏ trên cùng một đường chân chữ
    public string OccupancyRateValue => OccupancyRate.ToString("0.#", ReportFormat.VietnameseNumbers);

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
                OnPropertyChanged(nameof(TodayRevenueAmount));
            }
        }
    }
    public string TodayRevenueFormatted => ReportFormat.Currency(TodayRevenue);

    // Phần số của doanh thu, đơn vị "₫" hiển thị riêng
    public string TodayRevenueAmount => TodayRevenue.ToString("#,##0", ReportFormat.VietnameseNumbers);

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
    private int _visitorParkedCount;
    public int VisitorParkedCount
    {
        get => _visitorParkedCount;
        set => SetProperty(ref _visitorParkedCount, value);
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

    // Biểu đồ cột "xe đang gửi theo loại" (LiveCharts2), cập nhật mỗi lần nạp dữ liệu
    private ISeries[] _parkedSeries = Array.Empty<ISeries>();
    public ISeries[] ParkedSeries { get => _parkedSeries; private set => SetProperty(ref _parkedSeries, value); }

    private Axis[] _parkedXAxes = Array.Empty<Axis>();
    public Axis[] ParkedXAxes { get => _parkedXAxes; private set => SetProperty(ref _parkedXAxes, value); }

    private Axis[] _parkedYAxes = Array.Empty<Axis>();
    public Axis[] ParkedYAxes { get => _parkedYAxes; private set => SetProperty(ref _parkedYAxes, value); }

    public ObservableCollection<ParkingSession> RecentSessions { get; } = new();

    // Hiện trạng thái trống thay cho khung/bảng rỗng
    public bool HasParkedByVehicleType => ParkedByVehicleType.Count > 0;
    public bool HasRecentSessions => RecentSessions.Count > 0;

    public AsyncRelayCommand RefreshCommand { get; }

    public OverviewViewModel(IReportService reportService, ILocalizationService localization)
    {
        _reportService = reportService ?? throw new ArgumentNullException(nameof(reportService));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        RefreshCommand = new AsyncRelayCommand(LoadDataAsync);

        ParkedByVehicleType.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasParkedByVehicleType));
        RecentSessions.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasRecentSessions));
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
            VisitorParkedCount = snapshot.ParkedVisitors;

            ParkedByVehicleType.Clear();
            foreach (var item in snapshot.ParkedByVehicleType)
            {
                ParkedByVehicleType.Add(item);
            }

            // Biểu đồ cột dựng từ đúng số liệu vừa nạp từ CSDL
            var parkedItems = ParkedByVehicleType.ToList();
            ParkedSeries = LiveChartsSeriesFactory.CreateParkedByVehicleType(
                parkedItems, _localization.GetString("Str_Ov_ParkedByVehicleType"));
            ParkedXAxes = parkedItems.Count == 0 ? Array.Empty<Axis>() : LiveChartsSeriesFactory.CreateParkedCategoryAxes(parkedItems);
            ParkedYAxes = parkedItems.Count == 0 ? Array.Empty<Axis>() : LiveChartsSeriesFactory.CreateParkedValueAxes();

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
