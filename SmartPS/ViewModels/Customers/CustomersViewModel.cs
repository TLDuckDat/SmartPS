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

namespace SmartPS.ViewModels.Customers;

public class CustomersViewModel : ViewModelBase
{
    private readonly IDbContextFactory<SmartPsDbContext> _dbFactory;
    private readonly IDialogService _dialogService;
    private readonly List<CustomerItemViewModel> _allCustomers = new();
    private readonly List<Household> _allHouseholds = new();
    private readonly List<MonthlyTicket> _allTickets = new();
    
    // Grids
    public ObservableCollection<CustomerItemViewModel> FilteredCustomers { get; } = new();
    public ObservableCollection<Household> Households { get; } = new();
    public ObservableCollection<MonthlyTicket> Tickets { get; } = new();
    
    // Selections
    private CustomerItemViewModel? _selectedCustomer;
    public CustomerItemViewModel? SelectedCustomer { get => _selectedCustomer; set => SetProperty(ref _selectedCustomer, value); }
    private Household? _selectedHousehold;
    public Household? SelectedHousehold { get => _selectedHousehold; set => SetProperty(ref _selectedHousehold, value); }
    private MonthlyTicket? _selectedTicket;
    public MonthlyTicket? SelectedTicket { get => _selectedTicket; set => SetProperty(ref _selectedTicket, value); }

    // Search & Sort for Customers
    private string _searchKeyword = string.Empty;
    public string SearchKeyword { get => _searchKeyword; set { if (SetProperty(ref _searchKeyword, value)) ApplyCustomerFilter(); } }

    public string[] CustomerSortOptions { get; } = { "Mặc định (Thứ tự tạo)", "Tên khách hàng (A - Z)" };
    private int _selectedCustomerSortIndex = 0;
    public int SelectedCustomerSortIndex
    {
        get => _selectedCustomerSortIndex;
        set { if (SetProperty(ref _selectedCustomerSortIndex, value)) ApplyCustomerFilter(); }
    }

    // Search & Sort for Households
    private string _householdSearchKeyword = string.Empty;
    public string HouseholdSearchKeyword { get => _householdSearchKeyword; set { if (SetProperty(ref _householdSearchKeyword, value)) ApplyHouseholdFilter(); } }

    public string[] HouseholdSortOptions { get; } = { "Mặc định (Thứ tự tạo)", "Mã căn hộ" };
    private int _selectedHouseholdSortIndex = 0;
    public int SelectedHouseholdSortIndex
    {
        get => _selectedHouseholdSortIndex;
        set { if (SetProperty(ref _selectedHouseholdSortIndex, value)) ApplyHouseholdFilter(); }
    }

    // Search & Sort for Monthly Tickets
    private string _ticketSearchKeyword = string.Empty;
    public string TicketSearchKeyword { get => _ticketSearchKeyword; set { if (SetProperty(ref _ticketSearchKeyword, value)) ApplyTicketFilter(); } }

    public string[] TicketSortOptions { get; } = { "Mặc định (Thứ tự tạo)", "Tên khách hàng (A - Z)", "Hạn dùng (Sắp hết hạn)" };
    private int _selectedTicketSortIndex = 0;
    public int SelectedTicketSortIndex
    {
        get => _selectedTicketSortIndex;
        set { if (SetProperty(ref _selectedTicketSortIndex, value)) ApplyTicketFilter(); }
    }

    // KPIs
    private int _totalCustomers;
    public int TotalCustomers { get => _totalCustomers; set => SetProperty(ref _totalCustomers, value); }
    private int _activeTicketsCount;
    public int ActiveTicketsCount { get => _activeTicketsCount; set => SetProperty(ref _activeTicketsCount, value); }
    private int _expiringSoonCount;
    public int ExpiringSoonCount { get => _expiringSoonCount; set => SetProperty(ref _expiringSoonCount, value); }
    private decimal _monthlyRevenueTotal;
    public decimal MonthlyRevenueTotal
    {
        get => _monthlyRevenueTotal;
        set { if (SetProperty(ref _monthlyRevenueTotal, value)) OnPropertyChanged(nameof(MonthlyRevenueTotalFormatted)); }
    }
    public string MonthlyRevenueTotalFormatted => $"{MonthlyRevenueTotal:N0} đ/tháng";

    public AsyncRelayCommand RefreshCommand { get; }
    
    // Add Commands
    public AsyncRelayCommand AddHouseholdCommand { get; }
    public AsyncRelayCommand AddCustomerCommand { get; }
    public AsyncRelayCommand AddVehicleCommand { get; }
    public AsyncRelayCommand AddTicketCommand { get; }

    // Edit/Delete/Action Commands
    public AsyncRelayCommand DeleteHouseholdCommand { get; }
    public AsyncRelayCommand DeleteCustomerCommand { get; }
    public AsyncRelayCommand DeleteTicketCommand { get; }
    public AsyncRelayCommand ToggleTicketCommand { get; }
    public AsyncRelayCommand RenewTicketCommand { get; }

