using System.Collections.ObjectModel;
using SmartPS.Constants;
using SmartPS.DTOs.Parking;
using SmartPS.DTOs.Payment;
using SmartPS.DTOs.Shifts;
using SmartPS.Models.Parking;
using SmartPS.Models.Payment;
using SmartPS.Services.GateControl;
using SmartPS.Services.Payment;
using SmartPS.Services.Auth;
using SmartPS.Services.Authorization;
using SmartPS.Services.Audit;
using SmartPS.Services.Localization;
using SmartPS.Services.Shifts;
using SmartPS.Models.Shifts;
using SmartPS.ViewModels.Shifts;

namespace SmartPS.ViewModels.Transactions;

public class TransactionsViewModel : ViewModelBase
{
    private readonly IGateControlService _gateControlService;
    private readonly IPaymentService _paymentService;
    private readonly IAuthService _authService;
    private readonly IShiftService _shiftService;
    private readonly IPermissionService _permissionService;
    private readonly ILocalizationService _localizationService;
    private readonly List<ParkingSession> _allSessions = new();
    private readonly List<PaymentHistoryItem> _allPayments = new();
    private readonly List<FinancialTransaction> _allFinancialTransactions = new();
    private readonly List<Shift> _allShifts = new();

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

    private string _selectedStatusFilter = "All";
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

    private string _selectedPaymentFilter = "All";
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

    private DateTime? _fromDate;
    public DateTime? FromDate
    {
        get => _fromDate;
        set => SetProperty(ref _fromDate, value);
    }

    private DateTime? _toDate;
    public DateTime? ToDate
    {
        get => _toDate;
        set => SetProperty(ref _toDate, value);
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
    public ObservableCollection<ShiftTransactionItemViewModel> FilteredFinancialTransactions { get; } = new();
    public ObservableCollection<ShiftHistoryItemViewModel> FilteredShifts { get; } = new();

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand ApplyDateFilterCommand { get; }
    public RelayCommand ClearFilterCommand { get; }
    public AsyncRelayCommand ViewPaymentDetailsCommand { get; }
    public AsyncRelayCommand RefundPaymentCommand { get; }
    public RelayCommand ClosePaymentDetailsCommand { get; }

    public TransactionsViewModel(
        IGateControlService gateControlService,
        IPaymentService paymentService,
        IAuthService authService,
        IShiftService shiftService,
        IPermissionService permissionService,
        ILocalizationService localizationService)
    {
        _gateControlService = gateControlService ?? throw new ArgumentNullException(nameof(gateControlService));
        _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _shiftService = shiftService ?? throw new ArgumentNullException(nameof(shiftService));
        _permissionService = permissionService ?? throw new ArgumentNullException(nameof(permissionService));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));

