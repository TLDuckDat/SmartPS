using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using SmartPS.Constants;
using SmartPS.Models.Parking;
using SmartPS.Services.Authorization;
using SmartPS.Services.Common;
using SmartPS.Services.Customers;
using SmartPS.Services.Dialog;
using SmartPS.Services.Localization;
using SmartPS.Views.Customers;

namespace SmartPS.ViewModels.Customers;

public class CustomersViewModel : ViewModelBase
{
    private readonly ICustomersService _customersService;
    private readonly ITicketsService _ticketsService;
    private readonly IVehiclesService _vehiclesService;
    private readonly IDialogService _dialogService;
    private readonly IPermissionService _permissions;

    private readonly List<CustomerItemViewModel> _allCustomers = new();
    private readonly List<Household> _allHouseholds = new();
    private readonly List<MonthlyTicket> _allTickets = new();
    private readonly List<BlacklistedVehicle> _allBlacklist = new();

    // Grids
    public ObservableCollection<CustomerItemViewModel> FilteredCustomers { get; } = new();
    public ObservableCollection<Household> Households { get; } = new();
    public ObservableCollection<MonthlyTicket> Tickets { get; } = new();
    public ObservableCollection<BlacklistedVehicle> BlacklistVehicles { get; } = new();

    // Selections
    private CustomerItemViewModel? _selectedCustomer;
    public CustomerItemViewModel? SelectedCustomer { get => _selectedCustomer; set => SetProperty(ref _selectedCustomer, value); }
    private Household? _selectedHousehold;
    public Household? SelectedHousehold { get => _selectedHousehold; set => SetProperty(ref _selectedHousehold, value); }
    private MonthlyTicket? _selectedTicket;
    public MonthlyTicket? SelectedTicket
    {
        get => _selectedTicket;
        set
        {
            if (SetProperty(ref _selectedTicket, value))
            {
                _ = LoadTicketHistoriesAsync(value);
            }
        }
    }

    public ObservableCollection<MonthlyTicketHistory> SelectedTicketHistories { get; } = new();
    private bool _isLoadingHistories;
    public bool IsLoadingHistories { get => _isLoadingHistories; set => SetProperty(ref _isLoadingHistories, value); }
    private BlacklistedVehicle? _selectedBlacklist;
    public BlacklistedVehicle? SelectedBlacklist { get => _selectedBlacklist; set => SetProperty(ref _selectedBlacklist, value); }

    // Search & Sort for Customers
    private string _searchKeyword = string.Empty;
    public string SearchKeyword { get => _searchKeyword; set { if (SetProperty(ref _searchKeyword, value)) ApplyCustomerFilter(); } }

    public string[] CustomerSortOptions => new[] { AppText.Get("Str_Customers_SortDefault"), AppText.Get("Str_Customers_SortByName") };
    private int _selectedCustomerSortIndex = 0;
    public int SelectedCustomerSortIndex
    {
        get => _selectedCustomerSortIndex;
        set { if (SetProperty(ref _selectedCustomerSortIndex, value)) ApplyCustomerFilter(); }
    }

    // Search & Sort for Households
    private string _householdSearchKeyword = string.Empty;
    public string HouseholdSearchKeyword { get => _householdSearchKeyword; set { if (SetProperty(ref _householdSearchKeyword, value)) ApplyHouseholdFilter(); } }

    public string[] HouseholdSortOptions => new[] { AppText.Get("Str_Customers_SortDefault"), AppText.Get("Str_Customers_SortByApartment") };
    private int _selectedHouseholdSortIndex = 0;
    public int SelectedHouseholdSortIndex
    {
        get => _selectedHouseholdSortIndex;
        set { if (SetProperty(ref _selectedHouseholdSortIndex, value)) ApplyHouseholdFilter(); }
    }

    // Search & Sort for Monthly Tickets
    private string _ticketSearchKeyword = string.Empty;
    public string TicketSearchKeyword { get => _ticketSearchKeyword; set { if (SetProperty(ref _ticketSearchKeyword, value)) ApplyTicketFilter(); } }

    private bool _onlyExpiringSoonTickets;
    public bool OnlyExpiringSoonTickets
    {
        get => _onlyExpiringSoonTickets;
        set { if (SetProperty(ref _onlyExpiringSoonTickets, value)) ApplyTicketFilter(); }
    }

    public string[] TicketSortOptions => new[] { AppText.Get("Str_Customers_SortDefault"), AppText.Get("Str_Customers_SortByName"), AppText.Get("Str_Customers_SortByExpiry") };
    private int _selectedTicketSortIndex = 0;
    public int SelectedTicketSortIndex
    {
        get => _selectedTicketSortIndex;
        set { if (SetProperty(ref _selectedTicketSortIndex, value)) ApplyTicketFilter(); }
    }

    // Search for Blacklist
    private string _blacklistSearchKeyword = string.Empty;
    public string BlacklistSearchKeyword { get => _blacklistSearchKeyword; set { if (SetProperty(ref _blacklistSearchKeyword, value)) ApplyBlacklistFilter(); } }