    public CustomersViewModel(IDbContextFactory<SmartPsDbContext> dbFactory, IDialogService dialogService)
    {
        _dbFactory = dbFactory;
        _dialogService = dialogService;
        RefreshCommand = new AsyncRelayCommand(LoadDataAsync);
        
        AddHouseholdCommand = new AsyncRelayCommand(AddHouseholdAsync);
        AddCustomerCommand = new AsyncRelayCommand(AddCustomerAsync);
        AddVehicleCommand = new AsyncRelayCommand(AddVehicleAsync);
        AddTicketCommand = new AsyncRelayCommand(AddTicketAsync);

        DeleteHouseholdCommand = new AsyncRelayCommand(DeleteHouseholdAsync);
        DeleteCustomerCommand = new AsyncRelayCommand(DeleteCustomerAsync);
        DeleteTicketCommand = new AsyncRelayCommand(DeleteTicketAsync);
        ToggleTicketCommand = new AsyncRelayCommand(ToggleTicketAsync);
        RenewTicketCommand = new AsyncRelayCommand(RenewTicketAsync);
        
        _ = LoadDataAsync();
    }

    public async Task LoadDataAsync()
    {
        try 
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            
            var households = await db.Households.ToListAsync();
            _allHouseholds.Clear();
            _allHouseholds.AddRange(households);
            ApplyHouseholdFilter();
            
            var tickets = await db.MonthlyTickets.Include(t => t.Customer).Include(t => t.Vehicle).ToListAsync();
            _allTickets.Clear();
            _allTickets.AddRange(tickets);
            ApplyTicketFilter();
            
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
                        DefaultLicensePlate = mt?.RegisteredLicensePlate ?? "Chưa ĐK",
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
            _dialogService.ShowError($"Lỗi tải dữ liệu: {ex.Message}");
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

    private async Task AddHouseholdAsync()
    {
        var dlg = new HouseholdDialog();
        if (dlg.ShowDialog() == true)
        {
            try {
                await using var db = await _dbFactory.CreateDbContextAsync();
                if (await db.Households.AnyAsync(h => h.ApartmentCode == dlg.ApartmentCode))
                {
                    _dialogService.ShowError("Mã căn hộ này đã tồn tại!");
                    return;
                }
                db.Households.Add(new Household { ApartmentCode = dlg.ApartmentCode, MaxVehicles = dlg.MaxVehicles, Notes = dlg.Notes, CreatedAt = DateTime.UtcNow });
                await db.SaveChangesAsync();
                await LoadDataAsync();
                _dialogService.ShowSuccess("Thêm hộ gia đình thành công!");
            } catch (Exception ex) {
                _dialogService.ShowError($"Lỗi khi lưu: {ex.Message}");
            }
        }
    }

    private async Task AddCustomerAsync()
    {
        try {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var hhs = await db.Households.ToListAsync();
            var dlg = new CustomerDialog(hhs);
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
                        _dialogService.ShowError($"Hộ gia đình này đã đạt giới hạn xe ({max} xe). Không thể thêm phương tiện.");
                        return;
                    }
                }
                
                var cust = new Customer { FullName = dlg.FinalFullName, PhoneNumber = dlg.FinalPhoneNumber, HouseholdId = dlg.FinalSelectedHouseholdId, Type = dlg.FinalSelectedHouseholdId.HasValue ? CustomerType.Resident : CustomerType.External, CreatedAt = DateTime.UtcNow };
                db.Customers.Add(cust);
                await db.SaveChangesAsync();
                
                if (!string.IsNullOrEmpty(dlg.FinalLicensePlate))
                {
                    db.Vehicles.Add(new Vehicle { LicensePlate = dlg.FinalLicensePlate, VehicleTypeId = dlg.FinalSelectedVehicleType, OwnerCustomerId = cust.CustomerId, CreatedAt = DateTime.UtcNow, IsActive = true });
                    await db.SaveChangesAsync();
                }

                await LoadDataAsync();
                _dialogService.ShowSuccess("Thêm khách hàng thành công!");
            }
        } catch (Exception ex) {
            _dialogService.ShowError($"Lỗi: {ex.Message}");
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
                        _dialogService.ShowError($"Hộ gia đình này đã đạt giới hạn xe ({max} xe).");
                        return;
                    }
                }
                
                db.Vehicles.Add(new Vehicle { LicensePlate = dlg.FinalLicensePlate, VehicleTypeId = dlg.FinalSelectedVehicleType, OwnerCustomerId = cid, CreatedAt = DateTime.UtcNow, IsActive = true });
                await db.SaveChangesAsync();
                await LoadDataAsync();
                _dialogService.ShowSuccess("Thêm xe thành công!");
            }
        } catch (Exception ex) {
            _dialogService.ShowError($"Lỗi: {ex.Message}");
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
                    _dialogService.ShowError("Phương tiện này đã có vé tháng đang hoạt động!");
                    return;
                }
                var v = await db.Vehicles.FirstOrDefaultAsync(x => x.VehicleId == vid);
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

                var t = new MonthlyTicket
                {
                    CustomerId = dlg.FinalSelectedCustomerId,
                    VehicleId = vid,
                    RegisteredLicensePlate = v?.LicensePlate ?? "",
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
                
                var logLine = $"{{\"timestamp\":\"{DateTime.UtcNow:O}\",\"action\":\"IssueTicket\",\"details\":\"TicketCode={t.TicketCode}, Plate={t.RegisteredLicensePlate}, Fee={ticketFee}\"}}\n";
                await System.IO.File.AppendAllTextAsync("gate_audit_log.jsonl", logLine);

                await LoadDataAsync();
                _dialogService.ShowSuccess($"Đăng ký vé tháng thành công! Mức phí: {ticketFee:N0} VNĐ");
            }
        } catch (Exception ex) {
            _dialogService.ShowError($"Lỗi: {ex.Message}");
        }
    }

    private async Task DeleteHouseholdAsync()
    {
        if (SelectedHousehold == null) return;
        if (!_dialogService.ShowYesNo($"Bạn có chắc chắn muốn xóa Hộ gia đình {SelectedHousehold.ApartmentCode}?", "Xóa Hộ Gia Đình")) return;
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
                _dialogService.ShowSuccess("Xóa hộ gia đình thành công!");
            }
        } catch (Exception ex) {
            _dialogService.ShowError($"Không thể xóa vì lỗi cơ sở dữ liệu. Chi tiết: {ex.Message}");
        }
    }

    private async Task DeleteCustomerAsync()
    {
        if (SelectedCustomer == null) return;
        if (!_dialogService.ShowYesNo($"Bạn có chắc chắn muốn xóa khách hàng {SelectedCustomer.FullName}?", "Xóa Khách Hàng")) return;
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
                _dialogService.ShowSuccess("Xóa khách hàng thành công!");
            }
        } catch (Exception ex) {
            _dialogService.ShowError($"Không thể xóa vì lỗi cơ sở dữ liệu. Chi tiết: {ex.Message}");
        }
    }

    private async Task DeleteTicketAsync()
    {
        if (SelectedTicket == null) return;
        if (!_dialogService.ShowYesNo($"Bạn có chắc chắn muốn xóa vé {SelectedTicket.TicketCode}?", "Xóa Vé Tháng")) return;
        try {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var t = await db.MonthlyTickets.FindAsync(SelectedTicket.TicketId);
            if (t != null) {
                db.MonthlyTickets.Remove(t);
                await db.SaveChangesAsync();
                await LoadDataAsync();
                _dialogService.ShowSuccess("Xóa vé tháng thành công!");
            }
        } catch (Exception ex) {
            _dialogService.ShowError($"Lỗi: {ex.Message}");
        }
    }

    private async Task ToggleTicketAsync()
    {
        if (SelectedTicket == null) return;
        var action = SelectedTicket.Status == MonthlyTicketStatus.Active ? "Khóa" : "Mở khóa";
        if (!_dialogService.ShowYesNo($"Bạn có chắc muốn {action} vé {SelectedTicket.TicketCode}?", $"{action} Vé Tháng")) return;
        try {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var t = await db.MonthlyTickets.FindAsync(SelectedTicket.TicketId);
            if (t != null) {
                t.Status = t.Status == MonthlyTicketStatus.Active ? MonthlyTicketStatus.Cancelled : MonthlyTicketStatus.Active;
                await db.SaveChangesAsync();
                await LoadDataAsync();
                _dialogService.ShowSuccess($"{action} vé tháng thành công!");
            }
        } catch (Exception ex) {
            _dialogService.ShowError($"Lỗi: {ex.Message}");
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
                t.EndDate = dlg.FinalNewExpiry;
                t.Status = MonthlyTicketStatus.Active;
                t.DurationMonths += dlg.FinalDurationMonths;
                t.MonthlyPrice += dlg.FinalFee;
                await db.SaveChangesAsync();
                
                var logLine = $"{{\"timestamp\":\"{DateTime.UtcNow:O}\",\"action\":\"RenewTicket\",\"details\":\"TicketCode={t.TicketCode}, DurationAdded={dlg.FinalDurationMonths}, Fee={dlg.FinalFee}\"}}\n";
                await System.IO.File.AppendAllTextAsync("gate_audit_log.jsonl", logLine);

                await LoadDataAsync();
                _dialogService.ShowSuccess($"Gia hạn vé tháng thành công thêm {dlg.FinalDurationMonths} tháng! (+{dlg.FinalFee:N0} VNĐ)");
            }
        } catch (Exception ex) {
            _dialogService.ShowError($"Lỗi: {ex.Message}");
        }
    }
}
