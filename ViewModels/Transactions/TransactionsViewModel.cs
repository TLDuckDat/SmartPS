using System.Collections.ObjectModel;
using SmartPS.Models.Parking;
using SmartPS.Models.Payment;
using SmartPS.Services.GateControl;
using SmartPS.Services.Payment;

namespace SmartPS.ViewModels.Transactions;

public class TransactionsViewModel : ViewModelBase
{
    private readonly IGateControlService _gateControlService;
    private readonly IPaymentService _paymentService;
    private readonly List<ParkingSession> _allSessions = new();
    private readonly List<PaymentHistoryItem> _allPayments = new();

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

    private int _totalElectronicPayments;
    public int TotalElectronicPayments
    {
        get => _totalElectronicPayments;
        set => SetProperty(ref _totalElectronicPayments, value);
    }

    private int _paidElectronicPayments;
    public int PaidElectronicPayments
    {
        get => _paidElectronicPayments;
        set => SetProperty(ref _paidElectronicPayments, value);
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

    private int _selectedTabIndex;
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, value);
    }

    private bool _isPaymentDetailsOpen;
    public bool IsPaymentDetailsOpen
    {
        get => _isPaymentDetailsOpen;
        set => SetProperty(ref _isPaymentDetailsOpen, value);
    }

    private bool _isLoadingDetails;
    public bool IsLoadingDetails
    {
        get => _isLoadingDetails;
        set => SetProperty(ref _isLoadingDetails, value);
    }

    private PaymentDetailsResult? _selectedPaymentDetails;
    public PaymentDetailsResult? SelectedPaymentDetails
    {
        get => _selectedPaymentDetails;
        set => SetProperty(ref _selectedPaymentDetails, value);
    }

    public ObservableCollection<ParkingSession> FilteredSessions { get; } = new();
    public ObservableCollection<PaymentHistoryItem> FilteredPayments { get; } = new();

    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand ClearFilterCommand { get; }
    public AsyncRelayCommand ViewPaymentDetailsCommand { get; }
    public AsyncRelayCommand RefundPaymentCommand { get; }
    public RelayCommand ClosePaymentDetailsCommand { get; }

    public TransactionsViewModel(
        IGateControlService gateControlService,
        IPaymentService paymentService)
    {
        _gateControlService = gateControlService ?? throw new ArgumentNullException(nameof(gateControlService));
        _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));

        RefreshCommand = new AsyncRelayCommand(LoadDataAsync);
        ClearFilterCommand = new RelayCommand(() =>
        {
            SearchKeyword = string.Empty;
            SelectedStatusFilter = "Tất cả";
            SelectedPaymentFilter = "Tất cả";
        });

        ViewPaymentDetailsCommand = new AsyncRelayCommand(ExecuteViewPaymentDetailsAsync);
        RefundPaymentCommand = new AsyncRelayCommand(ExecuteRefundPaymentAsync, CanRefundPayment);
        ClosePaymentDetailsCommand = new RelayCommand(() =>
        {
            IsPaymentDetailsOpen = false;
            SelectedPaymentDetails = null;
        });

        _ = LoadDataAsync();
    }

    public async Task LoadDataAsync()
    {
        if (IsLoading) return;
        try
        {
            IsLoading = true;

            var historyTask = _gateControlService.GetAllSessionsHistoryAsync();
            var activeTask = _gateControlService.GetActiveSessionsAsync();
            var paymentsTask = _paymentService.GetPaymentHistoryAsync();

            await Task.WhenAll(historyTask, activeTask, paymentsTask);

            var history = await historyTask;
            var active = await activeTask;
            var payments = await paymentsTask;

            _allSessions.Clear();
            var seenIds = new HashSet<int>();
            foreach (var item in active.Concat(history))
            {
                if (item.SessionId > 0 && !seenIds.Add(item.SessionId))
                {
                    continue;
                }
                _allSessions.Add(item);
            }
            _allSessions.Sort((a, b) => b.CheckInTime.CompareTo(a.CheckInTime));

            _allPayments.Clear();
            _allPayments.AddRange(payments);

            // Cập nhật số liệu thống kê tài chính
            TotalTransactions = _allSessions.Count;
            CompletedTransactions = _allSessions.Count(s => s.Status == SessionStatus.Completed);
            ActiveTransactions = _allSessions.Count(s => s.Status == SessionStatus.Active);

            TotalRevenue = _allSessions.Where(s => s.Status == SessionStatus.Completed).Sum(s => s.TotalFee);
            CashRevenue = _allSessions.Where(s => s.Status == SessionStatus.Completed && s.PaymentMethod == PaymentMethod.Cash).Sum(s => s.TotalFee);
            VietQrRevenue = _allSessions.Where(s => s.Status == SessionStatus.Completed && s.PaymentMethod == PaymentMethod.VietQR).Sum(s => s.TotalFee);

            TotalElectronicPayments = _allPayments.Count;
            PaidElectronicPayments = _allPayments.Count(p => p.Status == PaymentStatus.Paid);

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

    private async Task ExecuteViewPaymentDetailsAsync(object? param)
    {
        int paymentId = 0;
        if (param is PaymentHistoryItem item)
        {
            paymentId = item.PaymentId;
        }
        else if (param is int id)
        {
            paymentId = id;
        }

        if (paymentId <= 0) return;

        try
        {
            IsLoadingDetails = true;
            IsPaymentDetailsOpen = true;
            SelectedPaymentDetails = await _paymentService.GetPaymentDetailsAsync(paymentId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ExecuteViewPaymentDetailsAsync Error]: {ex.Message}");
        }
        finally
        {
            IsLoadingDetails = false;
        }
    }

    private bool CanRefundPayment(object? param)
    {
        return param is PaymentHistoryItem item && item.Status == PaymentStatus.Paid;
    }

    private async Task ExecuteRefundPaymentAsync(object? param)
    {
        if (param is not PaymentHistoryItem item || item.Status != PaymentStatus.Paid)
        {
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            $"Xác nhận hoàn tiền giao dịch {item.TransactionReference}\n\nSố tiền: {item.Amount:N0} đ\n\nLưu ý: thao tác này ghi nhận giao dịch đã hoàn tiền trong SmartPS. Việc chuyển tiền thực tế cần được thực hiện theo quy trình hoàn tiền của nhà cung cấp thanh toán.",
            "Xác nhận hoàn tiền",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (confirm != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            IsLoading = true;
            var result = await _paymentService.ConfirmManualRefundAsync(
                item.PaymentId,
                "Hoàn tiền được xác nhận từ màn hình Sổ giao dịch");

            if (result.Success)
            {
                System.Windows.MessageBox.Show(
                    "Đã ghi nhận giao dịch ở trạng thái Refunded.",
                    "Hoàn tiền",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);

                await LoadDataAsync();
                if (SelectedPaymentDetails?.Payment.PaymentId == item.PaymentId)
                {
                    SelectedPaymentDetails = await _paymentService.GetPaymentDetailsAsync(item.PaymentId);
                }
            }
            else
            {
                System.Windows.MessageBox.Show(
                    result.Message,
                    "Không thể hoàn tiền",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Refund Error]: {ex.Message}");
            System.Windows.MessageBox.Show(
                "Có lỗi khi ghi nhận hoàn tiền.",
                "Hoàn tiền",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyFilters()
    {
        var kw = SearchKeyword?.Trim().ToUpperInvariant() ?? string.Empty;

        // 1. Lọc Sessions
        FilteredSessions.Clear();
        foreach (var s in _allSessions)
        {
            if (!string.IsNullOrEmpty(kw))
            {
                var matchPlate = !string.IsNullOrEmpty(s.LicensePlate) && s.LicensePlate.ToUpperInvariant().Contains(kw);
                var matchTicket = !string.IsNullOrEmpty(s.TicketCode) && s.TicketCode.ToUpperInvariant().Contains(kw);
                if (!matchPlate && !matchTicket)
                {
                    continue;
                }
            }

            if (SelectedStatusFilter == "Đã thanh toán" && s.Status != SessionStatus.Completed)
            {
                continue;
            }
            if (SelectedStatusFilter == "Đang đỗ" && s.Status != SessionStatus.Active)
            {
                continue;
            }
            if (SelectedStatusFilter == "Chờ thanh toán" && s.Status != SessionStatus.Active)
            {
                continue;
            }

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

        // 2. Lọc Electronic Payments
        FilteredPayments.Clear();
        foreach (var p in _allPayments)
        {
            if (!string.IsNullOrEmpty(kw))
            {
                var matchRef = !string.IsNullOrEmpty(p.TransactionReference) && p.TransactionReference.ToUpperInvariant().Contains(kw);
                var matchPlate = !string.IsNullOrEmpty(p.LicensePlate) && p.LicensePlate.ToUpperInvariant().Contains(kw);
                var matchTicket = !string.IsNullOrEmpty(p.TicketCode) && p.TicketCode.ToUpperInvariant().Contains(kw);
                if (!matchRef && !matchPlate && !matchTicket)
                {
                    continue;
                }
            }

            if (SelectedStatusFilter == "Đã thanh toán" && p.Status != PaymentStatus.Paid)
            {
                continue;
            }
            if (SelectedStatusFilter == "Chờ thanh toán" && p.Status != PaymentStatus.Pending && p.Status != PaymentStatus.Created)
            {
                continue;
            }
            if (SelectedStatusFilter == "Đang đỗ")
            {
                // Payments không có trạng thái Đang đỗ
                continue;
            }

            if (SelectedPaymentFilter == "Tiền mặt" && p.PaymentMethod != PaymentMethod.Cash)
            {
                continue;
            }
            if (SelectedPaymentFilter == "VietQR" && p.PaymentMethod != PaymentMethod.VietQR)
            {
                continue;
            }

            FilteredPayments.Add(p);
        }
    }
}
