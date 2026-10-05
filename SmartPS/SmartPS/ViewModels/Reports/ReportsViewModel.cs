using System.Collections.ObjectModel;
using SmartPS.Models.Parking;
using SmartPS.Services.GateControl;

namespace SmartPS.ViewModels.Reports;

public class ReportsViewModel : ViewModelBase
{
    private readonly IGateControlService _gateControlService;

    private decimal _cumulativeRevenue;
    public decimal CumulativeRevenue
    {
        get => _cumulativeRevenue;
        set
        {
            if (SetProperty(ref _cumulativeRevenue, value))
            {
                OnPropertyChanged(nameof(CumulativeRevenueFormatted));
            }
        }
    }
    public string CumulativeRevenueFormatted => $"{CumulativeRevenue:N0} đ";

    private int _cumulativeVehicles;
    public int CumulativeVehicles
    {
        get => _cumulativeVehicles;
        set => SetProperty(ref _cumulativeVehicles, value);
    }

    private double _turnoverRate;
    public double TurnoverRate
    {
        get => _turnoverRate;
        set => SetProperty(ref _turnoverRate, value);
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public ObservableCollection<DailyReportItem> ReportItems { get; } = new();

    public AsyncRelayCommand RefreshCommand { get; }

    public ReportsViewModel(IGateControlService gateControlService)
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

            var history = await _gateControlService.GetAllSessionsHistoryAsync();
            var active = await _gateControlService.GetActiveSessionsAsync();

            var all = active.Concat(history).GroupBy(s => s.SessionId).Select(g => g.First()).ToList();

            CumulativeVehicles = all.Count;
            CumulativeRevenue = all.Where(s => s.Status == SessionStatus.Completed).Sum(s => s.TotalFee);
            TurnoverRate = all.Count > 0 ? Math.Round((double)all.Count / 20.0, 2) : 0; // Hệ số quay vòng trên 20 ô đỗ

            // Nhóm theo ngày
            ReportItems.Clear();
            var grouped = all.GroupBy(s => s.CheckInTime.Date).OrderByDescending(g => g.Key).Take(7);

            foreach (var g in grouped)
            {
                var checkIns = g.Count();
                var checkOuts = g.Count(s => s.Status == SessionStatus.Completed);
                var rev = g.Where(s => s.Status == SessionStatus.Completed).Sum(s => s.TotalFee);
                var mb = g.Count(s => s.VehicleTypeId == 1);
                var car = g.Count(s => s.VehicleTypeId == 2);

                ReportItems.Add(new DailyReportItem
                {
                    DateDisplay = g.Key.ToString("dd/MM/yyyy"),
                    TotalCheckIns = checkIns,
                    TotalCheckOuts = checkOuts,
                    Revenue = rev,
                    MotorbikeCount = mb,
                    CarCount = car
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ReportsViewModel Error]: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }
}
