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
    public string MonthlyRevenueTotalFormatted => $"{MonthlyRevenueTotal:N0} đ/tháng";

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

                var payMethod = dlg.FinalPaymentMethod;

                // Xử lý luồng thanh toán:
                if (payMethod == PaymentMethod.Cash)
                {
                    // Tiền mặt: Nhân viên xác nhận đã thu đủ tiền
                    var confirmMsg = $"XÁC NHẬN THU TIỀN MẶT:\n\n" +
                                     $"- Khách hàng: {v?.OwnerCustomer?.FullName ?? "N/A"}\n" +
                                     $"- Biển số xe: {v?.LicensePlate}\n" +
                                     $"- Số tiền cần thu: {ticketFee:N0} VNĐ\n\n" +
                                     $"Nhân viên đã nhận đủ số tiền mặt này từ khách hàng chưa?";
                    if (!_dialogService.ShowYesNo(confirmMsg, "Xác Nhận Thu Tiền Mặt"))
                    {
                        _dialogService.ShowWarning("Giao dịch bị hủy do chưa thu tiền mặt từ khách hàng!");
                        return;
                    }
                }
                else if (payMethod == PaymentMethod.VietQR)
                {
                    // VietQR: Hiển thị thông tin chuyển khoản và chờ nhân viên/cổng xác nhận đã thanh toán thành công
                    var qrMsg = $"CHUYỂN KHOẢN VIETQR:\n\n" +
                                $"- Số tiền: {ticketFee:N0} VNĐ\n" +
                                $"- Nội dung CK: DANGKYVE {v?.LicensePlate}\n\n" +
                                $"Khách hàng đã quét mã và tài khoản đã nhận tiền thành công chưa?";
                    if (!_dialogService.ShowYesNo(qrMsg, "Xác Nhận Nhận Tiền VietQR"))
                    {
                        _dialogService.ShowWarning("Thanh toán VietQR chưa hoàn tất. Chưa tạo vé tháng!");
                        return;
                    }
                }
                else if (payMethod == PaymentMethod.Card)
                {
                    // Thẻ / POS: Xác nhận quẹt thẻ thành công
                    var cardMsg = $"THANH TOÁN THẺ / POS:\n\n" +
                                  $"- Số tiền: {ticketFee:N0} VNĐ\n" +
                                  $"Giao dịch quẹt thẻ qua máy POS đã in hóa đơn thành công chưa?";
                    if (!_dialogService.ShowYesNo(cardMsg, "Xác Nhận POS"))
                    {
                        _dialogService.ShowWarning("Giao dịch quẹt thẻ chưa thành công. Chưa tạo vé tháng!");
                        return;
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
                
                var logLine = $"{{\"timestamp\":\"{DateTime.UtcNow:O}\",\"action\":\"IssueTicket\",\"details\":\"TicketCode={t.TicketCode}, Plate={t.RegisteredLicensePlate}, Fee={ticketFee}, PaymentMethod={payMethod}\"}}\n";
                await System.IO.File.AppendAllTextAsync("gate_audit_log.jsonl", logLine);

                await LoadDataAsync();
                _dialogService.ShowSuccess($"Đăng ký vé tháng thành công! Mức phí: {ticketFee:N0} VNĐ ({payMethod})");
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

    private async Task DeleteRowVehicleAsync(object? parameter)
    {
        var item = parameter as CustomerItemViewModel ?? SelectedCustomer;
        if (item == null) return;

        // Nếu dòng này có phương tiện
        if (item.VehicleId.HasValue && item.VehicleId.Value > 0)
        {
            var msg = $"Bạn có chắc chắn muốn xóa phương tiện '{item.DefaultLicensePlate}' của khách hàng {item.FullName}?\n(Các vé tháng liên quan đến xe này sẽ bị hủy bỏ)";
            if (!_dialogService.ShowYesNo(msg, "Xóa Phương Tiện")) return;

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
                    _dialogService.ShowSuccess($"Đã xóa phương tiện '{item.DefaultLicensePlate}' thành công!");
                }
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"Lỗi khi xóa xe: {ex.Message}");
            }
        }
        else
        {
            // Dòng này không có xe (chỉ có khách hàng chưa thêm xe)
            var msg = $"Khách hàng {item.FullName} chưa có phương tiện. Bạn có muốn xóa hồ sơ khách hàng này không?";
            if (!_dialogService.ShowYesNo(msg, "Xóa Khách Hàng")) return;

            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync();
                var c = await db.Customers.FindAsync(item.CustomerId);
                if (c != null)
                {
                    db.Customers.Remove(c);
                    await db.SaveChangesAsync();
                    await LoadDataAsync();
                    _dialogService.ShowSuccess($"Đã xóa khách hàng {item.FullName} thành công!");
                }
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"Lỗi khi xóa khách hàng: {ex.Message}");
            }
        }
    }

    private async Task DeleteTicketAsync(object? parameter)
    {
        var item = parameter as MonthlyTicket ?? SelectedTicket;
        if (item == null) return;
        
        // Hỏi người dùng muốn HỦY VÉ (giữ lại lịch sử tài chính) hay XÓA CỨNG (chỉ khi nhập sai)
        var msg = $"Bạn muốn xử lý vé '{item.TicketCode}' (Xe: {item.RegisteredLicensePlate}) như thế nào?\n\n" +
                  "- Chọn 'Có (Yes)': HỦY VÉ (Đổi trạng thái Đã hủy, giữ lại lịch sử doanh thu đối soát).\n" +
                  "- Chọn 'Không (No)': XÓA VĨNH VIỄN (Chỉ dùng khi nhập nhầm, mất toàn bộ lịch sử).";
        
        bool cancelSoft = _dialogService.ShowYesNo(msg, "Hủy Hoặc Xóa Vé Tháng");
        
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
                    _dialogService.ShowSuccess($"Đã HỦY vé tháng '{t.TicketCode}'! Vé không còn hiệu lực tại cổng nhưng lịch sử doanh thu vẫn được bảo toàn.");
                }
                else
                {
                    // Xóa cứng
                    db.MonthlyTickets.Remove(t);
                    await db.SaveChangesAsync();
                    await LoadDataAsync();
                    _dialogService.ShowSuccess($"Đã XÓA VĨNH VIỄN vé tháng '{t.TicketCode}' khỏi hệ thống!");
                }
            }
        } catch (Exception ex) {
            _dialogService.ShowError($"Lỗi: {ex.Message}");
        }
    }

    private async Task CancelTicketAsync(object? parameter)
    {
        var item = parameter as MonthlyTicket ?? SelectedTicket;
        if (item == null) return;

        if (!_dialogService.ShowYesNo($"Bạn có chắc chắn muốn HỦY vé tháng '{item.TicketCode}'?\n(Vé sẽ bị ngừng hiệu lực tại cổng, nhưng lịch sử tiền thu vẫn được lưu để đối soát)", "Hủy Vé Tháng")) return;

        try {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var t = await db.MonthlyTickets.FindAsync(item.TicketId);
            if (t != null) {
                t.Status = MonthlyTicketStatus.Cancelled;
                await db.SaveChangesAsync();
                await LoadDataAsync();
                _dialogService.ShowSuccess($"Đã hủy vé tháng '{t.TicketCode}' thành công!");
            }
        } catch (Exception ex) {
            _dialogService.ShowError($"Lỗi: {ex.Message}");
        }
    }

    private async Task ToggleTicketAsync(object? parameter)
    {
        var item = parameter as MonthlyTicket ?? SelectedTicket;
        if (item == null) return;
        var isLocked = item.Status == MonthlyTicketStatus.Suspended || item.Status == MonthlyTicketStatus.Cancelled;
        var action = isLocked ? "Mở khóa" : "Tạm khóa";
        if (!_dialogService.ShowYesNo($"Bạn có chắc muốn {action} thẻ cho vé {item.TicketCode}?", $"{action} Thẻ Vé Tháng")) return;
        try {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var t = await db.MonthlyTickets.FindAsync(item.TicketId);
            if (t != null) {
                t.Status = isLocked ? MonthlyTicketStatus.Active : MonthlyTicketStatus.Suspended;
                await db.SaveChangesAsync();
                await LoadDataAsync();
                _dialogService.ShowSuccess($"Đã {action.ToLower()} thẻ vé tháng thành công!");
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
                var payMethod = dlg.FinalPaymentMethod;
                var fee = dlg.FinalFee;

                // Xử lý xác nhận thanh toán khi gia hạn:
                if (payMethod == PaymentMethod.Cash)
                {
                    var confirmMsg = $"XÁC NHẬN THU TIỀN GIA HẠN (TIỀN MẶT):\n\n" +
                                     $"- Khách hàng: {t.Customer?.FullName ?? "N/A"}\n" +
                                     $"- Biển số: {t.RegisteredLicensePlate}\n" +
                                     $"- Số tiền gia hạn: {fee:N0} VNĐ\n\n" +
                                     $"Nhân viên đã nhận đủ số tiền mặt này từ khách hàng chưa?";
                    if (!_dialogService.ShowYesNo(confirmMsg, "Xác Nhận Tiền Mặt"))
                    {
                        _dialogService.ShowWarning("Hủy gia hạn do chưa thu tiền từ khách hàng!");
                        return;
                    }
                }
                else if (payMethod == PaymentMethod.VietQR)
                {
                    var qrMsg = $"CHUYỂN KHOẢN VIETQR (GIA HẠN):\n\n" +
                                $"- Số tiền: {fee:N0} VNĐ\n" +
                                $"- Nội dung CK: GIAHAN {t.TicketCode}\n\n" +
                                $"Khách hàng đã quét mã và tài khoản đã nhận tiền thành công chưa?";
                    if (!_dialogService.ShowYesNo(qrMsg, "Xác Nhận Nhận Tiền VietQR"))
                    {
                        _dialogService.ShowWarning("Thanh toán VietQR chưa hoàn tất. Chưa gia hạn vé!");
                        return;
                    }
                }
                else if (payMethod == PaymentMethod.Card)
                {
                    var cardMsg = $"THANH TOÁN THẺ / POS (GIA HẠN):\n\n" +
                                  $"- Số tiền: {fee:N0} VNĐ\n" +
                                  $"Giao dịch quẹt thẻ qua máy POS đã thành công chưa?";
                    if (!_dialogService.ShowYesNo(cardMsg, "Xác Nhận POS"))
                    {
                        _dialogService.ShowWarning("Giao dịch quẹt thẻ chưa thành công. Chưa gia hạn vé!");
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
                _dialogService.ShowSuccess($"Gia hạn vé tháng thành công thêm {dlg.FinalDurationMonths} tháng! (+{dlg.FinalFee:N0} VNĐ - {payMethod})");
            }
        } catch (Exception ex) {
            _dialogService.ShowError($"Lỗi: {ex.Message}");
        }
    }

    private async Task AddBlacklistAsync()
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var customers = await db.Customers.OrderBy(c => c.FullName).ToListAsync();
            string? prefillPlate = SelectedCustomer?.DefaultLicensePlate;
            if (prefillPlate == "Chưa ĐK") prefillPlate = null;

            var dlg = new AddBlacklistDialog(customers, prefillPlate);
            if (dlg.ShowDialog() == true)
            {
                var cleanPlate = dlg.FinalLicensePlate.Trim().ToUpperInvariant();
                var normPlate = cleanPlate.Replace(" ", "").Replace("-", "").Replace(".", "");

                // Kiểm tra xem xe này đã có trong Blacklist chưa
                var existing = await db.BlacklistedVehicles.FirstOrDefaultAsync(b => b.LicensePlate.Replace(" ", "").Replace("-", "").Replace(".", "").ToUpper() == normPlate);
                if (existing != null)
                {
                    if (existing.IsActive)
                    {
                        _dialogService.ShowWarning($"Biển số '{cleanPlate}' hiện ĐANG NẰM trong danh sách cấm rồi!");
                        return;
                    }
                    else
                    {
                        // Đã từng bị cấm nhưng đang gỡ cấm -> Kích hoạt cấm lại
                        existing.IsActive = true;
                        existing.Reason = dlg.FinalReason;
                        existing.Notes = dlg.FinalNotes;
                        existing.CustomerId = dlg.FinalCustomerId;
                        existing.CreatedAt = DateTime.UtcNow;
                        await db.SaveChangesAsync();
                        await LoadDataAsync();
                        _dialogService.ShowSuccess($"Đã kích hoạt cấm lại phương tiện '{cleanPlate}'!");
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
                await LoadDataAsync();
                _dialogService.ShowSuccess($"Đã thêm biển số '{cleanPlate}' vào danh sách cấm thành công!");
            }
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"Lỗi: {ex.Message}");
        }
    }

    private async Task ToggleBlacklistAsync(object? parameter)
    {
        var item = parameter as BlacklistedVehicle ?? SelectedBlacklist;
        if (item == null) return;

        var actionText = item.IsActive ? "GỠ CẤM (Cho phép vào bãi trở lại)" : "KÍCH HOẠT CẤM LẠI";
        if (!_dialogService.ShowYesNo($"Bạn có chắc chắn muốn {actionText} cho phương tiện '{item.LicensePlate}'?", "Xác nhận thay đổi")) return;

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var b = await db.BlacklistedVehicles.FindAsync(item.BlacklistId);
            if (b != null)
            {
                b.IsActive = !b.IsActive;
                await db.SaveChangesAsync();
                await LoadDataAsync();
                _dialogService.ShowSuccess($"Thao tác thành công! Trạng thái xe '{item.LicensePlate}': {(b.IsActive ? "Đang cấm" : "Đã gỡ cấm")}.");
            }
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"Lỗi: {ex.Message}");
        }
    }

    private async Task DeleteBlacklistAsync(object? parameter)
    {
        var item = parameter as BlacklistedVehicle ?? SelectedBlacklist;
        if (item == null) return;

        if (!_dialogService.ShowYesNo($"Bạn có chắc chắn muốn XÓA HOÀN TOÀN biển số '{item.LicensePlate}' khỏi danh sách đen?\n(Bản ghi lịch sử vi phạm sẽ bị xóa vĩnh viễn)", "Xóa Khỏi Blacklist")) return;

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var b = await db.BlacklistedVehicles.FindAsync(item.BlacklistId);
            if (b != null)
            {
                db.BlacklistedVehicles.Remove(b);
                await db.SaveChangesAsync();
                await LoadDataAsync();
                _dialogService.ShowSuccess($"Đã xóa biển số '{item.LicensePlate}' khỏi danh sách đen!");
            }
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"Lỗi: {ex.Message}");
        }
    }
}
