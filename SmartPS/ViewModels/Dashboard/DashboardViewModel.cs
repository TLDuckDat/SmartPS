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

        ShowShortcutsCommand = new RelayCommand(_ =>
            _dialogService.ShowInfo(_localizationService.GetString("Msg_Help_Shortcuts"), _localizationService.GetString("Str_MainMenu_Shortcuts")));
        AboutCommand = new RelayCommand(_ =>
            _dialogService.ShowInfo(_localizationService.GetString("Msg_Help_About", AppVersion), _localizationService.GetString("Str_MainMenu_About")));

        ApplyRolePermissions();
    }

    private void InitializeNavigationMenu()
    {
        NavItems.Clear();

        // 1. MENU NGHIỆP VỤ
        AddItem(NavigationItemType.GateControl, OperationsCategory, 1,
            "M4 4h16v2H4V4zm0 4h16v2H4V8zm0 4h10v2H4v-2zm0 4h10v2H4v-2zm12 0h4v6h-4v-6zm-6 2H4v2h6v-2z");
        AddItem(NavigationItemType.Shifts, OperationsCategory, 2,
            "M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20zm1 5v5l4 2-1 2-5-3V7h2z");
        AddItem(NavigationItemType.ParkingMap, OperationsCategory, 3,
            "M20.5 3l-.16.03L15 5.1 9 3 3.36 4.9c-.21.07-.36.25-.36.48V20.5c0 .28.22.5.5.5l.16-.03L9 18.9l6 2.1 5.64-1.9c.21-.07.36-.25.36-.48V3.5c0-.28-.22-.5-.5-.5zM15 19l-6-2.11V5l6 2.11V19z");
        AddItem(NavigationItemType.Incidents, OperationsCategory, 4,
            "M1 21h22L12 2 1 21zm12-3h-2v-2h2v2zm0-4h-2v-4h2v4z");

        // 2. MENU DANH MỤC
        AddItem(NavigationItemType.Customers, CatalogCategory, 5,
            "M16 11c1.66 0 2.99-1.34 2.99-3S17.66 5 16 5c-1.66 0-3 1.34-3 3s1.34 3 3 3zm-8 0c1.66 0 2.99-1.34 2.99-3S9.66 5 8 5C6.34 5 5 6.34 5 8s1.34 3 3 3zm0 2c-2.33 0-7 1.17-7 3.5V19h14v-2.5c0-2.33-4.67-3.5-7-3.5zm8 0c-.29 0-.62.02-.97.05 1.16.84 1.97 1.97 1.97 3.45V19h6v-2.5c0-2.33-4.67-3.5-7-3.5z");
        AddItem(NavigationItemType.Pricing, CatalogCategory, 6,
            "M21.41 11.58l-9-9C12.05 2.22 11.55 2 11 2H4c-1.1 0-2 .9-2 2v7c0 .55.22 1.05.59 1.42l9 9c.36.36.86.58 1.41.58.55 0 1.05-.22 1.41-.59l7-7c.37-.36.59-.86.59-1.41 0-.55-.23-1.06-.59-1.42zM5.5 7C4.67 7 4 6.33 4 5.5S4.67 4 5.5 4 7 4.67 7 5.5 6.33 7 5.5 7z");

        // 3. MENU BÁO CÁO & THỐNG KÊ
        AddItem(NavigationItemType.Overview, ReportsCategory, 7,
            "M3 13h8V3H3v10zm0 8h8v-6H3v6zm10 0h8V11h-8v10zm0-18v6h8V3h-8z");
        AddItem(NavigationItemType.Reports, ReportsCategory, 8,
            "M19 3H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zM9 17H7v-7h2v7zm4 0h-2V7h2v10zm4 0h-2v-4h2v4z");
        AddItem(NavigationItemType.Transactions, ReportsCategory, 9,
            "M20 4H4c-1.11 0-1.99.89-1.99 2L2 18c0 1.11.89 2 2 2h16c1.11 0 2-.89 2-2V6c0-1.11-.89-2-2-2zm0 14H4v-6h16v6zm0-10H4V6h16v2z");

        // 4. MENU QUẢN TRỊ
        AddItem(NavigationItemType.UserManagement, AdminCategory, null,
            "M12 1L3 5v6c0 5.55 3.84 10.74 9 12 5.16-1.26 9-6.45 9-12V5l-9-4zm0 10.99h7c-.53 4.12-3.28 7.79-7 8.94V12H5V6.3l7-3.11v8.8z");
        AddItem(NavigationItemType.RolePermissions, AdminCategory, null,
            "M12.65 10C11.83 7.67 9.61 6 7 6c-3.31 0-6 2.69-6 6s2.69 6 6 6c2.61 0 4.83-1.67 5.65-4H17v4h4v-4h2v-4H12.65zM7 14c-1.1 0-2-.9-2-2s.9-2 2-2 2 .9 2 2-.9 2-2 2z");
        AddItem(NavigationItemType.AuditLog, AdminCategory, null,
            "M19 3H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zM9 17H7v-2h2v2zm0-4H7v-2h2v2zm0-4H7V7h2v2zm8 8h-6v-2h6v2zm0-4h-6v-2h6v2zm0-4h-6V7h6v2z");
        AddItem(NavigationItemType.Settings, AdminCategory, null,
            "M19.14 12.94c.04-.3.06-.61.06-.94 0-.32-.02-.64-.07-.94l2.03-1.58c.18-.14.23-.41.12-.61l-1.92-3.32c-.12-.22-.37-.29-.59-.22l-2.39.96c-.5-.38-1.03-.7-1.62-.94l-.36-2.54c-.04-.24-.24-.41-.48-.41h-3.84c-.24 0-.43.17-.47.41l-.36 2.54c-.59.24-1.13.57-1.62.94l-2.39-.96c-.22-.08-.47 0-.59.22L2.74 8.87c-.12.21-.08.47.12.61l2.03 1.58c-.05.3-.09.63-.09.94s.02.64.07.94l-2.03 1.58c-.18.14-.23.41-.12.61l1.92 3.32c.12.22.37.29.59.22l2.39-.96c.5.38 1.03.7 1.62.94l.36 2.54c.05.24.24.41.48.41h3.84c.24 0 .44-.17.47-.41l.36-2.54c.59-.24 1.13-.56 1.62-.94l2.39.96c.22.08.47 0 .59-.22l1.92-3.32c.12-.22.07-.47-.12-.61l-2.01-1.58zM12 15.6c-1.98 0-3.6-1.62-3.6-3.6s1.62-3.6 3.6-3.6 3.6 1.62 3.6 3.6-1.62 3.6-3.6 3.6z");
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
