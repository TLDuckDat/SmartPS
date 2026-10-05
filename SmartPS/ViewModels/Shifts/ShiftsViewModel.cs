using System.Collections.ObjectModel;
using System.Globalization;
using SmartPS.DTOs.Shifts;
using SmartPS.Models.Parking;
using SmartPS.Models.Shifts;
using SmartPS.Services.Auth;
using SmartPS.Services.Dialog;
using SmartPS.Services.Localization;
using SmartPS.Services.Shifts;

namespace SmartPS.ViewModels.Shifts;

public class ShiftsViewModel : ViewModelBase
{
    private readonly IShiftService _shiftService;
    private readonly IAuthService _authService;
    private readonly IDialogService _dialogService;
    private readonly ILocalizationService _localizationService;

    public ObservableCollection<ShiftTransactionItemViewModel> CurrentTransactions { get; } = new();
    public ObservableCollection<ShiftHistoryItemViewModel> ShiftHistory { get; } = new();
    public ObservableCollection<ShiftTransactionItemViewModel> SelectedShiftTransactions { get; } = new();
    public ObservableCollection<ShiftUserFilterOption> UserFilterOptions { get; } = new();
    public ObservableCollection<ShiftStatusFilterOption> StatusFilterOptions { get; } = new();
    public ObservableCollection<AdjustmentDirectionOption> AdjustmentDirectionOptions { get; } = new();
    public ObservableCollection<PaymentMethod> AdjustmentPaymentMethods { get; } = new()
    {
        PaymentMethod.Cash,
        PaymentMethod.VietQR,
        PaymentMethod.Card
    };
    public ObservableCollection<FinancialTransactionType> ManualTransactionTypes { get; } = new()
    {
        FinancialTransactionType.Refund,
        FinancialTransactionType.Adjustment,
        FinancialTransactionType.Incident
    };

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    private Shift? _currentShift;
    public Shift? CurrentShift
    {
        get => _currentShift;
        private set
        {
            if (SetProperty(ref _currentShift, value))
            {
                OnPropertyChanged(nameof(HasActiveShift));
                OnPropertyChanged(nameof(CurrentShiftCode));
                OnPropertyChanged(nameof(CurrentShiftOpenedAt));
                CommandManagerInvalidate();
            }
        }
    }

    private ShiftDashboardData? _currentDashboard;
    public ShiftDashboardData? CurrentDashboard
    {
        get => _currentDashboard;
        private set
        {
            if (SetProperty(ref _currentDashboard, value))
            {
                RaiseDashboardProperties();
            }
        }
    }

    public bool HasActiveShift => CurrentShift?.Status == ShiftStatus.Active;
    public string CurrentShiftCode => CurrentShift == null ? "--" : $"SHIFT-{CurrentShift.ShiftId:D6}";
    public string CurrentShiftOpenedAt => CurrentShift == null
        ? "--"
        : (CurrentShift.OpenedAt.Kind == DateTimeKind.Utc ? CurrentShift.OpenedAt.ToLocalTime() : CurrentShift.OpenedAt).ToString("dd/MM/yyyy HH:mm:ss");

    public string BeginningCashFormatted => FormatAmount(CurrentDashboard?.Shift.BeginningCash ?? 0);
    public string CashRevenueFormatted => FormatAmount(CurrentDashboard?.CashRevenue ?? 0);
    public string QrRevenueFormatted => FormatAmount(CurrentDashboard?.QrRevenue ?? 0);
    public string TotalRevenueFormatted => FormatAmount(CurrentDashboard?.TotalRevenue ?? 0);
    public string RefundFormatted => FormatAmount(CurrentDashboard?.RefundTotal ?? 0);
    public string AdjustmentFormatted => FormatAmount(CurrentDashboard?.AdjustmentTotal ?? 0);
    public string ExpectedCashFormatted => FormatAmount(CurrentDashboard?.ExpectedCash ?? 0);
    public int TransactionCount => CurrentDashboard?.TransactionCount ?? 0;

    private string _beginningCashInput = "0";
    public string BeginningCashInput
    {
        get => _beginningCashInput;
        set => SetProperty(ref _beginningCashInput, value);
    }

