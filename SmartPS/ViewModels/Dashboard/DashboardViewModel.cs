using System.Collections.ObjectModel;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using SmartPS.Models.Navigation;
using SmartPS.Services.Audit;
using SmartPS.Services.Auth;
using SmartPS.Services.Authorization;
using SmartPS.Services.Dialog;
using SmartPS.Services.Localization;
using SmartPS.ViewModels.Audit;
using SmartPS.ViewModels.Customers;
using SmartPS.ViewModels.GateControl;
using SmartPS.ViewModels.Incidents;
using SmartPS.ViewModels.Overview;
using SmartPS.ViewModels.ParkingMap;
using SmartPS.ViewModels.Pricing;
using SmartPS.ViewModels.Reports;
using SmartPS.ViewModels.RolePermissions;
using SmartPS.ViewModels.Settings;
using SmartPS.ViewModels.Transactions;
using SmartPS.ViewModels.Shifts;
using SmartPS.ViewModels.UserManagement;

namespace SmartPS.ViewModels.Dashboard;

/// <summary>
/// Cửa sổ chính kiểu ứng dụng desktop: Menu + ToolBar + TabControl (mỗi phân hệ một thẻ) + StatusBar.
/// Menu và thanh công cụ chỉ hiện các mục người dùng có quyền (NavigationAccessPolicy).
/// </summary>
public class DashboardViewModel : ViewModelBase
{
    public const string OperationsCategory = "Str_MainMenu_Operations";
    public const string CatalogCategory = "Str_MainMenu_Catalog";
    public const string ReportsCategory = "Str_MainMenu_Reports";
    public const string AdminCategory = "Str_MainMenu_Admin";

    private static readonly NavigationItemType[] ToolbarItemIds =
    {
        NavigationItemType.Overview, NavigationItemType.GateControl, NavigationItemType.Shifts,
        NavigationItemType.Incidents, NavigationItemType.Customers, NavigationItemType.Reports
    };

    private readonly IAuthService _authService;
    private readonly IPermissionService _permissionService;
    private readonly IDialogService _dialogService;
    private readonly ILocalizationService _localizationService;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IAuditService _auditService;
    private readonly IServiceProvider _serviceProvider;

    // Cache các ViewModel con để giữ trạng thái khi đóng rồi mở lại thẻ
    private readonly Dictionary<NavigationItemType, ViewModelBase> _viewModelCache = new();

    /// <summary>Tạo ViewModel cho từng phân hệ; test thay bằng stub để không cần dựng service thật.</summary>
    internal Func<NavigationItemType, ViewModelBase>? ViewModelFactory { get; set; }

    public string CurrentLanguage => _localizationService.CurrentLanguage;
    public bool IsLanguageVi => CurrentLanguage == "vi-VN";
    public bool IsLanguageEn => CurrentLanguage == "en-US";
    public bool IsLanguageJa => CurrentLanguage == "ja-JP";

    // Thông tin người đăng nhập
    public string CurrentUserFullName => _authService.CurrentUser?.FullName ?? string.Empty;
    public string CurrentUserRole => _authService.CurrentUser?.Role?.RoleName ?? string.Empty;

    public string AppVersion { get; } = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0";

    // Toàn bộ phân hệ, theo thứ tự trên thanh Menu
    public ObservableCollection<NavigationMenuItem> NavItems { get; } = new();

    // Các menu cấp 1 lấy từ NavItems theo nhóm
    public NavigationMenuGroup OperationsMenu { get; }
    public NavigationMenuGroup CatalogMenu { get; }
    public NavigationMenuGroup ReportsMenu { get; }
    public NavigationMenuGroup AdminMenu { get; }

    // Các nút phân hệ dùng nhiều trên ToolBar
    public IReadOnlyList<NavigationMenuItem> ToolbarItems { get; }

    // Các thẻ đang mở trong vùng làm việc
    public ObservableCollection<TabItemViewModel> OpenTabs { get; } = new();

