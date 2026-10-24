using Microsoft.Extensions.DependencyInjection;
using SmartPS.Models.Navigation;
using SmartPS.Services.Auth;
using SmartPS.Services.Dialog;
using SmartPS.Services.Localization;
using SmartPS.ViewModels;
using SmartPS.ViewModels.Dashboard;

namespace SmartPS.Tests.Unit;

/// <summary>
/// Desktop main window: Menu groups, ToolBar, one tab per module (TabControl) and Ctrl+1…9 shortcuts.
/// Child view models are replaced by stubs through DashboardViewModel.ViewModelFactory.
/// </summary>
public class DashboardTabsTests
{
    private sealed class StubViewModel : ViewModelBase
    {
        public StubViewModel(NavigationItemType type) => Type = type;
        public NavigationItemType Type { get; }
    }

    private sealed class Harness
    {
        public required CurrentUserContext Context { get; init; }
        public required FakeAuditService Audit { get; init; }
        public required FakeDialogService Dialog { get; init; }
        public required DashboardViewModel ViewModel { get; init; }
        public List<NavigationItemType> Created { get; } = new();

        public NavigationMenuItem Item(NavigationItemType id) => ViewModel.NavItems.Single(i => i.Id == id);

        public void Open(NavigationItemType id) => ViewModel.NavigateCommand.Execute(Item(id));
    }

    private static Harness Create()
    {
        var context = new CurrentUserContext();
        var audit = new FakeAuditService();
        var dialog = new FakeDialogService();

        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUserContext>(context);
        services.AddSingleton<IPermissionService, PermissionService>();
        services.AddSingleton<IAuthService, FakeAuthService>();
        services.AddSingleton<IDialogService>(dialog);
        services.AddSingleton<ILocalizationService, FakeLocalizationService>();
        services.AddSingleton<IAuditService>(audit);
        var provider = services.BuildServiceProvider();

        var vm = ActivatorUtilities.CreateInstance<DashboardViewModel>(provider);
        var harness = new Harness { Context = context, Audit = audit, Dialog = dialog, ViewModel = vm };
        vm.ViewModelFactory = type =>
        {
            harness.Created.Add(type);
            return new StubViewModel(type);
        };
        return harness;
    }

    [Fact]
    public void Opening_a_module_adds_one_tab_and_selects_it()
    {
        var h = Create();
        h.Context.SetUser(TestUsers.Operator());

        h.Open(NavigationItemType.GateControl);

        var tab = Assert.Single(h.ViewModel.OpenTabs);
        Assert.Same(tab, h.ViewModel.SelectedTab);
        Assert.Same(h.Item(NavigationItemType.GateControl), h.ViewModel.SelectedNavItem);
        Assert.True(h.Item(NavigationItemType.GateControl).IsSelected);
        Assert.Same(tab.Content, h.ViewModel.CurrentViewModel);
        Assert.True(h.ViewModel.HasOpenTabs);
    }

    [Fact]
    public void Opening_an_open_module_again_switches_to_its_tab_without_duplicating_it()
    {
        var h = Create();
        h.Context.SetUser(TestUsers.Operator());
        h.Open(NavigationItemType.GateControl);
        h.Open(NavigationItemType.Shifts);

        h.Open(NavigationItemType.GateControl);

        Assert.Equal(2, h.ViewModel.OpenTabs.Count);
        Assert.Equal(NavigationItemType.GateControl, h.ViewModel.SelectedTab!.Item.Id);
        Assert.Equal(new[] { NavigationItemType.GateControl, NavigationItemType.Shifts }, h.Created);
    }

    [Fact]
    public void Closing_the_selected_tab_selects_its_neighbour_and_closing_the_last_clears_the_content()
    {
        var h = Create();
        h.Context.SetUser(TestUsers.Operator());
        h.Open(NavigationItemType.GateControl);
        h.Open(NavigationItemType.Shifts);
        h.Open(NavigationItemType.Incidents);
        h.Open(NavigationItemType.Shifts);

        h.ViewModel.CloseTabCommand.Execute(null);

        Assert.Equal(new[] { NavigationItemType.GateControl, NavigationItemType.Incidents }, h.ViewModel.OpenTabs.Select(t => t.Item.Id));
        Assert.Equal(NavigationItemType.Incidents, h.ViewModel.SelectedTab!.Item.Id);
        Assert.False(h.Item(NavigationItemType.Shifts).IsSelected);

        h.ViewModel.CloseAllTabsCommand.Execute(null);

        Assert.Empty(h.ViewModel.OpenTabs);
        Assert.Null(h.ViewModel.SelectedTab);
        Assert.Null(h.ViewModel.SelectedNavItem);
        Assert.Null(h.ViewModel.CurrentViewModel);
        Assert.False(h.ViewModel.HasOpenTabs);
    }

    [Fact]
    public void Closing_a_background_tab_keeps_the_selected_one()
    {
        var h = Create();
        h.Context.SetUser(TestUsers.Operator());
        h.Open(NavigationItemType.GateControl);
        h.Open(NavigationItemType.Shifts);
        var background = h.ViewModel.OpenTabs[0];

        h.ViewModel.CloseTabCommand.Execute(background);

        Assert.Equal(NavigationItemType.Shifts, Assert.Single(h.ViewModel.OpenTabs).Item.Id);
        Assert.Equal(NavigationItemType.Shifts, h.ViewModel.SelectedTab!.Item.Id);
    }