    private string _actualCashInput = string.Empty;
    public string ActualCashInput
    {
        get => _actualCashInput;
        set
        {
            if (SetProperty(ref _actualCashInput, value))
            {
                OnPropertyChanged(nameof(DifferencePreviewFormatted));
            }
        }
    }

    public string DifferencePreviewFormatted
    {
        get
        {
            if (CurrentDashboard == null || !TryParseMoney(ActualCashInput, out var actual)) return "--";
            return FormatAmount(actual - CurrentDashboard.ExpectedCash);
        }
    }

    private ShiftUserFilterOption? _selectedUserFilter;
    public ShiftUserFilterOption? SelectedUserFilter
    {
        get => _selectedUserFilter;
        set => SetProperty(ref _selectedUserFilter, value);
    }

    private ShiftStatusFilterOption? _selectedStatusFilter;
    public ShiftStatusFilterOption? SelectedStatusFilter
    {
        get => _selectedStatusFilter;
        set => SetProperty(ref _selectedStatusFilter, value);
    }

    private DateTime? _filterFromDate;
    public DateTime? FilterFromDate
    {
        get => _filterFromDate;
        set => SetProperty(ref _filterFromDate, value);
    }

    private DateTime? _filterToDate;
    public DateTime? FilterToDate
    {
        get => _filterToDate;
        set => SetProperty(ref _filterToDate, value);
    }

    private ShiftHistoryItemViewModel? _selectedHistoryShift;
    public ShiftHistoryItemViewModel? SelectedHistoryShift
    {
        get => _selectedHistoryShift;
        set
        {
            if (SetProperty(ref _selectedHistoryShift, value))
            {
                _ = LoadSelectedShiftAsync();
                OnPropertyChanged(nameof(HasSelectedHistoryShift));
                OnPropertyChanged(nameof(CanReviewSelectedShift));
                OnPropertyChanged(nameof(CanAdjustSelectedShift));
                OnPropertyChanged(nameof(CanCreateManualTransaction));
                CommandManagerInvalidate();
            }
        }
    }

    private ShiftDashboardData? _selectedShiftDashboard;
    public ShiftDashboardData? SelectedShiftDashboard
    {
        get => _selectedShiftDashboard;
        private set
        {
            if (SetProperty(ref _selectedShiftDashboard, value))
            {
                RaiseSelectedShiftProperties();
            }
        }
    }

    public bool HasSelectedHistoryShift => SelectedHistoryShift != null;
    public bool IsManager => IsManagerRole(_authService.CurrentUser?.Role?.RoleName);
    public bool CanReviewSelectedShift => IsManager && SelectedHistoryShift?.Shift.Status == ShiftStatus.Locked;
    public bool CanAdjustSelectedShift => IsManager && SelectedHistoryShift?.Shift.Status == ShiftStatus.Locked;
    public bool CanCreateManualTransaction => IsManager && SelectedHistoryShift?.Shift.Status == ShiftStatus.Active;

    public string SelectedBeginningCashFormatted => SelectedShiftDashboard == null ? "--" : FormatAmount(SelectedShiftDashboard.Shift.BeginningCash);
    public string SelectedExpectedCashFormatted => SelectedShiftDashboard == null ? "--" : FormatAmount(SelectedShiftDashboard.ExpectedCash);
    public string SelectedActualCashFormatted => SelectedShiftDashboard?.Shift.ActualCash is decimal value ? FormatAmount(value) : "--";
    public string SelectedDifferenceFormatted => SelectedShiftDashboard?.Shift.Difference is decimal value ? FormatAmount(value) : "--";
    public string SelectedCashRevenueFormatted => SelectedShiftDashboard == null ? "--" : FormatAmount(SelectedShiftDashboard.CashRevenue);
    public string SelectedQrRevenueFormatted => SelectedShiftDashboard == null ? "--" : FormatAmount(SelectedShiftDashboard.QrRevenue);
    public string SelectedTotalRevenueFormatted => SelectedShiftDashboard == null ? "--" : FormatAmount(SelectedShiftDashboard.TotalRevenue);

    public string AdjustmentExpectedCashBeforeFormatted
        => SelectedShiftDashboard == null ? "--" : FormatAmount(SelectedShiftDashboard.ExpectedCash);

