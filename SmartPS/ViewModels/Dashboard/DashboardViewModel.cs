using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
using SmartPS.Models.Navigation;
using SmartPS.Services.Auth;
using SmartPS.Services.Authorization;
using SmartPS.Services.Dialog;
using SmartPS.Services.Localization;
using SmartPS.ViewModels.Customers;
using SmartPS.ViewModels.GateControl;
using SmartPS.ViewModels.Incidents;
using SmartPS.ViewModels.Overview;
using SmartPS.ViewModels.ParkingMap;
using SmartPS.ViewModels.Pricing;
using SmartPS.ViewModels.Reports;
using SmartPS.ViewModels.Settings;
using SmartPS.ViewModels.Transactions;
using SmartPS.ViewModels.UserManagement;

namespace SmartPS.ViewModels.Dashboard;

public class DashboardViewModel : ViewModelBase
{
    private readonly IAuthService _authService;
    private readonly IPermissionService _permissionService;
    private readonly IDialogService _dialogService;
    private readonly ILocalizationService _localizationService;
    private readonly IServiceProvider _serviceProvider;

    // Cache các ViewModel con để giữ trạng thái
    private readonly Dictionary<NavigationItemType, ViewModelBase> _viewModelCache = new();

    public string CurrentLanguage => _localizationService.CurrentLanguage;

    // Thông tin người đăng nhập
    public string CurrentUserFullName => _authService.CurrentUser?.FullName ?? _localizationService.GetString("Str_Dash_DefaultAdminName");
    public string CurrentUserRole => _authService.CurrentUser?.Role?.RoleName ?? "Admin";