    // KPIs
    private int _totalCustomers;
    public int TotalCustomers { get => _totalCustomers; set => SetProperty(ref _totalCustomers, value); }
    private int _activeTicketsCount;
    public int ActiveTicketsCount { get => _activeTicketsCount; set => SetProperty(ref _activeTicketsCount, value); }
    private int _expiringSoonCount;
    public int ExpiringSoonCount { get => _expiringSoonCount; set => SetProperty(ref _expiringSoonCount, value); }
    private int _blacklistedCount;
    public int BlacklistedCount { get => _blacklistedCount; set => SetProperty(ref _blacklistedCount, value); }
    private decimal _monthlyRevenueTotal;
    public decimal MonthlyRevenueTotal
    {
        get => _monthlyRevenueTotal;
        set { if (SetProperty(ref _monthlyRevenueTotal, value)) OnPropertyChanged(nameof(MonthlyRevenueTotalFormatted)); }
    }
    public string MonthlyRevenueTotalFormatted => AppText.Get("Str_Customers_RevenuePerMonth", MonthlyRevenueTotal);

    public AsyncRelayCommand RefreshCommand { get; }

    // Add Commands
    public AsyncRelayCommand AddHouseholdCommand { get; }
    public AsyncRelayCommand AddCustomerCommand { get; }
    public AsyncRelayCommand AddVehicleCommand { get; }
    public AsyncRelayCommand AddTicketCommand { get; }
    public AsyncRelayCommand AddBlacklistCommand { get; }

    // Edit/Delete/Action Commands
    public AsyncRelayCommand DeleteHouseholdCommand { get; }
    public AsyncRelayCommand DeleteCustomerCommand { get; }
    public AsyncRelayCommand DeleteTicketCommand { get; }
    public AsyncRelayCommand CancelTicketCommand { get; }
    public AsyncRelayCommand ToggleTicketCommand { get; }
    public AsyncRelayCommand RenewTicketCommand { get; }
    public AsyncRelayCommand ChangePlateCommand { get; }
    public AsyncRelayCommand DeleteRowVehicleCommand { get; }
    public AsyncRelayCommand ToggleBlacklistCommand { get; }
    public AsyncRelayCommand DeleteBlacklistCommand { get; }

    public CustomersViewModel(
        ICustomersService customersService,
        ITicketsService ticketsService,
        IVehiclesService vehiclesService,
        IDialogService dialogService,
        IPermissionService permissions)
    {
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _customersService = customersService;
        _ticketsService = ticketsService;
        _vehiclesService = vehiclesService;
        _dialogService = dialogService;

        AppText.Changed += OnLanguageChanged;
        RefreshCommand = new AsyncRelayCommand(LoadDataAsync, () => CanManage);

        AddHouseholdCommand = new AsyncRelayCommand(() => RunGuardedAsync(AddHouseholdAsync), () => CanManage);
        AddCustomerCommand = new AsyncRelayCommand(() => RunGuardedAsync(AddCustomerAsync), () => CanManage);
        AddVehicleCommand = new AsyncRelayCommand(() => RunGuardedAsync(AddVehicleAsync), () => CanManage);
        AddTicketCommand = new AsyncRelayCommand(() => RunGuardedAsync(AddTicketAsync), () => CanManage);
        AddBlacklistCommand = new AsyncRelayCommand(() => RunGuardedAsync(AddBlacklistAsync), () => CanManage);

        DeleteHouseholdCommand = new AsyncRelayCommand(() => RunGuardedAsync(DeleteHouseholdAsync), () => CanManage);
        DeleteCustomerCommand = new AsyncRelayCommand(() => RunGuardedAsync(DeleteCustomerAsync), () => CanManage);
        DeleteTicketCommand = new AsyncRelayCommand(p => RunGuardedAsync(() => DeleteTicketAsync(p)), _ => CanManage);
        CancelTicketCommand = new AsyncRelayCommand(p => RunGuardedAsync(() => CancelTicketAsync(p)), _ => CanManage);
        ToggleTicketCommand = new AsyncRelayCommand(p => RunGuardedAsync(() => ToggleTicketAsync(p)), _ => CanManage);
        RenewTicketCommand = new AsyncRelayCommand(() => RunGuardedAsync(RenewTicketAsync), () => CanManage);
        ChangePlateCommand = new AsyncRelayCommand(p => RunGuardedAsync(() => ChangePlateAsync(p)), _ => CanManage);
        DeleteRowVehicleCommand = new AsyncRelayCommand(p => RunGuardedAsync(() => DeleteRowVehicleAsync(p)), _ => CanManage);
        ToggleBlacklistCommand = new AsyncRelayCommand(p => RunGuardedAsync(() => ToggleBlacklistAsync(p)), _ => CanManage);
        DeleteBlacklistCommand = new AsyncRelayCommand(p => RunGuardedAsync(() => DeleteBlacklistAsync(p)), _ => CanManage);

        if (CanManage)
        {
            _ = LoadDataAsync();
        }
    }

