using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using SmartPS.Models.Parking;
using SmartPS.Data;
using Microsoft.EntityFrameworkCore;
using SmartPS.Services.Dialog;
using SmartPS.Views.Customers;
using System.Windows;
using SmartPS.Services.Localization;

namespace SmartPS.ViewModels.Customers;

public class CustomersViewModel : ViewModelBase
{
    private readonly IDbContextFactory<SmartPsDbContext> _dbFactory;
    private readonly IDialogService _dialogService;
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
    public MonthlyTicket? SelectedTicket { get => _selectedTicket; set => SetProperty(ref _selectedTicket, value); }
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
    public AsyncRelayCommand DeleteRowVehicleCommand { get; }
    public AsyncRelayCommand ToggleBlacklistCommand { get; }
    public AsyncRelayCommand DeleteBlacklistCommand { get; }

    public CustomersViewModel(IDbContextFactory<SmartPsDbContext> dbFactory, IDialogService dialogService)
    {
        _dbFactory = dbFactory;
        _dialogService = dialogService;
        AppText.Changed += OnLanguageChanged;
        RefreshCommand = new AsyncRelayCommand(LoadDataAsync);
        
        AddHouseholdCommand = new AsyncRelayCommand(AddHouseholdAsync);
        AddCustomerCommand = new AsyncRelayCommand(AddCustomerAsync);
        AddVehicleCommand = new AsyncRelayCommand(AddVehicleAsync);
        AddTicketCommand = new AsyncRelayCommand(AddTicketAsync);
        AddBlacklistCommand = new AsyncRelayCommand(AddBlacklistAsync);

        DeleteHouseholdCommand = new AsyncRelayCommand(DeleteHouseholdAsync);
        DeleteCustomerCommand = new AsyncRelayCommand(DeleteCustomerAsync);
        DeleteTicketCommand = new AsyncRelayCommand(DeleteTicketAsync);
        CancelTicketCommand = new AsyncRelayCommand(CancelTicketAsync);
        ToggleTicketCommand = new AsyncRelayCommand(ToggleTicketAsync);
        RenewTicketCommand = new AsyncRelayCommand(RenewTicketAsync);
        DeleteRowVehicleCommand = new AsyncRelayCommand(DeleteRowVehicleAsync);
        ToggleBlacklistCommand = new AsyncRelayCommand(ToggleBlacklistAsync);
        DeleteBlacklistCommand = new AsyncRelayCommand(DeleteBlacklistAsync);
        
        _ = LoadDataAsync();
    }

    /// <summary>Đổi ngôn ngữ: dựng lại danh sách sắp xếp nhưng giữ lựa chọn đang dùng.</summary>
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
            await using var db = await _dbFactory.CreateDbContextAsync();

            // Tự động chuẩn hóa dữ liệu biển số cũ sang chuẩn hoa không dấu phân cách nếu còn sót
            var dirtyVehicles = await db.Vehicles.Where(v => v.LicensePlate.Contains("-") || v.LicensePlate.Contains(".") || v.LicensePlate.Contains(" ")).ToListAsync();
            foreach (var v in dirtyVehicles) { v.LicensePlate = System.Text.RegularExpressions.Regex.Replace(v.LicensePlate, @"[^a-zA-Z0-9]", "").ToUpperInvariant(); }
            var dirtyTickets = await db.MonthlyTickets.Where(t => t.RegisteredLicensePlate.Contains("-") || t.RegisteredLicensePlate.Contains(".") || t.RegisteredLicensePlate.Contains(" ")).ToListAsync();
            foreach (var t in dirtyTickets) { t.RegisteredLicensePlate = System.Text.RegularExpressions.Regex.Replace(t.RegisteredLicensePlate, @"[^a-zA-Z0-9]", "").ToUpperInvariant(); }
            var dirtyBlacklists = await db.BlacklistedVehicles.Where(b => b.LicensePlate.Contains("-") || b.LicensePlate.Contains(".") || b.LicensePlate.Contains(" ")).ToListAsync();
            foreach (var b in dirtyBlacklists) { b.LicensePlate = System.Text.RegularExpressions.Regex.Replace(b.LicensePlate, @"[^a-zA-Z0-9]", "").ToUpperInvariant(); }
            if (dirtyVehicles.Count > 0 || dirtyTickets.Count > 0 || dirtyBlacklists.Count > 0) { await db.SaveChangesAsync(); }