    // Danh sách mục menu trên Sidebar
    public ObservableCollection<NavigationMenuItem> NavItems { get; } = new();

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
            }
        }
    }

    // ViewModel của phân hệ đang được hiển thị trong ContentControl
    private ViewModelBase? _currentViewModel;
    public ViewModelBase? CurrentViewModel
    {
        get => _currentViewModel;
        set => SetProperty(ref _currentViewModel, value);
    }

    // Commands
    public RelayCommand<NavigationMenuItem> NavigateCommand { get; }
    public RelayCommand<string> SetLanguageCommand { get; }
    public RelayCommand LogoutCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }

    public event Action? LogoutRequested;

    public DashboardViewModel(
        IAuthService authService,
        IPermissionService permissionService,
        IDialogService dialogService,
        ILocalizationService localizationService,
        IServiceProvider serviceProvider)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _permissionService = permissionService ?? throw new ArgumentNullException(nameof(permissionService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

        _localizationService.LanguageChanged += () =>
        {
            void UpdateUiLanguage()
            {
                OnPropertyChanged(nameof(CurrentLanguage));
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
        SetLanguageCommand = new RelayCommand<string>(langCode =>
        {
            if (!string.IsNullOrEmpty(langCode))
            {
                _localizationService.SetLanguage(langCode);
            }
        });

        LogoutCommand = new RelayCommand(() =>
        {
            _authService.Logout();
            LogoutRequested?.Invoke();
        });

        RefreshCommand = new AsyncRelayCommand(async () =>
        {
            if (CurrentViewModel is UserManagementViewModel uvm)
            {
                await uvm.LoadDataAsync();
            }
        });

        InitializeNavigationMenu();
        ApplyRolePermissions();
    }

    private void InitializeNavigationMenu()
    {
        NavItems.Clear();

        // 1. NHÓM VẬN HÀNH (OPERATIONS)
        NavItems.Add(new NavigationMenuItem
        {
            Id = NavigationItemType.Overview,
            TitleKey = "Str_Menu_Overview",
            CategoryKey = "Str_Nav_Group_Operations",
            IconData = "M3 13h8V3H3v10zm0 8h8v-6H3v6zm10 0h8V11h-8v10zm0-18v6h8V3h-8z",
            AllowedRoles = new[] { "Admin", "Manager", "Operator" }
        });

        NavItems.Add(new NavigationMenuItem
        {
            Id = NavigationItemType.GateControl,
            TitleKey = "Str_Menu_GateControl",
            CategoryKey = "Str_Nav_Group_Operations",
            IconData = "M4 4h16v2H4V4zm0 4h16v2H4V8zm0 4h10v2H4v-2zm0 4h10v2H4v-2zm12 0h4v6h-4v-6zm-6 2H4v2h6v-2z",
            AllowedRoles = new[] { "Admin", "Manager", "Operator" }
        });

        NavItems.Add(new NavigationMenuItem
        {
            Id = NavigationItemType.ParkingMap,
            TitleKey = "Str_Menu_ParkingMap",
            CategoryKey = "Str_Nav_Group_Operations",
            IconData = "M20.5 3l-.16.03L15 5.1 9 3 3.36 4.9c-.21.07-.36.25-.36.48V20.5c0 .28.22.5.5.5l.16-.03L9 18.9l6 2.1 5.64-1.9c.21-.07.36-.25.36-.48V3.5c0-.28-.22-.5-.5-.5zM15 19l-6-2.11V5l6 2.11V19z",
            AllowedRoles = new[] { "Admin", "Manager", "Operator" }
        });

        NavItems.Add(new NavigationMenuItem
        {
            Id = NavigationItemType.Incidents,
            TitleKey = "Str_Menu_Incidents",
            CategoryKey = "Str_Nav_Group_Operations",
            IconData = "M1 21h22L12 2 1 21zm12-3h-2v-2h2v2zm0-4h-2v-4h2v4z",
            AllowedRoles = new[] { "Admin", "Manager", "Operator" }
        });

        // 2. NHÓM QUẢN LÝ DOANH NGHIỆP (MANAGEMENT)
        NavItems.Add(new NavigationMenuItem
        {
            Id = NavigationItemType.Customers,
            TitleKey = "Str_Menu_Customers",
            CategoryKey = "Str_Nav_Group_Management",
            IconData = "M16 11c1.66 0 2.99-1.34 2.99-3S17.66 5 16 5c-1.66 0-3 1.34-3 3s1.34 3 3 3zm-8 0c1.66 0 2.99-1.34 2.99-3S9.66 5 8 5C6.34 5 5 6.34 5 8s1.34 3 3 3zm0 2c-2.33 0-7 1.17-7 3.5V19h14v-2.5c0-2.33-4.67-3.5-7-3.5zm8 0c-.29 0-.62.02-.97.05 1.16.84 1.97 1.97 1.97 3.45V19h6v-2.5c0-2.33-4.67-3.5-7-3.5z",
            AllowedRoles = new[] { "Admin", "Manager" }
        });

        NavItems.Add(new NavigationMenuItem
        {
            Id = NavigationItemType.Reports,
            TitleKey = "Str_Menu_Reports",
            CategoryKey = "Str_Nav_Group_Management",
            IconData = "M19 3H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zM9 17H7v-7h2v7zm4 0h-2V7h2v10zm4 0h-2v-4h2v4z",
            AllowedRoles = new[] { "Admin", "Manager" }
        });

        NavItems.Add(new NavigationMenuItem
        {
            Id = NavigationItemType.Transactions,
            TitleKey = "Str_Menu_Transactions",
            CategoryKey = "Str_Nav_Group_Management",
            IconData = "M20 4H4c-1.11 0-1.99.89-1.99 2L2 18c0 1.11.89 2 2 2h16c1.11 0 2-.89 2-2V6c0-1.11-.89-2-2-2zm0 14H4v-6h16v6zm0-10H4V6h16v2z",
            AllowedRoles = new[] { "Admin", "Manager" }
        });

        NavItems.Add(new NavigationMenuItem
        {
            Id = NavigationItemType.Pricing,
            TitleKey = "Str_Menu_Pricing",
            CategoryKey = "Str_Nav_Group_Management",
            IconData = "M21.41 11.58l-9-9C12.05 2.22 11.55 2 11 2H4c-1.1 0-2 .9-2 2v7c0 .55.22 1.05.59 1.42l9 9c.36.36.86.58 1.41.58.55 0 1.05-.22 1.41-.59l7-7c.37-.36.59-.86.59-1.41 0-.55-.23-1.06-.59-1.42zM5.5 7C4.67 7 4 6.33 4 5.5S4.67 4 5.5 4 7 4.67 7 5.5 6.33 7 5.5 7z",
            AllowedRoles = new[] { "Admin", "Manager" }
        });

        // 3. NHÓM HỆ THỐNG & BẢO MẬT (SYSTEM)
        NavItems.Add(new NavigationMenuItem
        {
            Id = NavigationItemType.UserManagement,
            TitleKey = "Str_Menu_UserManagement",
            CategoryKey = "Str_Nav_Group_System",
            IconData = "M12 1L3 5v6c0 5.55 3.84 10.74 9 12 5.16-1.26 9-6.45 9-12V5l-9-4zm0 10.99h7c-.53 4.12-3.28 7.79-7 8.94V12H5V6.3l7-3.11v8.8z",
            AllowedRoles = new[] { "Admin" }
        });

        NavItems.Add(new NavigationMenuItem
        {
            Id = NavigationItemType.Settings,
            TitleKey = "Str_Menu_Settings",
            CategoryKey = "Str_Nav_Group_System",
            IconData = "M19.14 12.94c.04-.3.06-.61.06-.94 0-.32-.02-.64-.07-.94l2.03-1.58c.18-.14.23-.41.12-.61l-1.92-3.32c-.12-.22-.37-.29-.59-.22l-2.39.96c-.5-.38-1.03-.7-1.62-.94l-.36-2.54c-.04-.24-.24-.41-.48-.41h-3.84c-.24 0-.43.17-.47.41l-.36 2.54c-.59.24-1.13.57-1.62.94l-2.39-.96c-.22-.08-.47 0-.59.22L2.74 8.87c-.12.21-.08.47.12.61l2.03 1.58c-.05.3-.09.63-.09.94s.02.64.07.94l-2.03 1.58c-.18.14-.23.41-.12.61l1.92 3.32c.12.22.37.29.59.22l2.39-.96c.5.38 1.03.7 1.62.94l.36 2.54c.05.24.24.41.48.41h3.84c.24 0 .44-.17.47-.41l.36-2.54c.59-.24 1.13-.56 1.62-.94l2.39.96c.22.08.47 0 .59-.22l1.92-3.32c.12-.22.07-.47-.12-.61l-2.01-1.58zM12 15.6c-1.98 0-3.6-1.62-3.6-3.6s1.62-3.6 3.6-3.6 3.6 1.62 3.6 3.6-1.62 3.6-3.6 3.6z",
            AllowedRoles = new[] { "Admin" }
        });
    }

    public void ApplyRolePermissions()
    {
        var role = _authService.CurrentUser?.Role?.RoleName ?? "Admin";

        foreach (var item in NavItems)
        {
            item.IsVisible = item.AllowedRoles.Any(r => r.Equals(role, StringComparison.OrdinalIgnoreCase));
        }

        // Chọn phân hệ đầu tiên có quyền truy cập
        var firstVisible = NavItems.FirstOrDefault(i => i.IsVisible);
        if (firstVisible != null)
        {
            ExecuteNavigate(firstVisible);
        }
    }

    private void ExecuteNavigate(NavigationMenuItem? item)
    {
        if (item == null) return;

        var role = _authService.CurrentUser?.Role?.RoleName ?? "Admin";
        if (!item.AllowedRoles.Any(r => r.Equals(role, StringComparison.OrdinalIgnoreCase)))
        {
            _dialogService.ShowWarning(_localizationService.GetString("Msg_Nav_AccessDenied"));
            return;
        }

        SelectedNavItem = item;
        CurrentViewModel = ResolveViewModel(item.Id);
    }

    private ViewModelBase ResolveViewModel(NavigationItemType type)
    {
        if (_viewModelCache.TryGetValue(type, out var cachedVm))
        {
            return cachedVm;
        }

        ViewModelBase newVm = type switch
        {
            NavigationItemType.Overview => _serviceProvider.GetRequiredService<OverviewViewModel>(),
            NavigationItemType.GateControl => _serviceProvider.GetRequiredService<GateControlViewModel>(),
            NavigationItemType.ParkingMap => _serviceProvider.GetRequiredService<ParkingMapViewModel>(),
            NavigationItemType.Customers => _serviceProvider.GetRequiredService<CustomersViewModel>(),
            NavigationItemType.Reports => _serviceProvider.GetRequiredService<ReportsViewModel>(),
            NavigationItemType.Incidents => _serviceProvider.GetRequiredService<IncidentsViewModel>(),
            NavigationItemType.Transactions => _serviceProvider.GetRequiredService<TransactionsViewModel>(),
            NavigationItemType.UserManagement => _serviceProvider.GetRequiredService<UserManagementViewModel>(),
            NavigationItemType.Pricing => _serviceProvider.GetRequiredService<PricingViewModel>(),
            NavigationItemType.Settings => _serviceProvider.GetRequiredService<SettingsViewModel>(),
            _ => _serviceProvider.GetRequiredService<OverviewViewModel>()
        };

        _viewModelCache[type] = newVm;
        return newVm;
    }
}