    private TabItemViewModel? _selectedTab;
    public TabItemViewModel? SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (SetProperty(ref _selectedTab, value))
            {
                SelectedNavItem = value?.Item;
                CurrentViewModel = value?.Content;
            }
        }
    }

    public bool HasOpenTabs => OpenTabs.Count > 0;

    private NavigationMenuItem? _selectedNavItem;
    public NavigationMenuItem? SelectedNavItem
    {
        get => _selectedNavItem;
        set
        {
            if (SetProperty(ref _selectedNavItem, value))
            {
                foreach (var item in NavItems)
                {
                    item.IsSelected = (item == value);
                }

                OnPropertyChanged(nameof(CurrentScreenTitle));
            }
        }
    }

    public string CurrentScreenTitle => SelectedNavItem?.Title ?? string.Empty;

    // ViewModel của thẻ đang được chọn
    private ViewModelBase? _currentViewModel;
    public ViewModelBase? CurrentViewModel
    {
        get => _currentViewModel;
        set => SetProperty(ref _currentViewModel, value);
    }

    // Commands
    public RelayCommand<NavigationMenuItem> NavigateCommand { get; }
    public RelayCommand<string> OpenByShortcutCommand { get; }
    public RelayCommand<TabItemViewModel> CloseTabCommand { get; }
    public RelayCommand CloseAllTabsCommand { get; }
    public RelayCommand<string> SetLanguageCommand { get; }
    public AsyncRelayCommand LogoutCommand { get; }
    public AsyncRelayCommand ExitCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand ShowProfileCommand { get; }
    public RelayCommand ShowShortcutsCommand { get; }
    public RelayCommand AboutCommand { get; }

    public event Action? LogoutRequested;
    public event Action? ExitRequested;

    public DashboardViewModel(
        IAuthService authService,
        IPermissionService permissionService,
        IDialogService dialogService,
        ILocalizationService localizationService,
        ICurrentUserContext currentUserContext,
        IAuditService auditService,
        IServiceProvider serviceProvider)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _permissionService = permissionService ?? throw new ArgumentNullException(nameof(permissionService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _currentUserContext = currentUserContext ?? throw new ArgumentNullException(nameof(currentUserContext));
        _auditService = auditService ?? throw new ArgumentNullException(nameof(auditService));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

        InitializeNavigationMenu();
        OperationsMenu = new NavigationMenuGroup(OperationsCategory, NavItems);
        CatalogMenu = new NavigationMenuGroup(CatalogCategory, NavItems);
        ReportsMenu = new NavigationMenuGroup(ReportsCategory, NavItems);
        AdminMenu = new NavigationMenuGroup(AdminCategory, NavItems);
        ToolbarItems = ToolbarItemIds.Select(id => NavItems.Single(i => i.Id == id)).ToList();
        UpdateTitles();

        OpenTabs.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasOpenTabs));

        // Quyền của người dùng hiện tại thay đổi (đăng nhập lại, lưu ma trận quyền, tự sửa tài khoản): chỉ tính lại hiển thị menu
        _currentUserContext.Changed += OnCurrentUserChanged;

        _localizationService.LanguageChanged += () =>
        {
            void UpdateUiLanguage()
            {
                UpdateTitles();
                OnPropertyChanged(nameof(CurrentLanguage));
                OnPropertyChanged(nameof(IsLanguageVi));
                OnPropertyChanged(nameof(IsLanguageEn));
                OnPropertyChanged(nameof(IsLanguageJa));
                OnPropertyChanged(nameof(CurrentUserFullName));
                OnPropertyChanged(nameof(CurrentUserRole));
            }

            if (System.Windows.Application.Current?.Dispatcher.CheckAccess() == false)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(UpdateUiLanguage);
            }
            else
            {
                UpdateUiLanguage();
            }
        };

        NavigateCommand = new RelayCommand<NavigationMenuItem>(ExecuteNavigate);
        OpenByShortcutCommand = new RelayCommand<string>(OpenByShortcut);
        CloseTabCommand = new RelayCommand<TabItemViewModel>(CloseTab, tab => (tab ?? SelectedTab) != null);
        CloseAllTabsCommand = new RelayCommand(_ => CloseAllTabs(), _ => HasOpenTabs);
        SetLanguageCommand = new RelayCommand<string>(langCode =>
        {
            if (!string.IsNullOrEmpty(langCode))
            {
                _localizationService.SetLanguage(langCode);
            }
        });

        LogoutCommand = new AsyncRelayCommand(async () =>
        {
            await _authService.LogoutAsync();
            _currentUserContext.Changed -= OnCurrentUserChanged;
            LogoutRequested?.Invoke();
        });

        ExitCommand = new AsyncRelayCommand(async () =>
        {
            if (!_dialogService.ShowConfirm(_localizationService.GetString("Msg_MainMenu_ExitConfirm"))) return;

            await _authService.LogoutAsync();
            _currentUserContext.Changed -= OnCurrentUserChanged;
            ExitRequested?.Invoke();
        });

        RefreshCommand = new AsyncRelayCommand(async () =>
        {
            if (CurrentViewModel != null)
            {
                await LoadContentAsync(CurrentViewModel, isRefresh: true);
            }
        });

        ShowProfileCommand = new AsyncRelayCommand(async () =>
        {
            await _dialogService.ShowUserProfileDialogAsync();
        });

        ShowShortcutsCommand = new RelayCommand(_ =>
            _dialogService.ShowInfo(_localizationService.GetString("Msg_Help_Shortcuts"), _localizationService.GetString("Str_MainMenu_Shortcuts")));
        AboutCommand = new RelayCommand(_ =>
            _dialogService.ShowInfo(_localizationService.GetString("Msg_Help_About", AppVersion), _localizationService.GetString("Str_MainMenu_About")));

        ApplyRolePermissions();
    }

    private void InitializeNavigationMenu()
    {
        NavItems.Clear();

        // 1. MENU NGHIỆP VỤ (Bộ icon SVG tự vẽ nét mảnh đồng bộ hddat-portfolio)
        AddItem(NavigationItemType.GateControl, OperationsCategory, 1,
            "M4,20 V9 A2,2 0 0,1 6,7 H8 A2,2 0 0,1 10,9 V20 M3,20 H11 M10,11 L20,6 M10,13.5 L20,8.5 M18,14 V20 M16,20 H20");
        AddItem(NavigationItemType.Shifts, OperationsCategory, 2,
            "M12,3.5 A8.5,8.5 0 1,1 11.99,3.5 M12,7.5 V12 L15.5,14");
        AddItem(NavigationItemType.ParkingMap, OperationsCategory, 3,
            "M3.5,6.5 L9,4 L15,6.5 L20.5,4 V17.5 L15,20 L9,17.5 L3.5,20 Z M9,4 V17.5 M15,6.5 V20");
        AddItem(NavigationItemType.Incidents, OperationsCategory, 4,
            "M10.3,4.8 L3.4,17 A2,2 0 0,0 5.1,20 H18.9 A2,2 0 0,0 20.6,17 L13.7,4.8 A2,2 0 0,0 10.3,4.8 Z M12,9.5 V13.5 M12,16.8 H12.01");

        // 2. MENU DANH MỤC
        AddItem(NavigationItemType.Customers, CatalogCategory, 5,
            "M9,11 A3.5,3.5 0 1,0 9,4 A3.5,3.5 0 1,0 9,11 Z M3.5,19.5 C3.5,16.2 5.9,14 9,14 C12.1,14 14.5,16.2 14.5,19.5 M15.5,4.3 A3.3,3.3 0 0,1 15.5,10.7 M17,14.2 C19.2,14.8 20.5,16.6 20.5,19.5");
        AddItem(NavigationItemType.Pricing, CatalogCategory, 6,
            "M3.5,10.8 V5.5 A2,2 0 0,1 5.5,3.5 H10.8 A2,2 0 0,1 12.2,4.1 L19.9,11.8 A2,2 0 0,1 19.9,14.6 L14.6,19.9 A2,2 0 0,1 11.8,19.9 L4.1,12.2 A2,2 0 0,1 3.5,10.8 Z M7.5,7.5 H7.52");

        // 3. MENU BÁO CÁO & THỐNG KÊ
        AddItem(NavigationItemType.Overview, ReportsCategory, 7,
            "M4,5.5 A1.5,1.5 0 0,1 5.5,4 H9.5 A1.5,1.5 0 0,1 11,5.5 V9.5 A1.5,1.5 0 0,1 9.5,11 H5.5 A1.5,1.5 0 0,1 4,9.5 Z M13,5.5 A1.5,1.5 0 0,1 14.5,4 H18.5 A1.5,1.5 0 0,1 20,5.5 V9.5 A1.5,1.5 0 0,1 18.5,11 H14.5 A1.5,1.5 0 0,1 13,9.5 Z M4,14.5 A1.5,1.5 0 0,1 5.5,13 H9.5 A1.5,1.5 0 0,1 11,14.5 V18.5 A1.5,1.5 0 0,1 9.5,20 H5.5 A1.5,1.5 0 0,1 4,18.5 Z M13,14.5 A1.5,1.5 0 0,1 14.5,13 H18.5 A1.5,1.5 0 0,1 20,14.5 V18.5 A1.5,1.5 0 0,1 18.5,20 H14.5 A1.5,1.5 0 0,1 13,18.5 Z");
        AddItem(NavigationItemType.Reports, ReportsCategory, 8,
            "M4,20 H20 M7,20 V13 M12,20 V9 M17,20 V5 M5.5,10 L10.5,6.5 L14,8.5 L18.5,4");
        AddItem(NavigationItemType.Transactions, ReportsCategory, 9,
            "M3,7.5 A2.5,2.5 0 0,1 5.5,5 H18.5 A2.5,2.5 0 0,1 21,7.5 V16.5 A2.5,2.5 0 0,1 18.5,19 H5.5 A2.5,2.5 0 0,1 3,16.5 Z M3,9.5 H21 M7,14.5 H11");

        // 4. MENU QUẢN TRỊ
        AddItem(NavigationItemType.UserManagement, AdminCategory, null,
            "M12,3 L4.5,6.2 V11.5 C4.5,16.2 7.7,19.8 12,21 C16.3,19.8 19.5,16.2 19.5,11.5 V6.2 Z M12,11.5 A2.2,2.2 0 1,0 12,7.1 A2.2,2.2 0 1,0 12,11.5 Z M8.5,16.2 C9.3,14.6 10.5,13.8 12,13.8 C13.5,13.8 14.7,14.6 15.5,16.2");
        AddItem(NavigationItemType.RolePermissions, AdminCategory, null,
            "M7.5,15.5 A4,4 0 1,1 11.2,10 H20.5 V13.5 H17.5 V16 H14.5 V13.5 H11.2 A4,4 0 0,1 7.5,15.5 Z M7.5,11.5 H7.52");
        AddItem(NavigationItemType.AuditLog, AdminCategory, null,
            "M6,3.5 H15.5 A2,2 0 0,1 17.5,5.5 V18.5 A2,2 0 0,1 15.5,20.5 H6 A2,2 0 0,1 4,18.5 V5.5 A2,2 0 0,1 6,3.5 Z M8,8 H13.5 M8,12 H13.5 M8,16 H11 M15,15.5 L17,17.5 L20.5,14");
        AddItem(NavigationItemType.Settings, AdminCategory, null,
            "M4,7 H10 M14,7 H20 M12,7 A2,2 0 1,0 12,7.01 M4,12 H14 M18,12 H20 M16,12 A2,2 0 1,0 16,12.01 M4,17 H6 M10,17 H20 M8,17 A2,2 0 1,0 8,17.01");
    }

    private void AddItem(NavigationItemType id, string categoryKey, int? shortcutDigit, string iconData)
    {
        var item = new NavigationMenuItem
        {
            Id = id,
            TitleKey = $"Str_Menu_{id}",
            CategoryKey = categoryKey,
            ShortcutDigit = shortcutDigit,
            IconData = iconData,
            RequiredPermissions = NavigationAccessPolicy.GetRequiredPermissions(id)
        };
        item.OpenCommand = new RelayCommand(_ => ExecuteNavigate(item));
        NavItems.Add(item);
    }

    private void UpdateTitles()
    {
        foreach (var item in NavItems)
        {
            item.Title = _localizationService.GetString(item.TitleKey);
        }

        OnPropertyChanged(nameof(CurrentScreenTitle));
    }

    private void OnCurrentUserChanged(object? sender, EventArgs e)
    {
        if (System.Windows.Application.Current?.Dispatcher.CheckAccess() == false)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(RefreshNavigationVisibility);
        }
        else
        {
            RefreshNavigationVisibility();
        }
    }

    private void RefreshNavigationVisibility()
    {
        foreach (var item in NavItems)
        {
            item.IsVisible = NavigationAccessPolicy.CanAccess(item.Id, _permissionService);
        }

        OperationsMenu.RefreshVisibility();
        CatalogMenu.RefreshVisibility();
        ReportsMenu.RefreshVisibility();
        AdminMenu.RefreshVisibility();

        OnPropertyChanged(nameof(CurrentUserFullName));
        OnPropertyChanged(nameof(CurrentUserRole));

        // Thẻ của phân hệ không còn quyền: đóng thẻ, không tự mở phân hệ khác
        foreach (var tab in OpenTabs.Where(t => !t.Item.IsVisible).ToList())
        {
            CloseTab(tab);
        }
    }

    public void ApplyRolePermissions()
    {
        RefreshNavigationVisibility();

        // Màn hình đầu tiên: Tổng quan nếu được phép, nếu không thì phân hệ đầu tiên có quyền
        var home = NavItems.FirstOrDefault(i => i.Id == NavigationItemType.Overview && i.IsVisible)
                   ?? NavItems.FirstOrDefault(i => i.IsVisible);
        if (home != null)
        {
            ExecuteNavigate(home);
        }
    }

    private void ExecuteNavigate(NavigationMenuItem? item)
    {
        if (item == null) return;

        if (!NavigationAccessPolicy.CanAccess(item.Id, _permissionService))
        {
            _dialogService.ShowWarning(_localizationService.GetString("Msg_Nav_AccessDenied"));
            _ = LogNavigationDeniedAsync(item);
            return;
        }

        // Phân hệ đã mở thì chuyển sang thẻ đó, chưa mở thì thêm thẻ mới
        var tab = OpenTabs.FirstOrDefault(t => t.Item == item);
        if (tab == null)
        {
            tab = new TabItemViewModel(item, ResolveViewModel(item.Id));
            OpenTabs.Add(tab);
        }

        SelectedTab = tab;
        _ = LoadContentAsync(tab.Content, isRefresh: false);
    }

    private void OpenByShortcut(string? digitText)
    {
        if (!int.TryParse(digitText, out var digit)) return;

        // Phím tắt của phân hệ không có quyền thì bỏ qua (mục đó cũng không hiện trên menu)
        var item = NavItems.FirstOrDefault(i => i.ShortcutDigit == digit);
        if (item is { IsVisible: true })
        {
            ExecuteNavigate(item);
        }
    }

    private void CloseTab(TabItemViewModel? tab)
    {
        tab ??= SelectedTab;
        if (tab == null) return;

        var index = OpenTabs.IndexOf(tab);
        if (index < 0) return;

        var wasSelected = SelectedTab == tab;
        OpenTabs.RemoveAt(index);

        if (wasSelected)
        {
            SelectedTab = OpenTabs.Count == 0 ? null : OpenTabs[Math.Min(index, OpenTabs.Count - 1)];
        }
    }

    private void CloseAllTabs()
    {
        SelectedTab = null;
        OpenTabs.Clear();
    }

    /// <summary>Nạp dữ liệu cho phân hệ. Cài đặt tự nạp khi tạo nên chỉ nạp lại khi bấm Làm mới.</summary>
    private static Task LoadContentAsync(ViewModelBase content, bool isRefresh)
    {
        return content switch
        {
            OverviewViewModel ovm => ovm.LoadDataAsync(),
            ParkingMapViewModel pvm => pvm.LoadDataAsync(),
            TransactionsViewModel tvm => tvm.LoadDataAsync(),
            ShiftsViewModel svm => svm.LoadDataAsync(),
            CustomersViewModel cvm => cvm.LoadDataAsync(),
            ReportsViewModel rvm => rvm.LoadDataAsync(),
            PricingViewModel prvm => prvm.LoadDataAsync(),
            IncidentsViewModel ivm => ivm.LoadDataAsync(),
            UserManagementViewModel uvm => uvm.LoadDataAsync(),
            RolePermissionsViewModel rpvm => rpvm.LoadDataAsync(),
            AuditLogViewModel avm => avm.LoadDataAsync(),
            SettingsViewModel setvm when isRefresh => setvm.LoadDataAsync(),
            _ => Task.CompletedTask
        };
    }

    private async Task LogNavigationDeniedAsync(NavigationMenuItem item)
    {
        try
        {
            await _auditService.LogAsync(AuditEntry.AccessDenied(
                item.RequiredPermissions,
                _permissionService.IsAuthenticated ? "MissingPermission" : "NotAuthenticated",
                "Navigation",
                item.Id.ToString()));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Dashboard] Could not audit denied navigation: {ex.Message}");
        }
    }

    private ViewModelBase ResolveViewModel(NavigationItemType type)
    {
        if (_viewModelCache.TryGetValue(type, out var cachedVm))
        {
            return cachedVm;
        }

        ViewModelBase newVm = ViewModelFactory?.Invoke(type) ?? type switch
        {
            NavigationItemType.Overview => _serviceProvider.GetRequiredService<OverviewViewModel>(),
            NavigationItemType.GateControl => _serviceProvider.GetRequiredService<GateControlViewModel>(),
            NavigationItemType.ParkingMap => _serviceProvider.GetRequiredService<ParkingMapViewModel>(),
            NavigationItemType.Customers => _serviceProvider.GetRequiredService<CustomersViewModel>(),
            NavigationItemType.Reports => _serviceProvider.GetRequiredService<ReportsViewModel>(),
            NavigationItemType.Incidents => _serviceProvider.GetRequiredService<IncidentsViewModel>(),
            NavigationItemType.Transactions => _serviceProvider.GetRequiredService<TransactionsViewModel>(),
            NavigationItemType.Shifts => _serviceProvider.GetRequiredService<ShiftsViewModel>(),
            NavigationItemType.UserManagement => _serviceProvider.GetRequiredService<UserManagementViewModel>(),
            NavigationItemType.Pricing => _serviceProvider.GetRequiredService<PricingViewModel>(),
            NavigationItemType.RolePermissions => _serviceProvider.GetRequiredService<RolePermissionsViewModel>(),
            NavigationItemType.AuditLog => _serviceProvider.GetRequiredService<AuditLogViewModel>(),
            NavigationItemType.Settings => _serviceProvider.GetRequiredService<SettingsViewModel>(),
            _ => _serviceProvider.GetRequiredService<OverviewViewModel>()
        };

        _viewModelCache[type] = newVm;
        return newVm;
    }
}