    /// <summary>Quyền Parking.Configure: khoá mọi nút thêm/xoá/vé/blacklist khi thiếu quyền (service vẫn kiểm lại).</summary>
    public bool CanManage => _permissions.HasPermission(Permissions.ParkingConfigure);

    private async Task RunGuardedAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (PermissionDeniedException)
        {
            _dialogService.ShowWarning(AppText.Get("Msg_Auth_PermissionDenied"));
        }
    }

    private void OnLanguageChanged()
    {
        var (customerSort, householdSort, ticketSort) = (SelectedCustomerSortIndex, SelectedHouseholdSortIndex, SelectedTicketSortIndex);
        OnPropertyChanged(nameof(CustomerSortOptions));
        OnPropertyChanged(nameof(HouseholdSortOptions));
        OnPropertyChanged(nameof(TicketSortOptions));
        OnPropertyChanged(nameof(MonthlyRevenueTotalFormatted));
        SelectedCustomerSortIndex = customerSort;
        SelectedHouseholdSortIndex = householdSort;
        SelectedTicketSortIndex = ticketSort;
    }

    public async Task LoadDataAsync()
    {
        try
        {
            var tHouseholds = _customersService.GetHouseholdsAsync();
            var tTickets = _ticketsService.GetTicketsAsync();
            var tBlacklists = _customersService.GetBlacklistAsync();
            var tCustomers = _customersService.GetCustomersAsync();
            var tVehicles = _vehiclesService.GetVehiclesAsync();
            var tVehicleTypes = _vehiclesService.GetVehicleTypesAsync();
            var tPricingRules = _ticketsService.GetPricingRulesAsync();

            await Task.WhenAll(tHouseholds, tTickets, tBlacklists, tCustomers, tVehicles, tVehicleTypes, tPricingRules);

            var households = await tHouseholds;
            var tickets = await tTickets;
            var blacklists = await tBlacklists;
            var customers = await tCustomers;
            var vehicles = await tVehicles;
            var vehicleTypes = await tVehicleTypes;
            var pricingRules = await tPricingRules;

            _allHouseholds.Clear();
            _allHouseholds.AddRange(households);
            ApplyHouseholdFilter();

            _allTickets.Clear();
            _allTickets.AddRange(tickets);
            ApplyTicketFilter();

            _allBlacklist.Clear();
            _allBlacklist.AddRange(blacklists);
            BlacklistedCount = blacklists.Count(b => b.IsActive);
            ApplyBlacklistFilter();

            _allCustomers.Clear();

            foreach (var c in customers)
            {
                var custVehicles = vehicles.Where(v => v.OwnerCustomerId == c.CustomerId).ToList();

                if (custVehicles.Count > 0)
                {
                    foreach (var v in custVehicles)
                    {
                        var vtName = vehicleTypes.FirstOrDefault(vt => vt.VehicleTypeId == v.VehicleTypeId)?.TypeName ?? "";
                        var mt = c.MonthlyTickets?.FirstOrDefault(x => x.VehicleId == v.VehicleId && x.Status == MonthlyTicketStatus.Active)
                                 ?? c.MonthlyTickets?.FirstOrDefault(x => x.VehicleId == v.VehicleId);

                        _allCustomers.Add(new CustomerItemViewModel
                        {
                            CustomerId = c.CustomerId,
                            VehicleId = v.VehicleId,
                            FullName = c.FullName,
                            PhoneNumber = c.PhoneNumber,
                            Email = c.Email,
                            IdentityCard = c.IdentityCard,
                            DefaultLicensePlate = v.LicensePlate,
                            Type = c.Type,
                            VehicleTypeName = vtName,
                            MonthlyTicketCode = mt?.TicketCode ?? "",
                            TicketExpiry = mt?.EndDate,
                            ApartmentCode = c.Household?.ApartmentCode ?? "",
                            CreatedAt = c.CreatedAt
                        });
                    }
                }
                else
                {
                    var mt = c.MonthlyTickets?.FirstOrDefault(x => x.Status == MonthlyTicketStatus.Active)
                             ?? c.MonthlyTickets?.FirstOrDefault();

                    _allCustomers.Add(new CustomerItemViewModel
                    {
                        CustomerId = c.CustomerId,
                        VehicleId = null,
                        FullName = c.FullName,
                        PhoneNumber = c.PhoneNumber,
                        Email = c.Email,
                        IdentityCard = c.IdentityCard,
                        DefaultLicensePlate = mt?.RegisteredLicensePlate ?? AppText.Get("Str_Customers_NoPlate"),
                        Type = c.Type,
                        VehicleTypeName = "",
                        MonthlyTicketCode = mt?.TicketCode ?? "",
                        TicketExpiry = mt?.EndDate,
                        ApartmentCode = c.Household?.ApartmentCode ?? "",
                        CreatedAt = c.CreatedAt
                    });
                }
            }

            var now = DateTime.UtcNow;
            TotalCustomers = customers.Count;
            ActiveTicketsCount = tickets.Count(t => t.Status == MonthlyTicketStatus.Active);
            // Chuẩn hóa cảnh báo sắp hết hạn: Còn hiệu lực và trong vòng 15 ngày tới
            ExpiringSoonCount = tickets.Count(t => t.Status == MonthlyTicketStatus.Active && t.EndDate > now && t.EndDate <= now.AddDays(15));

            decimal totalMonthlyRev = 0;
            foreach (var t in tickets.Where(x => x.Status == MonthlyTicketStatus.Active))
            {
                var rule = pricingRules.FirstOrDefault(r => r.VehicleTypeId == t.VehicleTypeId);
                if (t.MonthlyPrice > 0 && t.DurationMonths > 0)
                {
                    totalMonthlyRev += (t.MonthlyPrice / t.DurationMonths);
                }
                else if (rule != null)
                {
                    totalMonthlyRev += rule.Monthly1Price;
                }
                else
                {
                    totalMonthlyRev += 100000m;
                }
            }
            MonthlyRevenueTotal = totalMonthlyRev;

            ApplyCustomerFilter();
        }
        catch (PermissionDeniedException)
        {
            _dialogService.ShowWarning(AppText.Get("Msg_Auth_PermissionDenied"));
        }
        catch (Exception ex)
        {
            _dialogService.ShowError(AppText.Get("Msg_Common_LoadError", ex.Message));
        }
    }

    private static string GetSortableVietnameseName(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return string.Empty;
        var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 1) return fullName;
        return parts[^1] + " " + string.Join(" ", parts[..^1]);
    }

    public void ApplyFilter()
    {
        ApplyCustomerFilter();
    }

    private void ApplyCustomerFilter()
    {
        var kw = SearchKeyword?.Trim().ToUpperInvariant() ?? string.Empty;
        var query = _allCustomers.AsEnumerable();

        if (!string.IsNullOrEmpty(kw))
        {
            query = query.Where(c =>
                c.FullName.ToUpperInvariant().Contains(kw) ||
                c.PhoneNumber.Contains(kw) ||
                c.DefaultLicensePlate.ToUpperInvariant().Contains(kw) ||
                c.MonthlyTicketCode.ToUpperInvariant().Contains(kw) ||
                c.ApartmentCode.ToUpperInvariant().Contains(kw));
        }

        query = SelectedCustomerSortIndex switch
        {
            1 => query.OrderBy(x => GetSortableVietnameseName(x.FullName), StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.FullName),
            _ => query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.CustomerId)
        };

        FilteredCustomers.Clear();
        foreach (var c in query)
        {
            FilteredCustomers.Add(c);
        }
    }

    private void ApplyHouseholdFilter()
    {
        var kw = HouseholdSearchKeyword?.Trim().ToUpperInvariant() ?? string.Empty;
        var query = _allHouseholds.AsEnumerable();

        if (!string.IsNullOrEmpty(kw))
        {
            query = query.Where(h =>
                (h.ApartmentCode != null && h.ApartmentCode.ToUpperInvariant().Contains(kw)) ||
                (h.Notes != null && h.Notes.ToUpperInvariant().Contains(kw)));
        }

        query = SelectedHouseholdSortIndex switch
        {
            1 => query.OrderBy(x => x.ApartmentCode, StringComparer.CurrentCultureIgnoreCase),
            _ => query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.HouseholdId)
        };

        Households.Clear();
        foreach (var h in query)
        {
            Households.Add(h);
        }
    }

    private void ApplyTicketFilter()
    {
        var kw = TicketSearchKeyword?.Trim().ToUpperInvariant() ?? string.Empty;
        var query = _allTickets.AsEnumerable();

        if (OnlyExpiringSoonTickets)
        {
            var now = DateTime.UtcNow;
            var threshold = now.AddDays(15);
            query = query.Where(t => t.Status == MonthlyTicketStatus.Active && t.EndDate > now && t.EndDate <= threshold);
        }

        if (!string.IsNullOrEmpty(kw))
        {
            query = query.Where(t =>
                (t.TicketCode != null && t.TicketCode.ToUpperInvariant().Contains(kw)) ||
                (t.RegisteredLicensePlate != null && t.RegisteredLicensePlate.ToUpperInvariant().Contains(kw)) ||
                (t.Customer != null && t.Customer.FullName.ToUpperInvariant().Contains(kw)) ||
                (t.Customer != null && t.Customer.PhoneNumber.Contains(kw)));
        }

        query = SelectedTicketSortIndex switch
        {
            1 => query.OrderBy(x => GetSortableVietnameseName(x.Customer?.FullName ?? string.Empty), StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.CreatedAt),
            2 => query.OrderBy(x => x.EndDate),
            _ => query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.TicketId)
        };

        Tickets.Clear();
        foreach (var t in query)
        {
            Tickets.Add(t);
        }
    }

    private void ApplyBlacklistFilter()
    {
        var kw = BlacklistSearchKeyword?.Trim().ToUpperInvariant() ?? string.Empty;
        var query = _allBlacklist.AsEnumerable();

        if (!string.IsNullOrEmpty(kw))
        {
            query = query.Where(b =>
                (b.LicensePlate != null && b.LicensePlate.ToUpperInvariant().Contains(kw)) ||
                (b.Reason != null && b.Reason.ToUpperInvariant().Contains(kw)) ||
                (b.Notes != null && b.Notes.ToUpperInvariant().Contains(kw)) ||
                (b.Customer != null && b.Customer.FullName.ToUpperInvariant().Contains(kw)));
        }

        query = query.OrderByDescending(b => b.CreatedAt);

        BlacklistVehicles.Clear();
        foreach (var b in query)
        {
            BlacklistVehicles.Add(b);
        }
    }

    private async Task AddHouseholdAsync()
    {
        var vm = new HouseholdDialogViewModel();
        var dlg = new HouseholdDialog(vm);
        if (dlg.ShowDialog() == true)
        {
            var req = vm.ToRequest();
            var res = await _customersService.AddHouseholdAsync(req);
            if (res.Success)
            {
                await LoadDataAsync();
                _dialogService.ShowSuccess(AppText.Get("Msg_Customers_HouseholdAdded"));
            }
            else
            {
                _dialogService.ShowError(res.Message);
            }
        }
    }

    private async Task AddCustomerAsync()
    {
        try
        {
            var hhs = await _customersService.GetHouseholdsAsync();
            var vehicleTypes = await _vehiclesService.GetVehicleTypesAsync();

            var vm = new CustomerDialogViewModel(hhs, vehicleTypes);
            var dlg = new CustomerDialog(vm);
            if (dlg.ShowDialog() == true)
            {
                var req = vm.ToRequest();
                var res = await _customersService.AddCustomerAsync(req);
                if (res.Success)
                {
                    await LoadDataAsync();
                    _dialogService.ShowSuccess(AppText.Get("Msg_Customers_CustomerAdded"));
                }
                else
                {
                    _dialogService.ShowError(res.Message);
                }
            }
        }
        catch (Exception ex)
        {
            _dialogService.ShowError(AppText.Get("Msg_Common_ErrorDetail", ex.Message));
        }
    }

    private async Task AddVehicleAsync()
    {
        try
        {
            var customers = await _customersService.GetCustomersAsync();
            var vehicleTypes = await _vehiclesService.GetVehicleTypesAsync();
            int? preselectedId = SelectedCustomer?.CustomerId;

            var vm = new VehicleDialogViewModel(customers, vehicleTypes, preselectedId);
            var dlg = new VehicleDialog(vm);
            if (dlg.ShowDialog() == true)
            {
                var req = vm.ToRequest();
                var res = await _vehiclesService.AddVehicleAsync(req);
                if (res.Success)
                {
                    await LoadDataAsync();
                    _dialogService.ShowSuccess(AppText.Get("Msg_Customers_VehicleAdded"));
                }
                else
                {
                    _dialogService.ShowError(res.Message);
                }
            }
        }
        catch (Exception ex)
        {
            _dialogService.ShowError(AppText.Get("Msg_Common_ErrorDetail", ex.Message));
        }
    }

    private async Task AddTicketAsync()
    {
        try
        {
            var customers = await _customersService.GetCustomersAsync();
            var pricingRules = await _ticketsService.GetPricingRulesAsync();
            var vehicles = await _vehiclesService.GetVehiclesAsync();

            int? preselectedId = SelectedCustomer?.CustomerId ?? SelectedTicket?.CustomerId;
            var vm = new IssueTicketDialogViewModel(customers, vehicles, pricingRules, preselectedId);
            var dlg = new IssueTicketDialog(vm);

            if (dlg.ShowDialog() == true)
            {
                var req = vm.ToRequest();
                var fee = vm.EstimatedFeePreview;
                var payMethod = req.PaymentMethod;

                // Xử lý xác nhận thanh toán trước khi gọi Service
                if (payMethod == PaymentMethod.Cash)
                {
                    var confirmMsg = AppText.Get("Msg_Customers_ConfirmCashNew", vm.SelectedCustomer?.FullName ?? "N/A", vm.SelectedVehicle?.LicensePlate, fee);
                    if (!_dialogService.ShowYesNo(confirmMsg, AppText.Get("Str_Customers_ConfirmCashTitle")))
                    {
                        _dialogService.ShowWarning(AppText.Get("Msg_Customers_CashNotCollected"));
                        return;
                    }
                }
                else if (payMethod == PaymentMethod.VietQR)
                {
                    var qrMsg = AppText.Get("Msg_Customers_ConfirmQrNew", fee, vm.SelectedVehicle?.LicensePlate);
                    if (!_dialogService.ShowYesNo(qrMsg, AppText.Get("Str_Customers_ConfirmQrTitle")))
                    {
                        _dialogService.ShowWarning(AppText.Get("Msg_Customers_QrNotDoneNew"));
                        return;
                    }
                }
                else if (payMethod == PaymentMethod.Card)
                {
                    var cardMsg = AppText.Get("Msg_Customers_ConfirmCardNew", fee);
                    if (!_dialogService.ShowYesNo(cardMsg, AppText.Get("Str_Customers_ConfirmCardTitle")))
                    {
                        _dialogService.ShowWarning(AppText.Get("Msg_Customers_CardNotDoneNew"));
                        return;
                    }
                }

                var res = await _ticketsService.IssueTicketAsync(req);
                if (res.Success)
                {
                    await LoadDataAsync();
                    _dialogService.ShowSuccess(AppText.Get("Msg_Customers_PassRegistered", fee, payMethod));
                }
                else
                {
                    _dialogService.ShowError(res.Message);
                }
            }
        }
        catch (Exception ex)
        {
            _dialogService.ShowError(AppText.Get("Msg_Common_ErrorDetail", ex.Message));
        }
    }

    private async Task DeleteHouseholdAsync()
    {
        if (SelectedHousehold == null) return;
        if (!_dialogService.ShowYesNo(AppText.Get("Msg_Customers_ConfirmDeleteHousehold", SelectedHousehold.ApartmentCode), AppText.Get("Str_Customers_DeleteHouseholdTitle"))) return;

        var res = await _customersService.DeleteHouseholdAsync(SelectedHousehold.HouseholdId);
        if (res.Success)
        {
            await LoadDataAsync();
            _dialogService.ShowSuccess(AppText.Get("Msg_Customers_HouseholdDeleted"));
        }
        else
        {
            _dialogService.ShowError(res.Message);
        }
    }

    private async Task DeleteCustomerAsync()
    {
        if (SelectedCustomer == null) return;
        if (!_dialogService.ShowYesNo(AppText.Get("Msg_Customers_ConfirmDeleteCustomer", SelectedCustomer.FullName), AppText.Get("Str_Customers_DeleteCustomerTitle"))) return;

        var res = await _customersService.DeleteCustomerAsync(SelectedCustomer.CustomerId);
        if (res.Success)
        {
            await LoadDataAsync();
            _dialogService.ShowSuccess(AppText.Get("Msg_Customers_CustomerDeleted"));
        }
        else
        {
            _dialogService.ShowError(res.Message);
        }
    }

    private async Task DeleteRowVehicleAsync(object? parameter)
    {
        var item = parameter as CustomerItemViewModel ?? SelectedCustomer;
        if (item == null) return;

        if (item.VehicleId.HasValue && item.VehicleId.Value > 0)
        {
            var msg = AppText.Get("Msg_Customers_ConfirmDeleteVehicle", item.DefaultLicensePlate, item.FullName);
            if (!_dialogService.ShowYesNo(msg, AppText.Get("Str_Customers_DeleteVehicleTitle"))) return;

            var res = await _vehiclesService.DeleteVehicleAsync(item.VehicleId.Value);
            if (res.Success)
            {
                await LoadDataAsync();
                _dialogService.ShowSuccess(AppText.Get("Msg_Customers_VehicleDeleted", item.DefaultLicensePlate));
            }
            else
            {
                _dialogService.ShowError(res.Message);
            }
        }
        else
        {
            var msg = AppText.Get("Msg_Customers_ConfirmDeleteCustomerNoVehicle", item.FullName);
            if (!_dialogService.ShowYesNo(msg, AppText.Get("Str_Customers_DeleteCustomerTitle"))) return;

            var res = await _customersService.DeleteCustomerAsync(item.CustomerId);
            if (res.Success)
            {
                await LoadDataAsync();
                _dialogService.ShowSuccess(AppText.Get("Msg_Customers_CustomerDeletedNamed", item.FullName));
            }
            else
            {
                _dialogService.ShowError(res.Message);
            }
        }
    }

    private async Task DeleteTicketAsync(object? parameter)
    {
        var item = parameter as MonthlyTicket ?? SelectedTicket;
        if (item == null) return;

        var msg = AppText.Get("Msg_Customers_CancelOrDeletePass", item.TicketCode, item.RegisteredLicensePlate);
        if (!_dialogService.ShowYesNo(msg, AppText.Get("Str_Customers_CancelOrDeleteTitle"))) return;

        var res = await _ticketsService.CancelTicketAsync(item.TicketId, "Hủy vé theo yêu cầu");
        if (res.Success)
        {
            await LoadDataAsync();
            _dialogService.ShowSuccess(AppText.Get("Msg_Customers_PassCancelledKeepHistory", item.TicketCode));
        }
        else
        {
            _dialogService.ShowError(res.Message);
        }
    }

    private async Task CancelTicketAsync(object? parameter)
    {
        var item = parameter as MonthlyTicket ?? SelectedTicket;
        if (item == null) return;

        if (!_dialogService.ShowYesNo(AppText.Get("Msg_Customers_ConfirmCancelPass", item.TicketCode), AppText.Get("Str_Customers_CancelPassTitle"))) return;

        var res = await _ticketsService.CancelTicketAsync(item.TicketId, "Hủy vé theo yêu cầu");
        if (res.Success)
        {
            await LoadDataAsync();
            _dialogService.ShowSuccess(AppText.Get("Msg_Customers_PassCancelled", item.TicketCode));
        }
        else
        {
            _dialogService.ShowError(res.Message);
        }
    }

    private async Task ToggleTicketAsync(object? parameter)
    {
        var item = parameter as MonthlyTicket ?? SelectedTicket;
        if (item == null) return;

        var isLocked = item.Status == MonthlyTicketStatus.Suspended || item.Status == MonthlyTicketStatus.Cancelled;
        var (confirmKey, titleKey, doneKey) = isLocked
            ? ("Msg_Customers_ConfirmUnlockPass", "Str_Customers_UnlockPassTitle", "Msg_Customers_PassUnlocked")
            : ("Msg_Customers_ConfirmLockPass", "Str_Customers_LockPassTitle", "Msg_Customers_PassLocked");

        if (!_dialogService.ShowYesNo(AppText.Get(confirmKey, item.TicketCode), AppText.Get(titleKey))) return;

        var reason = isLocked ? "Mở khóa thẻ vé tháng" : "Tạm khóa thẻ vé tháng";
        var res = await _ticketsService.ToggleLockTicketAsync(item.TicketId, reason);
        if (res.Success)
        {
            await LoadDataAsync();
            _dialogService.ShowSuccess(AppText.Get(doneKey));
        }
        else
        {
            _dialogService.ShowError(res.Message);
        }
    }

    private async Task RenewTicketAsync()
    {
        if (SelectedTicket == null) return;

        try
        {
            var pricingRules = await _ticketsService.GetPricingRulesAsync();
            var rule = pricingRules.FirstOrDefault(r => r.VehicleTypeId == SelectedTicket.VehicleTypeId);

            var vm = new RenewTicketDialogViewModel(SelectedTicket, rule);
            var dlg = new RenewTicketDialog(vm);

            if (dlg.ShowDialog() == true)
            {
                var req = vm.ToRequest();
                var fee = vm.EstimatedFee;
                var payMethod = req.PaymentMethod;

                if (payMethod == PaymentMethod.Cash)
                {
                    var confirmMsg = AppText.Get("Msg_Customers_ConfirmCashRenew", SelectedTicket.Customer?.FullName ?? "N/A", SelectedTicket.RegisteredLicensePlate, fee);
                    if (!_dialogService.ShowYesNo(confirmMsg, AppText.Get("Str_Customers_ConfirmCashRenewTitle")))
                    {
                        _dialogService.ShowWarning(AppText.Get("Msg_Customers_RenewCashNotCollected"));
                        return;
                    }
                }
                else if (payMethod == PaymentMethod.VietQR)
                {
                    var qrMsg = AppText.Get("Msg_Customers_ConfirmQrRenew", fee, SelectedTicket.TicketCode);
                    if (!_dialogService.ShowYesNo(qrMsg, AppText.Get("Str_Customers_ConfirmQrTitle")))
                    {
                        _dialogService.ShowWarning(AppText.Get("Msg_Customers_QrNotDoneRenew"));
                        return;
                    }
                }
                else if (payMethod == PaymentMethod.Card)
                {
                    var cardMsg = AppText.Get("Msg_Customers_ConfirmCardRenew", fee);
                    if (!_dialogService.ShowYesNo(cardMsg, AppText.Get("Str_Customers_ConfirmCardTitle")))
                    {
                        _dialogService.ShowWarning(AppText.Get("Msg_Customers_CardNotDoneRenew"));
                        return;
                    }
                }

                var res = await _ticketsService.RenewTicketAsync(req);
                if (res.Success)
                {
                    await LoadDataAsync();
                    _dialogService.ShowSuccess(AppText.Get("Msg_Customers_PassRenewed", req.AddedMonths, fee, payMethod));
                }
                else
                {
                    _dialogService.ShowError(res.Message);
                }
            }
        }
        catch (Exception ex)
        {
            _dialogService.ShowError(AppText.Get("Msg_Common_ErrorDetail", ex.Message));
        }
    }

    private async Task ChangePlateAsync(object? parameter)
    {
        var ticket = parameter as MonthlyTicket ?? SelectedTicket;
        if (ticket == null) return;

        if (ticket.Status == MonthlyTicketStatus.Cancelled)
        {
            _dialogService.ShowWarning("Vé này đã bị hủy, không thể đổi biển số.");
            return;
        }

        try
        {
            var custVehicles = await _vehiclesService.GetVehiclesByCustomerAsync(ticket.CustomerId);
            var available = custVehicles
                .Where(v => v.VehicleId != ticket.VehicleId && v.IsActive && v.VehicleTypeId == ticket.VehicleTypeId)
                .ToList();

            if (available.Count == 0)
            {
                _dialogService.ShowWarning("Khách hàng này không có phương tiện khác cùng loại phương tiện để chuyển vé sang. Vui lòng thêm phương tiện mới trước khi đổi biển.");
                return;
            }

            var vm = new ChangePlateDialogViewModel(ticket, available);
            var dlg = new ChangePlateDialog(vm);

            if (dlg.ShowDialog() == true)
            {
                var req = vm.ToRequest();
                var res = await _ticketsService.ChangePlateAsync(req);
                if (res.Success)
                {
                    await LoadDataAsync();
                    _dialogService.ShowSuccess("Đổi biển số cho vé tháng thành công.");
                }
                else
                {
                    _dialogService.ShowError(res.Message);
                }
            }
        }
        catch (Exception ex)
        {
            _dialogService.ShowError(AppText.Get("Msg_Common_ErrorDetail", ex.Message));
        }
    }

    public async Task LoadTicketHistoriesAsync(MonthlyTicket? ticket)
    {
        SelectedTicketHistories.Clear();
        if (ticket == null) return;

        try
        {
            IsLoadingHistories = true;
            var histories = await _ticketsService.GetTicketHistoriesAsync(ticket.TicketId);
            foreach (var h in histories)
            {
                SelectedTicketHistories.Add(h);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LoadTicketHistoriesAsync Error]: {ex}");
        }
        finally
        {
            IsLoadingHistories = false;
        }
    }

    private async Task AddBlacklistAsync()
    {
        try
        {
            var customers = await _customersService.GetCustomersAsync();
            string? prefillPlate = SelectedCustomer?.DefaultLicensePlate;
            if (!SmartPS.Services.GateControl.LicensePlateNormalizer.IsValid(prefillPlate)) prefillPlate = null;

            var dlg = new AddBlacklistDialog(customers.ToList(), prefillPlate);
            if (dlg.ShowDialog() == true)
            {
                var cleanPlate = _vehiclesService.NormalizePlate(dlg.FinalLicensePlate);
                if (string.IsNullOrEmpty(cleanPlate))
                {
                    _dialogService.ShowError(AppText.Get("Msg_Common_InvalidPlate"));
                    return;
                }
                if (cleanPlate.Length > 20)
                {
                    _dialogService.ShowError(AppText.Get("Msg_Common_PlateTooLong"));
                    return;
                }

                var res = await _customersService.AddBlacklistAsync(cleanPlate, dlg.FinalReason, dlg.FinalNotes, dlg.FinalCustomerId);
                if (res.Success)
                {
                    await LoadDataAsync();
                    _dialogService.ShowSuccess(AppText.Get("Msg_Customers_BlacklistAdded", cleanPlate));
                }
                else
                {
                    _dialogService.ShowWarning(res.Message);
                }
            }
        }
        catch (Exception ex)
        {
            _dialogService.ShowError(AppText.Get("Msg_Common_ErrorDetail", ex.Message));
        }
    }

    private async Task ToggleBlacklistAsync(object? parameter)
    {
        var item = parameter as BlacklistedVehicle ?? SelectedBlacklist;
        if (item == null) return;

        var confirmKey = item.IsActive ? "Msg_Customers_ConfirmUnban" : "Msg_Customers_ConfirmReban";
        if (!_dialogService.ShowYesNo(AppText.Get(confirmKey, item.LicensePlate), AppText.Get("Str_Dialog_Title_Confirm"))) return;

        var res = await _customersService.ToggleBlacklistAsync(item.BlacklistId);
        if (res.Success)
        {
            await LoadDataAsync();
            _dialogService.ShowSuccess(AppText.Get(!item.IsActive ? "Msg_Customers_VehicleBanned" : "Msg_Customers_VehicleUnbanned", item.LicensePlate));
        }
        else
        {
            _dialogService.ShowError(res.Message);
        }
    }

    private async Task DeleteBlacklistAsync(object? parameter)
    {
        var item = parameter as BlacklistedVehicle ?? SelectedBlacklist;
        if (item == null) return;

        if (!_dialogService.ShowYesNo(AppText.Get("Msg_Customers_ConfirmRemoveBlacklist", item.LicensePlate), AppText.Get("Str_Customers_RemoveBlacklistTitle"))) return;

        var res = await _customersService.DeleteBlacklistAsync(item.BlacklistId);
        if (res.Success)
        {
            await LoadDataAsync();
            _dialogService.ShowSuccess(AppText.Get("Msg_Customers_BlacklistRemoved", item.LicensePlate));
        }
        else
        {
            _dialogService.ShowError(res.Message);
        }
    }
}
