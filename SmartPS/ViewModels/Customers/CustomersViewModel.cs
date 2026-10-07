using System.Collections.ObjectModel;
using SmartPS.Constants;
using SmartPS.Services.Authorization;
using SmartPS.Services.Customers;
using SmartPS.Services.Dialog;
using SmartPS.Services.Localization;

namespace SmartPS.ViewModels.Customers;

/// <summary>
/// Màn Khách hàng: danh sách cư dân/khách từ DB, biểu mẫu thêm/sửa kèm xe và vé tháng, và tab danh sách đen.
/// Quyền ghi lấy từ <see cref="IPermissionService"/>; người chỉ có Customer.View thấy màn ở chế độ chỉ xem.
/// </summary>
public class CustomersViewModel : ViewModelBase
{
    private readonly ICustomerService _customers;
    private readonly IPermissionService _permissions;
    private readonly IDialogService _dialog;
    private readonly ILocalizationService _localization;

    private bool _loaded;
    private int _searchVersion;

    public CustomersViewModel(
        ICustomerService customers,
        IMonthlyTicketService tickets,
        IBlacklistService blacklist,
        IPermissionService permissions,
        IDialogService dialogService,
        ILocalizationService localization)
    {
        _customers = customers ?? throw new ArgumentNullException(nameof(customers));
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _dialog = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));

        Editor = new CustomerEditorViewModel(customers, tickets, permissions, dialogService, localization, RefreshAfterChangeAsync);
        Blacklist = new BlacklistViewModel(blacklist, permissions, dialogService, localization);

        RefreshCommand = new AsyncRelayCommand(LoadDataAsync);
        SearchCommand = new AsyncRelayCommand(() => SearchAsync(resetPage: true));
        PrevPageCommand = new AsyncRelayCommand(() => GoToPageAsync(PageIndex - 1), () => PageIndex > 0);
        NextPageCommand = new AsyncRelayCommand(() => GoToPageAsync(PageIndex + 1), () => PageIndex + 1 < TotalPages);
        NewCustomerCommand = new RelayCommand(() => Editor.StartNew(), () => CanManageCustomers);

        _localization.LanguageChanged += OnLanguageChanged;
    }

    public bool CanManageCustomers => _permissions.HasPermission(Permissions.CustomerManage);

    public bool CanManageBlacklist => _permissions.HasPermission(Permissions.BlacklistManage);

    public bool IsReadOnly => !CanManageCustomers;

    public ObservableCollection<CustomerItemViewModel> Customers { get; } = new();

    public CustomerEditorViewModel Editor { get; }

    public BlacklistViewModel Blacklist { get; }

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand SearchCommand { get; }

    public AsyncRelayCommand PrevPageCommand { get; }

    public AsyncRelayCommand NextPageCommand { get; }

    public RelayCommand NewCustomerCommand { get; }

    private CustomerItemViewModel? _selectedCustomer;
    public CustomerItemViewModel? SelectedCustomer
    {
        get => _selectedCustomer;
        set
        {
            if (SetProperty(ref _selectedCustomer, value) && value is not null)
            {
                _ = Editor.OpenAsync(value.CustomerId);
            }
        }
    }

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty) && _loaded)
            {
                _ = SearchAsync(resetPage: true);
            }
        }
    }

    private CustomerListFilter _filter = CustomerListFilter.All;
    public CustomerListFilter Filter
    {
        get => _filter;
        set
        {
            if (SetProperty(ref _filter, value) && _loaded)
            {
                _ = SearchAsync(resetPage: true);
            }
        }
    }

    private int _totalCustomers;
    public int TotalCustomers { get => _totalCustomers; private set => SetProperty(ref _totalCustomers, value); }

    private int _residentCount;
    public int ResidentCount { get => _residentCount; private set => SetProperty(ref _residentCount, value); }

    private int _activeTicketsCount;
    public int ActiveTicketsCount { get => _activeTicketsCount; private set => SetProperty(ref _activeTicketsCount, value); }

    private int _expiringSoonCount;
    public int ExpiringSoonCount { get => _expiringSoonCount; private set => SetProperty(ref _expiringSoonCount, value); }

    private decimal _activeTicketRevenue;
    public decimal ActiveTicketRevenue
    {
        get => _activeTicketRevenue;
        private set
        {
            if (SetProperty(ref _activeTicketRevenue, value))
            {
                OnPropertyChanged(nameof(ActiveTicketRevenueFormatted));
            }
        }
    }

    public string ActiveTicketRevenueFormatted => $"{ActiveTicketRevenue:N0} đ";

    private int _pageIndex;
    public int PageIndex
    {
        get => _pageIndex;
        private set
        {
            if (SetProperty(ref _pageIndex, value))
            {
                OnPropertyChanged(nameof(PageInfoText));
            }
        }
    }

    private int _totalPages;
    public int TotalPages
    {
        get => _totalPages;
        private set
        {
            if (SetProperty(ref _totalPages, value))
            {
                OnPropertyChanged(nameof(PageInfoText));
            }
        }
    }

    private int _matchedCount;
    public int MatchedCount
    {
        get => _matchedCount;
        private set
        {
            if (SetProperty(ref _matchedCount, value))
            {
                OnPropertyChanged(nameof(PageInfoText));
            }
        }
    }

    public string PageInfoText => _localization.GetString("Str_Cust_PageInfo", PageIndex + 1, Math.Max(1, TotalPages), MatchedCount);

    /// <summary>Tải tổng quan, danh sách khách hàng và danh sách đen. Không tự gọi từ constructor.</summary>
    public async Task LoadDataAsync()
    {
        try
        {
            var summary = await _customers.GetSummaryAsync();
            TotalCustomers = summary.TotalCustomers;
            ResidentCount = summary.ResidentCount;
            ActiveTicketsCount = summary.ActiveTicketCount;
            ExpiringSoonCount = summary.ExpiringSoonCount;
            ActiveTicketRevenue = summary.ActiveTicketRevenue;

            await SearchAsync(resetPage: false);
            await Blacklist.LoadAsync();
            _loaded = true;
        }
        catch (PermissionDeniedException)
        {
            _dialog.ShowWarning(_localization.GetString("Msg_Auth_PermissionDenied"));
        }
        catch (Exception ex)
        {
            _dialog.ShowError(_localization.GetString("Msg_Cust_LoadError", ex.Message));
        }
    }

    private async Task GoToPageAsync(int pageIndex)
    {
        PageIndex = Math.Max(0, pageIndex);
        await SearchAsync(resetPage: false);
    }

    private async Task SearchAsync(bool resetPage)
    {
        if (resetPage)
        {
            PageIndex = 0;
        }

        var version = Interlocked.Increment(ref _searchVersion);
        try
        {
            var page = await _customers.SearchAsync(new CustomerQuery
            {
                SearchText = SearchText,
                Filter = Filter,
                PageIndex = PageIndex
            });

            // Bỏ kết quả của lần tìm kiếm đã cũ
            if (version != Volatile.Read(ref _searchVersion))
            {
                return;
            }

            Customers.Clear();
            foreach (var item in page.Items)
            {
                Customers.Add(new CustomerItemViewModel(item, _localization));
            }

            PageIndex = page.PageIndex;
            TotalPages = page.TotalPages;
            MatchedCount = page.TotalCount;
            PrevPageCommand.RaiseCanExecuteChanged();
            NextPageCommand.RaiseCanExecuteChanged();
        }
        catch (PermissionDeniedException)
        {
            _dialog.ShowWarning(_localization.GetString("Msg_Auth_PermissionDenied"));
        }
        catch (Exception ex)
        {
            _dialog.ShowError(_localization.GetString("Msg_Cust_LoadError", ex.Message));
        }
    }

    private async Task RefreshAfterChangeAsync()
    {
        try
        {
            var summary = await _customers.GetSummaryAsync();
            TotalCustomers = summary.TotalCustomers;
            ResidentCount = summary.ResidentCount;
            ActiveTicketsCount = summary.ActiveTicketCount;
            ExpiringSoonCount = summary.ExpiringSoonCount;
            ActiveTicketRevenue = summary.ActiveTicketRevenue;
        }
        catch (Exception ex)
        {
            _dialog.ShowError(_localization.GetString("Msg_Cust_LoadError", ex.Message));
        }

        await SearchAsync(resetPage: false);
    }

    private void OnLanguageChanged()
    {
        foreach (var item in Customers)
        {
            item.RefreshTexts();
        }

        OnPropertyChanged(nameof(PageInfoText));
    }
}