        RefreshCommand = new AsyncRelayCommand(LoadDataAsync);
        ApplyDateFilterCommand = new AsyncRelayCommand(LoadDataAsync);
        ClearFilterCommand = new RelayCommand(() =>
        {
            SearchKeyword = string.Empty;
            SelectedStatusFilter = "All";
            SelectedPaymentFilter = "All";
            FromDate = null;
            ToDate = null;
            _ = LoadDataAsync();
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

            var userId = _authService.CurrentUser?.UserId ?? 0;
            if (userId <= 0)
            {
                return;
            }

            var fromUtc = ToVietnamDateStartUtc(FromDate);
            var toUtc = ToVietnamDateEndUtc(ToDate);
            var shiftTask = _shiftService.GetShiftHistoryAsync(new ShiftFilterRequest
            {
                UserId = userId,
                OpenedFromUtc = fromUtc,
                OpenedToUtc = toUtc,
                Limit = 500
            });
            var sessionsTask = _gateControlService.GetSessionsHistoryAsync(new ParkingSessionHistoryFilterRequest
            {
                UserId = userId,
                FromUtc = fromUtc,
                ToUtc = toUtc,
                Limit = 1000
            });
            var paymentsTask = _paymentService.GetPaymentHistoryAsync(new PaymentHistoryFilterRequest
            {
                CheckoutUserId = userId,
                FromUtc = fromUtc,
                ToUtc = toUtc,
                Limit = 500
            });
            var financialTask = _shiftService.GetTransactionsAsync(new FinancialTransactionFilterRequest
            {
                CreatedByUserId = userId,
                FromUtc = fromUtc,
                ToUtc = toUtc,
                Limit = 1000
            });

            await Task.WhenAll(shiftTask, sessionsTask, paymentsTask, financialTask);

            var shifts = await shiftTask;
            var sessions = await sessionsTask;
            var payments = await paymentsTask;
            var financialTransactions = await financialTask;

            _allSessions.Clear();
            _allSessions.AddRange(sessions);
            _allSessions.Sort((a, b) => b.CheckInTime.CompareTo(a.CheckInTime));

            _allShifts.Clear();
            _allShifts.AddRange(shifts);

            _allPayments.Clear();
            _allPayments.AddRange(payments);

            _allFinancialTransactions.Clear();
            _allFinancialTransactions.AddRange(financialTransactions);

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
        return param is PaymentHistoryItem item && item.Status == PaymentStatus.Paid
               && _permissionService.HasPermission(Permissions.PaymentRefund);
    }

    private async Task ExecuteRefundPaymentAsync(object? param)
    {
        if (param is not PaymentHistoryItem item || item.Status != PaymentStatus.Paid)
        {
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            AppText.Get("Msg_Tx_ConfirmRefund", item.TransactionReference, item.Amount),
            AppText.Get("Str_Tx_ConfirmRefundTitle"),
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
                "Hoàn tiền được xác nhận từ màn hình Sổ giao dịch",
                _authService.CurrentUser?.UserId ?? 0);

            if (result.Success)
            {
                System.Windows.MessageBox.Show(
                    AppText.Get("Msg_Tx_RefundRecorded"),
                    AppText.Get("Str_Tx_RefundTitle"),
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);

                IsLoading = false;
                await LoadDataAsync();
                if (SelectedPaymentDetails?.Payment.PaymentId == item.PaymentId)
                {
                    SelectedPaymentDetails = await _paymentService.GetPaymentDetailsAsync(item.PaymentId);
                }
            }
            else
            {
                System.Windows.MessageBox.Show(
                    result.IsPermissionDenied ? _localizationService.GetString("Msg_Auth_PermissionDenied") : result.Message,
                    AppText.Get("Str_Tx_RefundFailedTitle"),
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Refund Error]: {ex.Message}");
            System.Windows.MessageBox.Show(
                AppText.Get("Msg_Tx_RefundError"),
                AppText.Get("Str_Tx_RefundTitle"),
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

        FilteredShifts.Clear();
        foreach (var shift in _allShifts)
        {
            var item = new ShiftHistoryItemViewModel(shift, _localizationService);
            if (!string.IsNullOrEmpty(kw) &&
                !item.ShiftCode.ToUpperInvariant().Contains(kw) &&
                !item.EmployeeName.ToUpperInvariant().Contains(kw))
            {
                continue;
            }

            if (SelectedStatusFilter == "Active" && shift.Status != ShiftStatus.Active) continue;
            if (SelectedStatusFilter == "Locked" && shift.Status != ShiftStatus.Locked) continue;
            if (SelectedStatusFilter == "Reviewed" && shift.Status != ShiftStatus.Reviewed) continue;
            if (SelectedStatusFilter is "Completed" or "Paid" or "Pending" or "Parked") continue;
            FilteredShifts.Add(item);
        }

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

            if ((SelectedStatusFilter is "Paid" or "Completed") && s.Status != SessionStatus.Completed)
            {
                continue;
            }
            if ((SelectedStatusFilter is "Active" or "Parked") && s.Status != SessionStatus.Active)
            {
                continue;
            }
            if (SelectedStatusFilter == "Pending" && s.Status != SessionStatus.Active)
            {
                continue;
            }
            if (SelectedStatusFilter == "Locked" || SelectedStatusFilter == "Reviewed")
            {
                continue;
            }

            if (SelectedPaymentFilter == "Cash" && s.PaymentMethod != PaymentMethod.Cash)
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

            if ((SelectedStatusFilter is "Paid" or "Completed") && p.Status != PaymentStatus.Paid)
            {
                continue;
            }
            if (SelectedStatusFilter == "Pending" && p.Status != PaymentStatus.Pending && p.Status != PaymentStatus.Created)
            {
                continue;
            }
            if (SelectedStatusFilter is "Active" or "Parked" or "Locked" or "Reviewed")
            {
                // Payments không có trạng thái Đang đỗ
                continue;
            }
            if (SelectedStatusFilter == "Locked" || SelectedStatusFilter == "Reviewed")
            {
                continue;
            }

            if (SelectedPaymentFilter == "Cash" && p.PaymentMethod != PaymentMethod.Cash)
            {
                continue;
            }
            if (SelectedPaymentFilter == "VietQR" && p.PaymentMethod != PaymentMethod.VietQR)
            {
                continue;
            }

            FilteredPayments.Add(p);
        }

        FilteredFinancialTransactions.Clear();
        foreach (var transaction in _allFinancialTransactions)
        {
            if (!string.IsNullOrEmpty(kw) &&
                !transaction.TransactionCode.ToUpperInvariant().Contains(kw) &&
                !(transaction.ParkingSession?.LicensePlate?.ToUpperInvariant().Contains(kw) ?? false) &&
                !(transaction.ReferenceCode?.ToUpperInvariant().Contains(kw) ?? false)) continue;
            if (SelectedPaymentFilter == "Cash" && transaction.PaymentMethod != PaymentMethod.Cash) continue;
            if (SelectedPaymentFilter == "VietQR" && transaction.PaymentMethod != PaymentMethod.VietQR) continue;
            FilteredFinancialTransactions.Add(new ShiftTransactionItemViewModel(transaction));
        }
    }

    private static DateTime? ToVietnamDateStartUtc(DateTime? date)
        => date.HasValue
            ? AuditTime.VietnamDateStartUtc(DateOnly.FromDateTime(date.Value))
            : null;

    private static DateTime? ToVietnamDateEndUtc(DateTime? date)
        => date.HasValue
            ? AuditTime.VietnamDateStartUtc(DateOnly.FromDateTime(date.Value).AddDays(1))
            : null;
}