    public string AdjustmentExpectedCashAfterFormatted
        => TryGetSignedAdjustmentAmount(out var signedAmount) && SelectedShiftDashboard != null
            ? FormatAmount(SelectedShiftDashboard.ExpectedCash + GetCashImpact(signedAmount))
            : "--";

    public string AdjustmentDifferenceBeforeFormatted
        => SelectedShiftDashboard?.Shift.ActualCash is decimal actualCash
            ? FormatAmount(actualCash - SelectedShiftDashboard.ExpectedCash)
            : "--";

    public string AdjustmentDifferenceAfterFormatted
        => TryGetSignedAdjustmentAmount(out var signedAmount)
           && SelectedShiftDashboard?.Shift.ActualCash is decimal actualCash
            ? FormatAmount(actualCash - (SelectedShiftDashboard.ExpectedCash + GetCashImpact(signedAmount)))
            : "--";

    public string AdjustmentCashImpactFormatted
        => TryGetSignedAdjustmentAmount(out var signedAmount)
            ? GetCashImpact(signedAmount) == 0
                ? GetString("Str_Shifts_AdjustmentNoCashImpact")
                : FormatAmount(GetCashImpact(signedAmount))
            : "--";

    private string _reviewNote = string.Empty;
    public string ReviewNote
    {
        get => _reviewNote;
        set => SetProperty(ref _reviewNote, value);
    }

    private FinancialTransactionType _selectedManualTransactionType = FinancialTransactionType.Refund;
    public FinancialTransactionType SelectedManualTransactionType
    {
        get => _selectedManualTransactionType;
        set => SetProperty(ref _selectedManualTransactionType, value);
    }

    private PaymentMethod _selectedManualPaymentMethod = PaymentMethod.Cash;
    public PaymentMethod SelectedManualPaymentMethod
    {
        get => _selectedManualPaymentMethod;
        set => SetProperty(ref _selectedManualPaymentMethod, value);
    }

    private string _manualAmountInput = string.Empty;
    public string ManualAmountInput
    {
        get => _manualAmountInput;
        set => SetProperty(ref _manualAmountInput, value);
    }

    private string _manualReferenceCode = string.Empty;
    public string ManualReferenceCode
    {
        get => _manualReferenceCode;
        set => SetProperty(ref _manualReferenceCode, value);
    }

    private string _manualNote = string.Empty;
    public string ManualNote
    {
        get => _manualNote;
        set => SetProperty(ref _manualNote, value);
    }

    private string _adjustmentAmountInput = string.Empty;
    public string AdjustmentAmountInput
    {
        get => _adjustmentAmountInput;
        set
        {
            if (SetProperty(ref _adjustmentAmountInput, value))
                RaiseAdjustmentPreviewProperties();
        }
    }

    private AdjustmentDirection _selectedAdjustmentDirection = AdjustmentDirection.Increase;
    public AdjustmentDirection SelectedAdjustmentDirection
    {
        get => _selectedAdjustmentDirection;
        set
        {
            if (SetProperty(ref _selectedAdjustmentDirection, value))
                RaiseAdjustmentPreviewProperties();
        }
    }

    private string _adjustmentReferenceCode = string.Empty;
    public string AdjustmentReferenceCode
    {
        get => _adjustmentReferenceCode;
        set => SetProperty(ref _adjustmentReferenceCode, value);
    }

    private string _adjustmentNote = string.Empty;
    public string AdjustmentNote
    {
        get => _adjustmentNote;
        set => SetProperty(ref _adjustmentNote, value);
    }

    private PaymentMethod _selectedAdjustmentPaymentMethod = PaymentMethod.Cash;
    public PaymentMethod SelectedAdjustmentPaymentMethod
    {
        get => _selectedAdjustmentPaymentMethod;
        set
        {
            if (SetProperty(ref _selectedAdjustmentPaymentMethod, value))
                RaiseAdjustmentPreviewProperties();
        }
    }

    public AsyncRelayCommand OpenShiftCommand { get; }
    public AsyncRelayCommand CloseShiftCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand ApplyHistoryFilterCommand { get; }
    public AsyncRelayCommand ReviewShiftCommand { get; }
    public AsyncRelayCommand CreateManualTransactionCommand { get; }
    public AsyncRelayCommand AddAdjustmentCommand { get; }
    public RelayCommand ClearHistoryFilterCommand { get; }

