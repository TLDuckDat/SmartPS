using System.Collections.ObjectModel;
using SmartPS.Models.Parking;
using SmartPS.Services.GateControl;

namespace SmartPS.ViewModels.Transactions;

public class TransactionsViewModel : ViewModelBase
{
    private readonly IGateControlService _gateControlService;
    private readonly List<ParkingSession> _allSessions = new();

    private decimal _totalRevenue;
    public decimal TotalRevenue
    {
        get => _totalRevenue;
        set
        {
            if (SetProperty(ref _totalRevenue, value))
            {
                OnPropertyChanged(nameof(TotalRevenueFormatted));
            }
        }
    }
    public string TotalRevenueFormatted => $"{TotalRevenue:N0} đ";

    private decimal _cashRevenue;
    public decimal CashRevenue
    {
        get => _cashRevenue;
        set
        {
            if (SetProperty(ref _cashRevenue, value))
            {
                OnPropertyChanged(nameof(CashRevenueFormatted));
            }
        }
    }
    public string CashRevenueFormatted => $"{CashRevenue:N0} đ";

    private decimal _vietQrRevenue;
    public decimal VietQrRevenue
    {
        get => _vietQrRevenue;
        set
        {
            if (SetProperty(ref _vietQrRevenue, value))
            {
                OnPropertyChanged(nameof(VietQrRevenueFormatted));
            }
        }
    }
    public string VietQrRevenueFormatted => $"{VietQrRevenue:N0} đ";

    private int _totalTransactions;
    public int TotalTransactions
    {
        get => _totalTransactions;
        set => SetProperty(ref _totalTransactions, value);
    }

    private int _completedTransactions;
    public int CompletedTransactions
    {
        get => _completedTransactions;
        set => SetProperty(ref _completedTransactions, value);
    }

    private int _activeTransactions;
    public int ActiveTransactions
    {
        get => _activeTransactions;
        set => SetProperty(ref _activeTransactions, value);
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
                ApplyFilters();
            }
        }
    }

    private string _selectedStatusFilter = "Tất cả";
    public string SelectedStatusFilter
    {
        get => _selectedStatusFilter;
        set
        {
            if (SetProperty(ref _selectedStatusFilter, value))
            {
                ApplyFilters();
            }
        }
    }

    private string _selectedPaymentFilter = "Tất cả";
    public string SelectedPaymentFilter
    {
        get => _selectedPaymentFilter;
        set
        {
            if (SetProperty(ref _selectedPaymentFilter, value))
            {
                ApplyFilters();
            }
        }
    }

    public ObservableCollection<ParkingSession> FilteredSessions { get; } = new();

    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand ClearFilterCommand { get; }

    public TransactionsViewModel(IGateControlService gateControlService)
    {
        _gateControlService = gateControlService ?? throw new ArgumentNullException(nameof(gateControlService));
        RefreshCommand = new AsyncRelayCommand(LoadDataAsync);
        ClearFilterCommand = new RelayCommand(() =>
        {
            SearchKeyword = string.Empty;
            SelectedStatusFilter = "Tất cả";
            SelectedPaymentFilter = "Tất cả";
        });

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

            _allSessions.Clear();

            // Gộp tất cả các phiên (active + completed), loại bỏ trùng SessionId hoặc TicketCode
            var seenIds = new HashSet<int>();
            foreach (var item in active.Concat(history))
            {
                if (item.SessionId > 0 && !seenIds.Add(item.SessionId))
                {
                    continue;
                }
                _allSessions.Add(item);
            }

            // Sắp xếp giảm dần theo thời gian check-in mới nhất
            _allSessions.Sort((a, b) => b.CheckInTime.CompareTo(a.CheckInTime));

            // Tính toán số liệu thống kê tài chính
            TotalTransactions = _allSessions.Count;
            CompletedTransactions = _allSessions.Count(s => s.Status == SessionStatus.Completed);
            ActiveTransactions = _allSessions.Count(s => s.Status == SessionStatus.Active);

            TotalRevenue = _allSessions.Where(s => s.Status == SessionStatus.Completed).Sum(s => s.TotalFee);
            CashRevenue = _allSessions.Where(s => s.Status == SessionStatus.Completed && s.PaymentMethod == PaymentMethod.Cash).Sum(s => s.TotalFee);
            VietQrRevenue = _allSessions.Where(s => s.Status == SessionStatus.Completed && s.PaymentMethod == PaymentMethod.VietQR).Sum(s => s.TotalFee);

            ApplyFilters();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TransactionsViewModel Error]: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyFilters()
    {
        var kw = SearchKeyword?.Trim().ToUpperInvariant() ?? string.Empty;

        FilteredSessions.Clear();

        foreach (var s in _allSessions)
        {
            // Lọc theo từ khóa tìm kiếm (Biển số hoặc Mã vé)
            if (!string.IsNullOrEmpty(kw))
            {
                var matchPlate = !string.IsNullOrEmpty(s.LicensePlate) && s.LicensePlate.ToUpperInvariant().Contains(kw);
                var matchTicket = !string.IsNullOrEmpty(s.TicketCode) && s.TicketCode.ToUpperInvariant().Contains(kw);
                if (!matchPlate && !matchTicket)
                {
                    continue;
                }
            }

            // Lọc theo trạng thái
            if (SelectedStatusFilter == "Đã thanh toán" && s.Status != SessionStatus.Completed)
            {
                continue;
            }
            if (SelectedStatusFilter == "Đang đỗ" && s.Status != SessionStatus.Active)
            {
                continue;
            }

            // Lọc theo hình thức thanh toán
            if (SelectedPaymentFilter == "Tiền mặt" && s.PaymentMethod != PaymentMethod.Cash)
            {
                continue;
            }
            if (SelectedPaymentFilter == "VietQR" && s.PaymentMethod != PaymentMethod.VietQR)
            {
                continue;
            }

            FilteredSessions.Add(s);
        }
    }
}