    [Fact]
    public void Reopening_a_closed_module_reuses_its_view_model()
    {
        var h = Create();
        h.Context.SetUser(TestUsers.Operator());
        h.Open(NavigationItemType.GateControl);
        var first = h.ViewModel.CurrentViewModel;
        h.ViewModel.CloseTabCommand.Execute(null);

        h.Open(NavigationItemType.GateControl);

        Assert.Same(first, h.ViewModel.CurrentViewModel);
        Assert.Single(h.Created);
    }

    [Fact]
    public void Losing_a_permission_closes_the_tabs_it_unlocked()
    {
        var h = Create();
        h.Context.SetUser(TestUsers.Operator());
        h.Open(NavigationItemType.GateControl);
        h.Open(NavigationItemType.Reports);

        h.Context.SetUser(TestUsers.Build("Operator", TestUsers.OperatorSeedPermissions.Where(p => p != Permissions.ReportView).ToArray()));

        Assert.Equal(NavigationItemType.GateControl, Assert.Single(h.ViewModel.OpenTabs).Item.Id);
        Assert.Equal(NavigationItemType.GateControl, h.ViewModel.SelectedTab!.Item.Id);
    }

    [Fact]
    public void Logging_out_the_user_closes_every_tab()
    {
        var h = Create();
        h.Context.SetUser(TestUsers.Operator());
        h.Open(NavigationItemType.GateControl);

        h.Context.SetUser(null);

        Assert.Empty(h.ViewModel.OpenTabs);
        Assert.Null(h.ViewModel.CurrentViewModel);
    }

    [Fact]
    public void Ctrl_digit_opens_the_module_with_that_shortcut()
    {
        var h = Create();
        h.Context.SetUser(TestUsers.Operator());

        h.ViewModel.OpenByShortcutCommand.Execute("1");

        Assert.Equal(NavigationItemType.GateControl, h.ViewModel.SelectedTab!.Item.Id);
    }

    [Fact]
    public void Ctrl_digit_for_a_module_without_permission_is_ignored_without_warning_or_audit()
    {
        var h = Create();
        h.Context.SetUser(TestUsers.Operator());
        Assert.False(h.Item(NavigationItemType.Pricing).IsVisible);

        h.ViewModel.OpenByShortcutCommand.Execute(h.Item(NavigationItemType.Pricing).ShortcutDigit!.Value.ToString());

        Assert.Empty(h.ViewModel.OpenTabs);
        Assert.Empty(h.Dialog.Warnings);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public void Shortcuts_one_to_nine_are_unique_and_shown_in_the_menu()
    {
        var h = Create();

        var digits = h.ViewModel.NavItems.Where(i => i.ShortcutDigit != null).Select(i => i.ShortcutDigit!.Value).ToList();

        Assert.Equal(Enumerable.Range(1, 9), digits.OrderBy(d => d));
        Assert.All(h.ViewModel.NavItems.Where(i => i.ShortcutDigit != null),
            i => Assert.Equal($"Ctrl+{i.ShortcutDigit}", i.ShortcutText));
    }

    [Fact]
    public void Every_module_belongs_to_exactly_one_top_level_menu()
    {
        var h = Create();
        var vm = h.ViewModel;

        var grouped = new[] { vm.OperationsMenu, vm.CatalogMenu, vm.ReportsMenu, vm.AdminMenu }.SelectMany(g => g.Items).ToList();

        Assert.Equal(vm.NavItems.Count, grouped.Count);
        Assert.Equal(vm.NavItems.ToHashSet(), grouped.ToHashSet());
        Assert.Contains(vm.AdminMenu.Items, i => i.Id == NavigationItemType.AuditLog);
        Assert.Contains(vm.ReportsMenu.Items, i => i.Id == NavigationItemType.Overview);
    }

    [Fact]
    public void Operator_and_admin_see_different_top_level_menus()
    {
        var h = Create();

        h.Context.SetUser(TestUsers.Operator());
        Assert.True(h.ViewModel.OperationsMenu.IsVisible);
        Assert.True(h.ViewModel.ReportsMenu.IsVisible);
        Assert.False(h.ViewModel.CatalogMenu.IsVisible);
        Assert.False(h.ViewModel.AdminMenu.IsVisible);

        h.Context.SetUser(TestUsers.AdminWithoutGrants());
        Assert.True(h.ViewModel.CatalogMenu.IsVisible);
        Assert.True(h.ViewModel.AdminMenu.IsVisible);
    }

    [Fact]
    public void Toolbar_buttons_are_menu_items_and_follow_their_visibility()
    {
        var h = Create();
        h.Context.SetUser(TestUsers.Operator());

        Assert.All(h.ViewModel.ToolbarItems, i => Assert.Contains(i, h.ViewModel.NavItems));
        Assert.False(h.ViewModel.ToolbarItems.Single(i => i.Id == NavigationItemType.Customers).IsVisible);
        Assert.True(h.ViewModel.ToolbarItems.Single(i => i.Id == NavigationItemType.GateControl).IsVisible);
    }

    [Fact]
    public void Menu_items_open_through_their_own_command()
    {
        var h = Create();
        h.Context.SetUser(TestUsers.Operator());

        h.Item(NavigationItemType.Incidents).OpenCommand!.Execute(null);

        Assert.Equal(NavigationItemType.Incidents, h.ViewModel.SelectedTab!.Item.Id);
    }

    [Fact]
    public void Titles_come_from_the_localized_menu_keys()
    {
        var h = Create();
        h.Context.SetUser(TestUsers.Operator());
        h.Open(NavigationItemType.Shifts);

        // FakeLocalizationService returns the key itself
        Assert.All(h.ViewModel.NavItems, i => Assert.Equal($"Str_Menu_{i.Id}", i.Title));
        Assert.Equal("Str_Menu_Shifts", h.ViewModel.CurrentScreenTitle);
    }
}