    public ShiftsViewModel(
        IShiftService shiftService,
        IAuthService authService,
        IDialogService dialogService,
        ILocalizationService localizationService)
    {
        _shiftService = shiftService ?? throw new ArgumentNullException(nameof(shiftService));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));

        StatusFilterOptions.Add(new ShiftStatusFilterOption { Status = null, DisplayName = GetString("Str_Shifts_AllStatuses") });
        StatusFilterOptions.Add(new ShiftStatusFilterOption { Status = ShiftStatus.Active, DisplayName = GetString("Str_Shifts_StatusActive") });
        StatusFilterOptions.Add(new ShiftStatusFilterOption { Status = ShiftStatus.Locked, DisplayName = GetString("Str_Shifts_StatusLocked") });
        StatusFilterOptions.Add(new ShiftStatusFilterOption { Status = ShiftStatus.Reviewed, DisplayName = GetString("Str_Shifts_StatusReviewed") });
        SelectedStatusFilter = StatusFilterOptions[0];

        AdjustmentDirectionOptions.Add(new AdjustmentDirectionOption
        {
            Direction = AdjustmentDirection.Increase,
            DisplayName = GetString("Str_Shifts_AdjustmentIncrease")
        });
        AdjustmentDirectionOptions.Add(new AdjustmentDirectionOption
        {
            Direction = AdjustmentDirection.Decrease,
            DisplayName = GetString("Str_Shifts_AdjustmentDecrease")
        });

        _localizationService.LanguageChanged += RefreshLocalizedContent;

        OpenShiftCommand = new AsyncRelayCommand(OpenShiftAsync);
        CloseShiftCommand = new AsyncRelayCommand(CloseShiftAsync);
        RefreshCommand = new AsyncRelayCommand(LoadDataAsync);
        ApplyHistoryFilterCommand = new AsyncRelayCommand(LoadHistoryAsync);
        ReviewShiftCommand = new AsyncRelayCommand(ReviewSelectedShiftAsync, () => CanReviewSelectedShift);
        CreateManualTransactionCommand = new AsyncRelayCommand(CreateManualTransactionAsync, () => CanCreateManualTransaction);
        AddAdjustmentCommand = new AsyncRelayCommand(AddAdjustmentAsync, () => CanAdjustSelectedShift);
        ClearHistoryFilterCommand = new RelayCommand(() =>
        {
            FilterFromDate = null;
            FilterToDate = null;
            SelectedStatusFilter = StatusFilterOptions.FirstOrDefault();
            SelectedUserFilter = UserFilterOptions.FirstOrDefault();
            _ = LoadHistoryAsync();
        });
    }

    public async Task LoadDataAsync()
    {
        if (IsLoading) return;

        try
        {
            IsLoading = true;
            var user = _authService.CurrentUser;
            if (user == null) return;

            await EnsureUserFiltersAsync();

            CurrentShift = await _shiftService.GetActiveShiftAsync(user.UserId);
            if (CurrentShift != null)
            {
                CurrentDashboard = await _shiftService.GetShiftDashboardAsync(CurrentShift.ShiftId);
                ReloadCurrentTransactions();
            }
            else
            {
                CurrentDashboard = null;
                CurrentTransactions.Clear();
            }

            await LoadHistoryAsync();
        }
        catch (Exception ex)
        {
            _dialogService.ShowError(GetString("Msg_Shifts_LoadError", ex.Message));
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task OpenShiftAsync()
    {
        var user = _authService.CurrentUser;
        if (user == null)
        {
            _dialogService.ShowWarning(GetString("Msg_Shifts_LoginRequired"));
            return;
        }

        if (!TryParseMoney(BeginningCashInput, out var beginningCash) || beginningCash < 0)
        {
            _dialogService.ShowWarning(GetString("Msg_Shifts_BeginningCashInvalid"));
            return;
        }

        try
        {
            await _shiftService.OpenShiftAsync(user.UserId, beginningCash);
            BeginningCashInput = "0";
            _dialogService.ShowSuccess(GetString("Msg_Shifts_OpenSuccess"));
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            _dialogService.ShowError(ex.Message);
        }
    }

    private async Task CloseShiftAsync()
    {
        if (CurrentShift == null) return;

        if (!TryParseMoney(ActualCashInput, out var actualCash) || actualCash < 0)
        {
            _dialogService.ShowWarning(GetString("Msg_Shifts_ActualCashInvalid"));
            return;
        }

        try
        {
            var closed = await _shiftService.CloseShiftAsync(CurrentShift.ShiftId, _authService.CurrentUser?.UserId ?? 0, actualCash);
            _dialogService.ShowSuccess(GetString("Msg_Shifts_CloseSuccess", FormatAmount(closed.Difference ?? 0)));
            ActualCashInput = string.Empty;
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            _dialogService.ShowError(ex.Message);
        }
    }

    private async Task EnsureUserFiltersAsync()
    {
        var currentUser = _authService.CurrentUser;
        if (currentUser == null || UserFilterOptions.Count > 0) return;

        if (IsManager)
        {
            UserFilterOptions.Add(new ShiftUserFilterOption { UserId = null, DisplayName = GetString("Str_Shifts_AllEmployees") });
            var users = await _authService.GetUsersAsync();
            foreach (var user in users.Where(x => x.IsActive).OrderBy(x => x.FullName))
            {
                UserFilterOptions.Add(new ShiftUserFilterOption
                {
                    UserId = user.UserId,
                    DisplayName = $"{user.FullName} ({user.Username})"
                });
            }
        }
        else
        {
            UserFilterOptions.Add(new ShiftUserFilterOption
            {
                UserId = currentUser.UserId,
                DisplayName = currentUser.FullName
            });
        }

        SelectedUserFilter = UserFilterOptions.FirstOrDefault();
        OnPropertyChanged(nameof(IsManager));
    }

    private async Task LoadHistoryAsync()
    {
        var currentUser = _authService.CurrentUser;
        if (currentUser == null) return;

        try
        {
            DateTime? fromUtc = null;
            DateTime? toUtc = null;

            if (FilterFromDate.HasValue)
                fromUtc = DateTime.SpecifyKind(FilterFromDate.Value.Date, DateTimeKind.Local).ToUniversalTime();
            if (FilterToDate.HasValue)
                toUtc = DateTime.SpecifyKind(FilterToDate.Value.Date.AddDays(1), DateTimeKind.Local).ToUniversalTime();

            var request = new ShiftFilterRequest
            {
                UserId = IsManager ? SelectedUserFilter?.UserId : currentUser.UserId,
                OpenedFromUtc = fromUtc,
                OpenedToUtc = toUtc,
                Status = SelectedStatusFilter?.Status,
                Limit = 300
            };

            var items = await _shiftService.GetShiftHistoryAsync(request);
            ShiftHistory.Clear();
            foreach (var shift in items)
                ShiftHistory.Add(new ShiftHistoryItemViewModel(shift, _localizationService));

            if (SelectedHistoryShift != null && ShiftHistory.All(x => x.ShiftId != SelectedHistoryShift.ShiftId))
                SelectedHistoryShift = null;
        }
        catch (Exception ex)
        {
            _dialogService.ShowError(GetString("Msg_Shifts_HistoryLoadError", ex.Message));
        }
    }

    private async Task LoadSelectedShiftAsync()
    {
        SelectedShiftTransactions.Clear();
        SelectedShiftDashboard = null;
        ReviewNote = string.Empty;

        if (SelectedHistoryShift == null) return;

        try
        {
            SelectedShiftDashboard = await _shiftService.GetShiftDashboardAsync(SelectedHistoryShift.ShiftId);
            foreach (var transaction in SelectedShiftDashboard.Transactions)
                SelectedShiftTransactions.Add(new ShiftTransactionItemViewModel(transaction, _localizationService));

            ReviewNote = SelectedShiftDashboard.Shift.ManagerNote ?? string.Empty;
        }
        catch (Exception ex)
        {
            _dialogService.ShowError(GetString("Msg_Shifts_DetailLoadError", ex.Message));
        }
    }

    private async Task ReviewSelectedShiftAsync()
    {
        var currentUser = _authService.CurrentUser;
        if (currentUser == null || SelectedHistoryShift == null || !CanReviewSelectedShift) return;

        try
        {
            var shiftId = SelectedHistoryShift.ShiftId;
            await _shiftService.ReviewShiftAsync(shiftId, currentUser.UserId, ReviewNote);
            _dialogService.ShowSuccess(GetString("Msg_Shifts_ReviewSuccess"));
            await LoadHistoryAsync();
            SelectedHistoryShift = ShiftHistory.FirstOrDefault(x => x.ShiftId == shiftId);
        }
        catch (Exception ex)
        {
            _dialogService.ShowError(ex.Message);
        }
    }

    private async Task CreateManualTransactionAsync()
    {
        var currentUser = _authService.CurrentUser;
        if (currentUser == null || SelectedHistoryShift == null || !CanCreateManualTransaction) return;

        if (!TryParseMoney(ManualAmountInput, out var amount) || amount == 0)
        {
            _dialogService.ShowWarning(GetString("Msg_Shifts_ManualAmountInvalid"));
            return;
        }

        if (string.IsNullOrWhiteSpace(ManualNote))
        {
            _dialogService.ShowWarning(GetString("Msg_Shifts_ManualNoteRequired"));
            return;
        }

        try
        {
            await _shiftService.CreateManualTransactionAsync(
                SelectedHistoryShift.ShiftId,
                currentUser.UserId,
                SelectedManualTransactionType,
                SelectedManualPaymentMethod,
                amount,
                ManualReferenceCode,
                ManualNote);

            ManualAmountInput = string.Empty;
            ManualReferenceCode = string.Empty;
            ManualNote = string.Empty;
            _dialogService.ShowSuccess(GetString("Msg_Shifts_ManualSuccess"));
            await LoadSelectedShiftAsync();
            await LoadHistoryAsync();
        }
        catch (Exception ex)
        {
            _dialogService.ShowError(ex.Message);
        }
    }

    private async Task AddAdjustmentAsync()
    {
        var currentUser = _authService.CurrentUser;
        if (currentUser == null || SelectedHistoryShift == null || !CanAdjustSelectedShift) return;

        if (!TryParseMoney(AdjustmentAmountInput, out var amount) || amount <= 0)
        {
            _dialogService.ShowWarning(GetString("Msg_Shifts_AdjustmentAmountInvalid"));
            return;
        }

        if (string.IsNullOrWhiteSpace(AdjustmentNote))
        {
            _dialogService.ShowWarning(GetString("Msg_Shifts_AdjustmentNoteRequired"));
            return;
        }

        try
        {
            var signedAmount = SelectedAdjustmentDirection == AdjustmentDirection.Decrease
                ? -Math.Abs(amount)
                : Math.Abs(amount);

            await _shiftService.CreateManualTransactionAsync(
                SelectedHistoryShift.ShiftId,
                currentUser.UserId,
                FinancialTransactionType.Adjustment,
                SelectedAdjustmentPaymentMethod,
                signedAmount,
                AdjustmentReferenceCode,
                AdjustmentNote);

            AdjustmentAmountInput = string.Empty;
            AdjustmentReferenceCode = string.Empty;
            AdjustmentNote = string.Empty;
            _dialogService.ShowSuccess(GetString("Msg_Shifts_AdjustmentSuccess"));

            await LoadSelectedShiftAsync();
            await LoadHistoryAsync();
        }
        catch (Exception ex)
        {
            _dialogService.ShowError(ex.Message);
        }
    }

    private void ReloadCurrentTransactions()
    {
        CurrentTransactions.Clear();
        if (CurrentDashboard == null) return;
        foreach (var transaction in CurrentDashboard.Transactions)
            CurrentTransactions.Add(new ShiftTransactionItemViewModel(transaction, _localizationService));
    }

    private void RaiseDashboardProperties()
    {
        OnPropertyChanged(nameof(BeginningCashFormatted));
        OnPropertyChanged(nameof(CashRevenueFormatted));
        OnPropertyChanged(nameof(QrRevenueFormatted));
        OnPropertyChanged(nameof(TotalRevenueFormatted));
        OnPropertyChanged(nameof(RefundFormatted));
        OnPropertyChanged(nameof(AdjustmentFormatted));
        OnPropertyChanged(nameof(ExpectedCashFormatted));
        OnPropertyChanged(nameof(TransactionCount));
        OnPropertyChanged(nameof(DifferencePreviewFormatted));
    }

    private void RaiseSelectedShiftProperties()
    {
        OnPropertyChanged(nameof(SelectedBeginningCashFormatted));
        OnPropertyChanged(nameof(SelectedExpectedCashFormatted));
        OnPropertyChanged(nameof(SelectedActualCashFormatted));
        OnPropertyChanged(nameof(SelectedDifferenceFormatted));
        OnPropertyChanged(nameof(SelectedCashRevenueFormatted));
        OnPropertyChanged(nameof(SelectedQrRevenueFormatted));
        OnPropertyChanged(nameof(SelectedTotalRevenueFormatted));
        RaiseAdjustmentPreviewProperties();
        OnPropertyChanged(nameof(CanReviewSelectedShift));
        OnPropertyChanged(nameof(CanAdjustSelectedShift));
        OnPropertyChanged(nameof(CanCreateManualTransaction));
        CommandManagerInvalidate();
    }

    private void RefreshLocalizedContent()
    {
        foreach (var option in StatusFilterOptions)
        {
            option.DisplayName = option.Status switch
            {
                null => GetString("Str_Shifts_AllStatuses"),
                ShiftStatus.Active => GetString("Str_Shifts_StatusActive"),
                ShiftStatus.Locked => GetString("Str_Shifts_StatusLocked"),
                ShiftStatus.Reviewed => GetString("Str_Shifts_StatusReviewed"),
                _ => option.DisplayName
            };
        }

        foreach (var option in UserFilterOptions.Where(x => x.UserId == null))
            option.DisplayName = GetString("Str_Shifts_AllEmployees");

        foreach (var option in AdjustmentDirectionOptions)
        {
            option.DisplayName = option.Direction switch
            {
                AdjustmentDirection.Increase => GetString("Str_Shifts_AdjustmentIncrease"),
                AdjustmentDirection.Decrease => GetString("Str_Shifts_AdjustmentDecrease"),
                _ => option.DisplayName
            };
        }

        foreach (var item in CurrentTransactions)
            item.RefreshLocalization();
        foreach (var item in SelectedShiftTransactions)
            item.RefreshLocalization();
        foreach (var item in ShiftHistory)
            item.RefreshLocalization();

        RaiseDashboardProperties();
        RaiseSelectedShiftProperties();
    }

    private string GetString(string key, params object[] args)
        => _localizationService.GetString(key, args);

    private string FormatAmount(decimal amount)
        => GetString("Str_Shifts_CurrencyFormat", amount);

    private void RaiseAdjustmentPreviewProperties()
    {
        OnPropertyChanged(nameof(AdjustmentExpectedCashBeforeFormatted));
        OnPropertyChanged(nameof(AdjustmentExpectedCashAfterFormatted));
        OnPropertyChanged(nameof(AdjustmentDifferenceBeforeFormatted));
        OnPropertyChanged(nameof(AdjustmentDifferenceAfterFormatted));
        OnPropertyChanged(nameof(AdjustmentCashImpactFormatted));
        CommandManagerInvalidate();
    }

    private bool TryGetSignedAdjustmentAmount(out decimal signedAmount)
    {
        signedAmount = 0;
        if (!TryParseMoney(AdjustmentAmountInput, out var amount) || amount <= 0)
            return false;

        signedAmount = SelectedAdjustmentDirection == AdjustmentDirection.Decrease
            ? -Math.Abs(amount)
            : Math.Abs(amount);
        return true;
    }

    private decimal GetCashImpact(decimal signedAmount)
        => SelectedAdjustmentPaymentMethod == PaymentMethod.Cash ? signedAmount : 0;

    private static bool TryParseMoney(string? text, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var clean = text.Trim().Replace("đ", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        return decimal.TryParse(clean, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.CurrentCulture, out value)
               || decimal.TryParse(clean, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }

    private static bool IsManagerRole(string? roleName)
        => roleName?.Equals("Admin", StringComparison.OrdinalIgnoreCase) == true
           || roleName?.Equals("Manager", StringComparison.OrdinalIgnoreCase) == true;

    private static void CommandManagerInvalidate()
        => System.Windows.Input.CommandManager.InvalidateRequerySuggested();
}