            var households = await db.Households.ToListAsync();
            _allHouseholds.Clear();
            _allHouseholds.AddRange(households);
            ApplyHouseholdFilter();
            
            var tickets = await db.MonthlyTickets.Include(t => t.Customer).Include(t => t.Vehicle).ToListAsync();
            _allTickets.Clear();
            _allTickets.AddRange(tickets);
            ApplyTicketFilter();

            var blacklists = await db.BlacklistedVehicles.Include(b => b.Customer).OrderByDescending(b => b.CreatedAt).ToListAsync();
            _allBlacklist.Clear();
            _allBlacklist.AddRange(blacklists);
            BlacklistedCount = blacklists.Count(b => b.IsActive);
            ApplyBlacklistFilter();
            
            var customers = await db.Customers.Include(c => c.Household).Include(c => c.MonthlyTickets).ToListAsync();
            var vehicles = await db.Vehicles.Where(v => v.IsActive).ToListAsync();
            var vehicleTypes = await db.VehicleTypes.ToListAsync();
            var rules = await db.PricingRules.ToListAsync();
            _allCustomers.Clear();
            
            foreach (var c in customers)
            {
                var custVehicles = vehicles.Where(x => x.OwnerCustomerId == c.CustomerId).ToList();
                if (custVehicles.Count > 0)
                {
                    foreach (var v in custVehicles)
                    {
                        var vtName = vehicleTypes.FirstOrDefault(vt => vt.VehicleTypeId == v.VehicleTypeId)?.TypeName ?? "";
                        var mt = c.MonthlyTickets.FirstOrDefault(x => x.VehicleId == v.VehicleId && x.Status == MonthlyTicketStatus.Active)
                                 ?? c.MonthlyTickets.FirstOrDefault(x => x.VehicleId == v.VehicleId);

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
                    var mt = c.MonthlyTickets.FirstOrDefault(x => x.Status == MonthlyTicketStatus.Active)
                             ?? c.MonthlyTickets.FirstOrDefault();

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
            
            TotalCustomers = customers.Count;
            ActiveTicketsCount = tickets.Count(t => t.Status == MonthlyTicketStatus.Active);
            ExpiringSoonCount = tickets.Count(t => t.Status == MonthlyTicketStatus.Active && (t.EndDate - DateTime.UtcNow).TotalDays <= 15);
            
            decimal totalMonthlyRev = 0;
            foreach (var t in tickets.Where(x => x.Status == MonthlyTicketStatus.Active))
            {
                var rule = rules.FirstOrDefault(r => r.VehicleTypeId == t.VehicleTypeId);
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
        catch(Exception ex)
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
        var dlg = new HouseholdDialog();
        if (dlg.ShowDialog() == true)
        {
            try {
                await using var db = await _dbFactory.CreateDbContextAsync();
                if (await db.Households.AnyAsync(h => h.ApartmentCode == dlg.ApartmentCode))
                {
                    _dialogService.ShowError(AppText.Get("Msg_Customers_ApartmentExists"));
                    return;
                }
                db.Households.Add(new Household { ApartmentCode = dlg.ApartmentCode, MaxVehicles = dlg.MaxVehicles, Notes = dlg.Notes, CreatedAt = DateTime.UtcNow });
                await db.SaveChangesAsync();
                await LoadDataAsync();
                _dialogService.ShowSuccess(AppText.Get("Msg_Customers_HouseholdAdded"));
            } catch (Exception ex) {
                _dialogService.ShowError(AppText.Get("Msg_Common_SaveError", ex.Message));
            }
        }
    }

    private async Task AddCustomerAsync()
    {
        try {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var hhs = await db.Households.ToListAsync();
            var vehicleTypes = await db.VehicleTypes.OrderBy(vt => vt.VehicleTypeId).ToListAsync();
            var dlg = new CustomerDialog(hhs, vehicleTypes);
            if (dlg.ShowDialog() == true)
            {
                if (!string.IsNullOrEmpty(dlg.FinalLicensePlate) && dlg.FinalSelectedHouseholdId.HasValue)
                {
                    var h = await db.Households.FirstOrDefaultAsync(x => x.HouseholdId == dlg.FinalSelectedHouseholdId.Value);
                    var settings = await db.ParkingSettings.FirstOrDefaultAsync();
                    int max = h?.MaxVehicles ?? settings?.DefaultMaxVehiclesPerHousehold ?? 2;
                    int current = await db.Vehicles.CountAsync(v => v.OwnerCustomer != null && v.OwnerCustomer.HouseholdId == dlg.FinalSelectedHouseholdId.Value && v.IsActive);
                    if (current >= max)
                    {
                        _dialogService.ShowError(AppText.Get("Msg_Customers_HouseholdLimitAdd", max));
                        return;
                    }
                }
                
                var cust = new Customer { FullName = dlg.FinalFullName, PhoneNumber = dlg.FinalPhoneNumber, HouseholdId = dlg.FinalSelectedHouseholdId, Type = dlg.FinalSelectedHouseholdId.HasValue ? CustomerType.Resident : CustomerType.External, CreatedAt = DateTime.UtcNow };
                db.Customers.Add(cust);
                await db.SaveChangesAsync();
                
                if (!string.IsNullOrEmpty(dlg.FinalLicensePlate))
                {
                    var normPlate = System.Text.RegularExpressions.Regex.Replace(dlg.FinalLicensePlate.Trim(), @"[^a-zA-Z0-9]", "").ToUpperInvariant();
                    db.Vehicles.Add(new Vehicle { LicensePlate = normPlate, VehicleTypeId = dlg.FinalSelectedVehicleType, OwnerCustomerId = cust.CustomerId, CreatedAt = DateTime.UtcNow, IsActive = true });
                    await db.SaveChangesAsync();
                }

                await LoadDataAsync();
                _dialogService.ShowSuccess(AppText.Get("Msg_Customers_CustomerAdded"));
            }
        } catch (Exception ex) {
            _dialogService.ShowError(AppText.Get("Msg_Common_ErrorDetail", ex.Message));
        }
    }

    private async Task AddVehicleAsync()
    {
        try {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var customers = await db.Customers.OrderBy(c => c.FullName).ToListAsync();
            var vehicleTypes = await db.VehicleTypes.OrderBy(vt => vt.VehicleTypeId).ToListAsync();
            int? preselectedId = SelectedCustomer?.CustomerId;
            var dlg = new VehicleDialog(customers, vehicleTypes, preselectedId);
            if (dlg.ShowDialog() == true)
            {
                var cid = dlg.FinalSelectedCustomerId;
                var customer = await db.Customers.FirstOrDefaultAsync(c => c.CustomerId == cid);
                if (customer != null && customer.HouseholdId.HasValue)
                {
                    var h = await db.Households.FirstOrDefaultAsync(x => x.HouseholdId == customer.HouseholdId.Value);
                    var settings = await db.ParkingSettings.FirstOrDefaultAsync();
                    int max = h?.MaxVehicles ?? settings?.DefaultMaxVehiclesPerHousehold ?? 2;
                    int current = await db.Vehicles.CountAsync(v => v.OwnerCustomer != null && v.OwnerCustomer.HouseholdId == customer.HouseholdId.Value && v.IsActive);
                    if (current >= max)
                    {
                        _dialogService.ShowError(AppText.Get("Msg_Customers_HouseholdLimit", max));
                        return;
                    }
                }
                
                var normPlate = System.Text.RegularExpressions.Regex.Replace(dlg.FinalLicensePlate.Trim(), @"[^a-zA-Z0-9]", "").ToUpperInvariant();
                db.Vehicles.Add(new Vehicle { LicensePlate = normPlate, VehicleTypeId = dlg.FinalSelectedVehicleType, OwnerCustomerId = cid, CreatedAt = DateTime.UtcNow, IsActive = true });
                await db.SaveChangesAsync();
                await LoadDataAsync();
                _dialogService.ShowSuccess(AppText.Get("Msg_Customers_VehicleAdded"));
            }
        } catch (Exception ex) {
            _dialogService.ShowError(AppText.Get("Msg_Common_ErrorDetail", ex.Message));
        }
    }

    private async Task AddTicketAsync()
    {
        try {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var customers = await db.Customers.OrderBy(c => c.FullName).ToListAsync();
            var vehicles = await db.Vehicles.Where(v => v.IsActive).ToListAsync();
            var pricingRules = await db.PricingRules.ToListAsync();
            int? preselectedId = SelectedCustomer?.CustomerId ?? SelectedTicket?.CustomerId;
            var dlg = new IssueTicketDialog(customers, vehicles, pricingRules, preselectedId);
            if (dlg.ShowDialog() == true)
            {
                var vid = dlg.FinalSelectedVehicleId;
                var active = await db.MonthlyTickets.AnyAsync(t => t.VehicleId == vid && t.Status == MonthlyTicketStatus.Active && t.EndDate > DateTime.UtcNow);
                if (active)
                {
                    _dialogService.ShowError(AppText.Get("Msg_Customers_VehicleHasPass"));
                    return;
                }
                var v = await db.Vehicles.FirstOrDefaultAsync(x => x.VehicleId == vid);
                if (v == null || v.OwnerCustomerId != dlg.FinalSelectedCustomerId)
                {
                    _dialogService.ShowError(AppText.Get("Msg_Customers_VehicleNotOwned"));
                    return;
                }
                var dur = dlg.FinalDurationMonths;
                var ticketFee = dlg.FinalPrice;

                // Fallback fee calculation if not set from dialog
                if (ticketFee <= 0 && v != null)
                {
                    var rule = pricingRules.FirstOrDefault(r => r.VehicleTypeId == v.VehicleTypeId);
                    if (rule != null)
                    {
                        ticketFee = dur switch
                        {
                            1 => rule.Monthly1Price,
                            3 => rule.Monthly3Price,
                            6 => rule.Monthly6Price,
                            _ => rule.Monthly1Price * dur
                        };
                    }
                }

                var payMethod = dlg.FinalPaymentMethod;

                // Xử lý luồng thanh toán:
                if (payMethod == PaymentMethod.Cash)
                {
                    // Tiền mặt: Nhân viên xác nhận đã thu đủ tiền
                    var confirmMsg = AppText.Get("Msg_Customers_ConfirmCashNew", v?.OwnerCustomer?.FullName ?? "N/A", v?.LicensePlate, ticketFee);
                    if (!_dialogService.ShowYesNo(confirmMsg, AppText.Get("Str_Customers_ConfirmCashTitle")))
                    {
                        _dialogService.ShowWarning(AppText.Get("Msg_Customers_CashNotCollected"));
                        return;
                    }
                }
                else if (payMethod == PaymentMethod.VietQR)
                {
                    // VietQR: Hiển thị thông tin chuyển khoản và chờ nhân viên/cổng xác nhận đã thanh toán thành công
                    var qrMsg = AppText.Get("Msg_Customers_ConfirmQrNew", ticketFee, v?.LicensePlate);
                    if (!_dialogService.ShowYesNo(qrMsg, AppText.Get("Str_Customers_ConfirmQrTitle")))
                    {
                        _dialogService.ShowWarning(AppText.Get("Msg_Customers_QrNotDoneNew"));
                        return;
                    }
                }
                else if (payMethod == PaymentMethod.Card)
                {
                    // Thẻ / POS: Xác nhận quẹt thẻ thành công
                    var cardMsg = AppText.Get("Msg_Customers_ConfirmCardNew", ticketFee);
                    if (!_dialogService.ShowYesNo(cardMsg, AppText.Get("Str_Customers_ConfirmCardTitle")))
                    {
                        _dialogService.ShowWarning(AppText.Get("Msg_Customers_CardNotDoneNew"));
                        return;
                    }
                }

                var normPlate = System.Text.RegularExpressions.Regex.Replace((v?.LicensePlate ?? "").Trim(), @"[^a-zA-Z0-9]", "").ToUpperInvariant();

                var t = new MonthlyTicket
                {
                    CustomerId = dlg.FinalSelectedCustomerId,
                    VehicleId = vid,
                    RegisteredLicensePlate = normPlate,
                    VehicleTypeId = v?.VehicleTypeId ?? 2,
                    DurationMonths = dur,
                    MonthlyPrice = ticketFee,
                    StartDate = DateTime.UtcNow,
                    EndDate = DateTime.UtcNow.AddMonths(dur),
                    TicketCode = $"MT-{DateTime.UtcNow:yyyyMM}-{new Random().Next(1000, 9999)}",
                    Status = MonthlyTicketStatus.Active,
                    CreatedAt = DateTime.UtcNow
                };
                db.MonthlyTickets.Add(t);
                await db.SaveChangesAsync();
                
                var logLine = $"{{\"timestamp\":\"{DateTime.UtcNow:O}\",\"action\":\"IssueTicket\",\"details\":\"TicketCode={t.TicketCode}, Plate={t.RegisteredLicensePlate}, Fee={ticketFee}, PaymentMethod={payMethod}\"}}\n";
                await System.IO.File.AppendAllTextAsync("gate_audit_log.jsonl", logLine);

                await LoadDataAsync();
                _dialogService.ShowSuccess(AppText.Get("Msg_Customers_PassRegistered", ticketFee, payMethod));
            }
        } catch (Exception ex) {
            _dialogService.ShowError(AppText.Get("Msg_Common_ErrorDetail", ex.Message));
        }
    }

    private async Task DeleteHouseholdAsync()
    {
        if (SelectedHousehold == null) return;
        if (!_dialogService.ShowYesNo(AppText.Get("Msg_Customers_ConfirmDeleteHousehold", SelectedHousehold.ApartmentCode), AppText.Get("Str_Customers_DeleteHouseholdTitle"))) return;
        try {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var h = await db.Households.FindAsync(SelectedHousehold.HouseholdId);
            if (h != null) {
                // Set Customer to External instead of deleting them when Household is deleted
                var customers = await db.Customers.Where(c => c.HouseholdId == h.HouseholdId).ToListAsync();
                foreach(var c in customers) {
                    c.HouseholdId = null;
                    c.Type = CustomerType.External;
                }
                
                db.Households.Remove(h);
                await db.SaveChangesAsync();
                await LoadDataAsync();
                _dialogService.ShowSuccess(AppText.Get("Msg_Customers_HouseholdDeleted"));
            }
        } catch (Exception ex) {
            _dialogService.ShowError(AppText.Get("Msg_Common_DeleteDbError", ex.Message));
        }
    }

    private async Task DeleteCustomerAsync()
    {
        if (SelectedCustomer == null) return;
        if (!_dialogService.ShowYesNo(AppText.Get("Msg_Customers_ConfirmDeleteCustomer", SelectedCustomer.FullName), AppText.Get("Str_Customers_DeleteCustomerTitle"))) return;
        try {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var c = await db.Customers.FindAsync(SelectedCustomer.CustomerId);
            if (c != null) {
                // Cascade delete associated MonthlyTickets
                var tickets = await db.MonthlyTickets.Where(t => t.CustomerId == c.CustomerId).ToListAsync();
                db.MonthlyTickets.RemoveRange(tickets);
                
                // Set ParkingSessions CustomerId to null
                var sessions = await db.ParkingSessions.Where(s => s.CustomerId == c.CustomerId).ToListAsync();
                foreach(var s in sessions) s.CustomerId = null;
                
                // Cascade delete associated Vehicles
                var vehicles = await db.Vehicles.Where(v => v.OwnerCustomerId == c.CustomerId).ToListAsync();
                db.Vehicles.RemoveRange(vehicles);
                
                db.Customers.Remove(c);
                await db.SaveChangesAsync();
                await LoadDataAsync();
                _dialogService.ShowSuccess(AppText.Get("Msg_Customers_CustomerDeleted"));
            }
        } catch (Exception ex) {
            _dialogService.ShowError(AppText.Get("Msg_Common_DeleteDbError", ex.Message));
        }
    }

    private async Task DeleteRowVehicleAsync(object? parameter)
    {
        var item = parameter as CustomerItemViewModel ?? SelectedCustomer;
        if (item == null) return;

        // Nếu dòng này có phương tiện
        if (item.VehicleId.HasValue && item.VehicleId.Value > 0)
        {
            var msg = AppText.Get("Msg_Customers_ConfirmDeleteVehicle", item.DefaultLicensePlate, item.FullName);
            if (!_dialogService.ShowYesNo(msg, AppText.Get("Str_Customers_DeleteVehicleTitle"))) return;

            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync();
                var vehicle = await db.Vehicles.FindAsync(item.VehicleId.Value);
                if (vehicle != null)
                {
                    // Xóa các vé tháng của xe này
                    var tickets = await db.MonthlyTickets.Where(t => t.VehicleId == vehicle.VehicleId).ToListAsync();
                    if (tickets.Any())
                    {
                        db.MonthlyTickets.RemoveRange(tickets);
                    }

                    db.Vehicles.Remove(vehicle);
                    await db.SaveChangesAsync();
                    await LoadDataAsync();
                    _dialogService.ShowSuccess(AppText.Get("Msg_Customers_VehicleDeleted", item.DefaultLicensePlate));
                }
            }
            catch (Exception ex)
            {
                _dialogService.ShowError(AppText.Get("Msg_Customers_DeleteVehicleError", ex.Message));
            }
        }
        else
        {
            // Dòng này không có xe (chỉ có khách hàng chưa thêm xe)
            var msg = AppText.Get("Msg_Customers_ConfirmDeleteCustomerNoVehicle", item.FullName);
            if (!_dialogService.ShowYesNo(msg, AppText.Get("Str_Customers_DeleteCustomerTitle"))) return;

            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync();
                var c = await db.Customers.FindAsync(item.CustomerId);
                if (c != null)
                {
                    db.Customers.Remove(c);
                    await db.SaveChangesAsync();
                    await LoadDataAsync();
                    _dialogService.ShowSuccess(AppText.Get("Msg_Customers_CustomerDeletedNamed", item.FullName));
                }
            }
            catch (Exception ex)
            {
                _dialogService.ShowError(AppText.Get("Msg_Customers_DeleteCustomerError", ex.Message));
            }
        }
    }

    private async Task DeleteTicketAsync(object? parameter)
    {
        var item = parameter as MonthlyTicket ?? SelectedTicket;
        if (item == null) return;
        
        // Hỏi người dùng muốn HỦY VÉ (giữ lại lịch sử tài chính) hay XÓA CỨNG (chỉ khi nhập sai)
        var msg = AppText.Get("Msg_Customers_CancelOrDeletePass", item.TicketCode, item.RegisteredLicensePlate);
        
        bool cancelSoft = _dialogService.ShowYesNo(msg, AppText.Get("Str_Customers_CancelOrDeleteTitle"));
        
        try {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var t = await db.MonthlyTickets.FindAsync(item.TicketId);
            if (t != null) {
                if (cancelSoft)
                {
                    // Hủy vé (Soft delete)
                    t.Status = MonthlyTicketStatus.Cancelled;
                    await db.SaveChangesAsync();
                    await LoadDataAsync();
                    _dialogService.ShowSuccess(AppText.Get("Msg_Customers_PassCancelledKeepHistory", t.TicketCode));
                }
                else
                {
                    // Xóa cứng
                    db.MonthlyTickets.Remove(t);
                    await db.SaveChangesAsync();
                    await LoadDataAsync();
                    _dialogService.ShowSuccess(AppText.Get("Msg_Customers_PassDeletedPermanently", t.TicketCode));
                }
            }
        } catch (Exception ex) {
            _dialogService.ShowError(AppText.Get("Msg_Common_ErrorDetail", ex.Message));
        }
    }

    private async Task CancelTicketAsync(object? parameter)
    {
        var item = parameter as MonthlyTicket ?? SelectedTicket;
        if (item == null) return;

        if (!_dialogService.ShowYesNo(AppText.Get("Msg_Customers_ConfirmCancelPass", item.TicketCode), AppText.Get("Str_Customers_CancelPassTitle"))) return;

        try {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var t = await db.MonthlyTickets.FindAsync(item.TicketId);
            if (t != null) {
                t.Status = MonthlyTicketStatus.Cancelled;
                await db.SaveChangesAsync();
                await LoadDataAsync();
                _dialogService.ShowSuccess(AppText.Get("Msg_Customers_PassCancelled", t.TicketCode));
            }
        } catch (Exception ex) {
            _dialogService.ShowError(AppText.Get("Msg_Common_ErrorDetail", ex.Message));
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
        try {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var t = await db.MonthlyTickets.FindAsync(item.TicketId);
            if (t != null) {
                t.Status = isLocked ? MonthlyTicketStatus.Active : MonthlyTicketStatus.Suspended;
                await db.SaveChangesAsync();
                await LoadDataAsync();
                _dialogService.ShowSuccess(AppText.Get(doneKey));
            }
        } catch (Exception ex) {
            _dialogService.ShowError(AppText.Get("Msg_Common_ErrorDetail", ex.Message));
        }
    }

    private async Task RenewTicketAsync()
    {
        if (SelectedTicket == null) return;
        
        try {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var t = await db.MonthlyTickets.Include(x => x.Customer).Include(x => x.Vehicle).FirstOrDefaultAsync(x => x.TicketId == SelectedTicket.TicketId);
            if (t == null) return;

            var rule = await db.PricingRules.FirstOrDefaultAsync(r => r.VehicleTypeId == t.VehicleTypeId);
            var dlg = new RenewTicketDialog(t, rule);
            if (dlg.ShowDialog() == true)
            {
                var payMethod = dlg.FinalPaymentMethod;
                var fee = dlg.FinalFee;

                // Xử lý xác nhận thanh toán khi gia hạn:
                if (payMethod == PaymentMethod.Cash)
                {
                    var confirmMsg = AppText.Get("Msg_Customers_ConfirmCashRenew", t.Customer?.FullName ?? "N/A", t.RegisteredLicensePlate, fee);
                    if (!_dialogService.ShowYesNo(confirmMsg, AppText.Get("Str_Customers_ConfirmCashRenewTitle")))
                    {
                        _dialogService.ShowWarning(AppText.Get("Msg_Customers_RenewCashNotCollected"));
                        return;
                    }
                }
                else if (payMethod == PaymentMethod.VietQR)
                {
                    var qrMsg = AppText.Get("Msg_Customers_ConfirmQrRenew", fee, t.TicketCode);
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

                t.EndDate = dlg.FinalNewExpiry;
                t.Status = MonthlyTicketStatus.Active;
                t.DurationMonths += dlg.FinalDurationMonths;
                t.MonthlyPrice += dlg.FinalFee;
                await db.SaveChangesAsync();
                
                var logLine = $"{{\"timestamp\":\"{DateTime.UtcNow:O}\",\"action\":\"RenewTicket\",\"details\":\"TicketCode={t.TicketCode}, DurationAdded={dlg.FinalDurationMonths}, Fee={dlg.FinalFee}, PaymentMethod={payMethod}\"}}\n";
                await System.IO.File.AppendAllTextAsync("gate_audit_log.jsonl", logLine);

                await LoadDataAsync();
                _dialogService.ShowSuccess(AppText.Get("Msg_Customers_PassRenewed", dlg.FinalDurationMonths, dlg.FinalFee, payMethod));
            }
        } catch (Exception ex) {
            _dialogService.ShowError(AppText.Get("Msg_Common_ErrorDetail", ex.Message));
        }
    }

    private async Task AddBlacklistAsync()
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var customers = await db.Customers.OrderBy(c => c.FullName).ToListAsync();
            string? prefillPlate = SelectedCustomer?.DefaultLicensePlate;
            // "Chưa đăng ký" (theo ngôn ngữ lúc nạp) hoặc mọi chuỗi không phải biển số: không điền sẵn
            if (!SmartPS.Services.GateControl.LicensePlateNormalizer.IsValid(prefillPlate)) prefillPlate = null;

            var dlg = new AddBlacklistDialog(customers, prefillPlate);
            if (dlg.ShowDialog() == true)
            {
                var cleanPlate = System.Text.RegularExpressions.Regex.Replace(dlg.FinalLicensePlate.Trim(), @"[^a-zA-Z0-9]", "").ToUpperInvariant();
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

                // Kiểm tra xem xe này đã có trong Blacklist chưa
                var existing = await db.BlacklistedVehicles.FirstOrDefaultAsync(b => b.LicensePlate == cleanPlate);
                if (existing != null)
                {
                    if (existing.IsActive)
                    {
                        _dialogService.ShowWarning(AppText.Get("Msg_Customers_AlreadyBanned", cleanPlate));
                        return;
                    }
                    else
                    {
                        // Đã từng bị cấm nhưng đang gỡ cấm (xóa mềm) -> Kích hoạt cấm lại
                        existing.IsActive = true;
                        existing.Reason = dlg.FinalReason;
                        existing.Notes = dlg.FinalNotes;
                        existing.CustomerId = dlg.FinalCustomerId;
                        existing.CreatedAt = DateTime.UtcNow;
                        await db.SaveChangesAsync();

                        // Ghi nhật ký kiểm toán (Audit Log)
                        var auditLine = $"{{\"timestamp\":\"{DateTime.UtcNow:O}\",\"action\":\"BlacklistReactivate\",\"details\":\"Plate={cleanPlate}, Reason={dlg.FinalReason}\"}}\n";
                        await System.IO.File.AppendAllTextAsync("gate_audit_log.jsonl", auditLine);

                        await LoadDataAsync();
                        _dialogService.ShowSuccess(AppText.Get("Msg_Customers_RebanActivated", cleanPlate));
                        return;
                    }
                }

                var item = new BlacklistedVehicle
                {
                    LicensePlate = cleanPlate,
                    Reason = dlg.FinalReason,
                    Notes = dlg.FinalNotes,
                    CustomerId = dlg.FinalCustomerId,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                };

                db.BlacklistedVehicles.Add(item);
                await db.SaveChangesAsync();

                // Ghi nhật ký kiểm toán (Audit Log)
                var logLine = $"{{\"timestamp\":\"{DateTime.UtcNow:O}\",\"action\":\"BlacklistAdd\",\"details\":\"Plate={cleanPlate}, Reason={dlg.FinalReason}, Notes={dlg.FinalNotes}\"}}\n";
                await System.IO.File.AppendAllTextAsync("gate_audit_log.jsonl", logLine);

                await LoadDataAsync();
                _dialogService.ShowSuccess(AppText.Get("Msg_Customers_BlacklistAdded", cleanPlate));
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

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var b = await db.BlacklistedVehicles.FindAsync(item.BlacklistId);
            if (b != null)
            {
                b.IsActive = !b.IsActive;
                await db.SaveChangesAsync();

                // Ghi nhật ký kiểm toán (Audit Log)
                var logAction = b.IsActive ? "BlacklistEnable" : "BlacklistDisable";
                var logLine = $"{{\"timestamp\":\"{DateTime.UtcNow:O}\",\"action\":\"{logAction}\",\"details\":\"Plate={b.LicensePlate}, IsActive={b.IsActive}\"}}\n";
                await System.IO.File.AppendAllTextAsync("gate_audit_log.jsonl", logLine);

                await LoadDataAsync();
                _dialogService.ShowSuccess(AppText.Get(b.IsActive ? "Msg_Customers_VehicleBanned" : "Msg_Customers_VehicleUnbanned", item.LicensePlate));
            }
        }
        catch (Exception ex)
        {
            _dialogService.ShowError(AppText.Get("Msg_Common_ErrorDetail", ex.Message));
        }
    }

    private async Task DeleteBlacklistAsync(object? parameter)
    {
        var item = parameter as BlacklistedVehicle ?? SelectedBlacklist;
        if (item == null) return;

        if (!_dialogService.ShowYesNo(AppText.Get("Msg_Customers_ConfirmRemoveBlacklist", item.LicensePlate), AppText.Get("Str_Customers_RemoveBlacklistTitle"))) return;

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var b = await db.BlacklistedVehicles.FindAsync(item.BlacklistId);
            if (b != null)
            {
                // Xóa mềm: đánh dấu IsActive = false thay vì xóa cứng khỏi cơ sở dữ liệu
                b.IsActive = false;
                await db.SaveChangesAsync();

                // Ghi nhật ký kiểm toán (Audit Log)
                var logLine = $"{{\"timestamp\":\"{DateTime.UtcNow:O}\",\"action\":\"BlacklistSoftDelete\",\"details\":\"Plate={b.LicensePlate}\"}}\n";
                await System.IO.File.AppendAllTextAsync("gate_audit_log.jsonl", logLine);

                await LoadDataAsync();
                _dialogService.ShowSuccess(AppText.Get("Msg_Customers_BlacklistRemoved", item.LicensePlate));
            }
        }
        catch (Exception ex)
        {
            _dialogService.ShowError(AppText.Get("Msg_Common_ErrorDetail", ex.Message));
        }
    }
}
