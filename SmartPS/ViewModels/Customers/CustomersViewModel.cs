using System.Collections.ObjectModel;
using SmartPS.Models.Parking;

namespace SmartPS.ViewModels.Customers;

public class CustomersViewModel : ViewModelBase
{
    private readonly List<CustomerItemViewModel> _allCustomers = new();

    private int _totalCustomers;
    public int TotalCustomers
    {
        get => _totalCustomers;
        set => SetProperty(ref _totalCustomers, value);
    }

    private int _activeTicketsCount;
    public int ActiveTicketsCount
    {
        get => _activeTicketsCount;
        set => SetProperty(ref _activeTicketsCount, value);
    }

    private int _expiringSoonCount;
    public int ExpiringSoonCount
    {
        get => _expiringSoonCount;
        set => SetProperty(ref _expiringSoonCount, value);
    }

    private decimal _monthlyRevenueTotal;
    public decimal MonthlyRevenueTotal
    {
        get => _monthlyRevenueTotal;
        set
        {
            if (SetProperty(ref _monthlyRevenueTotal, value))
            {
                OnPropertyChanged(nameof(MonthlyRevenueTotalFormatted));
            }
        }
    }
    public string MonthlyRevenueTotalFormatted => $"{MonthlyRevenueTotal:N0} đ/tháng";

    private string _searchKeyword = string.Empty;
    public string SearchKeyword
    {
        get => _searchKeyword;
        set
        {
            if (SetProperty(ref _searchKeyword, value))
            {
                ApplyFilter();
            }
        }
    }

    private CustomerItemViewModel? _selectedCustomer;
    public CustomerItemViewModel? SelectedCustomer
    {
        get => _selectedCustomer;
        set => SetProperty(ref _selectedCustomer, value);
    }

    public ObservableCollection<CustomerItemViewModel> FilteredCustomers { get; } = new();

    public AsyncRelayCommand RefreshCommand { get; }

    public CustomersViewModel()
    {
        RefreshCommand = new AsyncRelayCommand(LoadDataAsync);
        InitializeCustomers();
        _ = LoadDataAsync();
    }

    private void InitializeCustomers()
    {
        _allCustomers.Clear();
        _allCustomers.AddRange(new[]
        {
            new CustomerItemViewModel
            {
                CustomerId = 1,
                FullName = "Nguyễn Văn Hùng",
                PhoneNumber = "0988.123.456",
                Email = "hung.nguyen@smartps.vn",
                IdentityCard = "001200001234",
                DefaultLicensePlate = "30K-555.55",
                Type = CustomerType.VIP,
                VehicleTypeName = "Ô tô con 4-7 chỗ",
                MonthlyTicketCode = "MT-2026-001",
                TicketExpiry = DateTime.UtcNow.AddMonths(5)
            },
            new CustomerItemViewModel
            {
                CustomerId = 2,
                FullName = "Trần Thị Mai Hương",
                PhoneNumber = "0912.888.999",
                Email = "huong.tran@gmail.com",
                IdentityCard = "001201004567",
                DefaultLicensePlate = "29B1-888.88",
                Type = CustomerType.VIP,
                VehicleTypeName = "Xe máy hai bánh",
                MonthlyTicketCode = "MT-2026-002",
                TicketExpiry = DateTime.UtcNow.AddMonths(3)
            },
            new CustomerItemViewModel
            {
                CustomerId = 3,
                FullName = "Lê Hoàng Long",
                PhoneNumber = "0977.345.678",
                Email = "long.le@fpt.com.vn",
                IdentityCard = "001202008899",
                DefaultLicensePlate = "30F-999.99",
                Type = CustomerType.Loyal,
                VehicleTypeName = "Ô tô con 4-7 chỗ",
                MonthlyTicketCode = "MT-2026-003",
                TicketExpiry = DateTime.UtcNow.AddDays(7)
            },
            new CustomerItemViewModel
            {
                CustomerId = 4,
                FullName = "Phạm Quốc Tuấn",
                PhoneNumber = "0904.567.890",
                Email = "tuan.pham@outlook.com",
                IdentityCard = "001203001122",
                DefaultLicensePlate = "29D2-123.45",
                Type = CustomerType.Regular,
                VehicleTypeName = "Xe máy hai bánh",
                MonthlyTicketCode = "MT-2026-004",
                TicketExpiry = DateTime.UtcNow.AddDays(-2) // Hết hạn
            },
            new CustomerItemViewModel
            {
                CustomerId = 5,
                FullName = "Đặng Thùy Dung",
                PhoneNumber = "0936.789.012",
                Email = "dung.dang@vinhome.vn",
                IdentityCard = "001204003344",
                DefaultLicensePlate = "30A-678.90",
                Type = CustomerType.VIP,
                VehicleTypeName = "Ô tô con 4-7 chỗ",
                MonthlyTicketCode = "MT-2026-005",
                TicketExpiry = DateTime.UtcNow.AddMonths(8)
            }
        });
    }

    public Task LoadDataAsync()
    {
        TotalCustomers = _allCustomers.Count;
        ActiveTicketsCount = _allCustomers.Count(c => c.HasActiveTicket);
        ExpiringSoonCount = _allCustomers.Count(c => c.HasActiveTicket && c.TicketExpiry.HasValue && (c.TicketExpiry.Value - DateTime.UtcNow).TotalDays <= 15);
        
        // Tính doanh thu vé tháng định kỳ (Ô tô 1.200.000đ, Xe máy 100.000đ)
        MonthlyRevenueTotal = _allCustomers.Where(c => c.HasActiveTicket).Sum(c => c.VehicleTypeName.Contains("Ô tô") ? 1200000m : 100000m);

        ApplyFilter();
        return Task.CompletedTask;
    }

    private void ApplyFilter()
    {
        var kw = SearchKeyword?.Trim().ToUpperInvariant() ?? string.Empty;
        FilteredCustomers.Clear();

        foreach (var c in _allCustomers)
        {
            var match = string.IsNullOrEmpty(kw) ||
                        c.FullName.ToUpperInvariant().Contains(kw) ||
                        c.PhoneNumber.Contains(kw) ||
                        c.DefaultLicensePlate.ToUpperInvariant().Contains(kw) ||
                        c.MonthlyTicketCode.ToUpperInvariant().Contains(kw);

            if (match)
            {
                FilteredCustomers.Add(c);
            }
        }

        if (SelectedCustomer == null || !FilteredCustomers.Contains(SelectedCustomer))
        {
            SelectedCustomer = FilteredCustomers.FirstOrDefault();
        }
    }
}
