using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SmartPS.Models.Navigation;
using SmartPS.Services.Auth;
using SmartPS.Services.Dialog;
using SmartPS.Services.Localization;
using SmartPS.ViewModels.Dashboard;

namespace SmartPS.Tests.Unit;

/// <summary>
/// T-NAV, R3, R4, AC-1, AC-2 at the view-model level. DashboardViewModel is built from a minimal
/// ServiceCollection (plan addendum N9) so constructor parameter order does not matter.
/// The VM is always created with no user, so the constructor never auto-navigates (no child VMs are registered).
/// </summary>
public class DashboardNavigationDenyTests
{
    private sealed class Harness
    {
        public required CurrentUserContext Context { get; init; }
        public required FakeAuditService Audit { get; init; }
        public required FakeDialogService Dialog { get; init; }
        public required DashboardViewModel ViewModel { get; init; }

        public NavigationMenuItem Item(NavigationItemType id) => ViewModel.NavItems.Single(i => i.Id == id);

        public HashSet<NavigationItemType> VisibleIds() => ViewModel.NavItems.Where(i => i.IsVisible).Select(i => i.Id).ToHashSet();
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
        return new Harness { Context = context, Audit = audit, Dialog = dialog, ViewModel = vm };
    }

    [Fact]
    public void Without_user_no_menu_item_is_visible_and_no_content_is_shown()
    {
        // AC-2 / R4: no "Admin" fallback any more.
        var h = Create();

        Assert.Equal(13, h.ViewModel.NavItems.Count);
        Assert.Empty(h.VisibleIds());
        Assert.Null(h.ViewModel.CurrentViewModel);
        Assert.Equal(string.Empty, h.ViewModel.CurrentUserRole);
        Assert.Equal(string.Empty, h.ViewModel.CurrentUserFullName);
    }

    [Fact]
    public void Menu_contains_new_RolePermissions_and_AuditLog_items_with_policy_permissions()
    {
        var h = Create();

        Assert.Contains(h.ViewModel.NavItems, i => i.Id == NavigationItemType.RolePermissions);
        Assert.Contains(h.ViewModel.NavItems, i => i.Id == NavigationItemType.AuditLog);
        foreach (var item in h.ViewModel.NavItems)
        {
            Assert.Equal(NavigationAccessPolicy.GetRequiredPermissions(item.Id), item.RequiredPermissions);
        }
    }

    [Fact]
    public void Operator_login_recomputes_visibility_to_AC1_set_without_auto_navigation()
    {
        // AC-1 + N9: Changed only recomputes visibility.
        var h = Create();

        h.Context.SetUser(TestUsers.Operator());

        Assert.Equal(new HashSet<NavigationItemType>
        {
            NavigationItemType.Overview, NavigationItemType.GateControl, NavigationItemType.Shifts, NavigationItemType.ParkingMap,
            NavigationItemType.Incidents, NavigationItemType.Reports, NavigationItemType.Transactions
        }, h.VisibleIds());
        Assert.False(h.Item(NavigationItemType.UserManagement).IsVisible);
        Assert.False(h.Item(NavigationItemType.AuditLog).IsVisible);
        Assert.Null(h.ViewModel.CurrentViewModel);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public void Direct_navigation_to_hidden_item_is_denied_warned_and_audited()
    {
        // T-NAV: Given Operator, When NavigateCommand(UserManagement), Then warning + ACCESS_DENIED(Navigation/UserManagement), no content change.
        var h = Create();
        h.Context.SetUser(TestUsers.Operator());
        var target = h.Item(NavigationItemType.UserManagement);

        h.ViewModel.NavigateCommand.Execute(target);

        Assert.Single(h.Dialog.Warnings);
        Assert.Null(h.ViewModel.CurrentViewModel);
        Assert.NotSame(target, h.ViewModel.SelectedNavItem);
        Assert.False(target.IsSelected);

        var entry = Assert.Single(h.Audit.Entries);
        Assert.Equal(AuditActions.AccessDenied, entry.Action);
        Assert.Equal(AuditOutcome.Denied, entry.Outcome);
        Assert.Equal("Navigation", entry.EntityType);
        Assert.Equal("UserManagement", entry.EntityId);
        using var doc = JsonDocument.Parse(AuditDetails.ToCanonicalJson(entry.Details));
        Assert.Equal(new[] { "User.View" },
            doc.RootElement.GetProperty("requiredPermissions").EnumerateArray().Select(e => e.GetString()).ToArray());
    }

    [Fact]
    public void Direct_navigation_without_any_user_is_denied_and_audited()
    {
        var h = Create();

        h.ViewModel.NavigateCommand.Execute(h.Item(NavigationItemType.Overview));

        Assert.Single(h.Dialog.Warnings);
        Assert.Null(h.ViewModel.CurrentViewModel);
        var entry = Assert.Single(h.Audit.Entries);
        Assert.Equal("Navigation", entry.EntityType);
        Assert.Equal("Overview", entry.EntityId);
    }

    [Fact]
    public void Admin_sees_every_item_including_role_matrix_and_audit_log()
    {
        var h = Create();

        h.Context.SetUser(TestUsers.AdminWithoutGrants());

        Assert.Equal(Enum.GetValues<NavigationItemType>().ToHashSet(), h.VisibleIds());
    }

    [Fact]
    public void Clearing_the_user_hides_everything_again()
    {
        var h = Create();
        h.Context.SetUser(TestUsers.Manager());
        Assert.NotEmpty(h.VisibleIds());

        h.Context.SetUser(null);

        Assert.Empty(h.VisibleIds());
        Assert.Null(h.ViewModel.CurrentViewModel);
    }

    [Fact]
    public void Removing_Report_View_hides_reports_and_transactions_on_reload()
    {
        // AC-4 (VM part): permission reload after a matrix save.
        var h = Create();
        h.Context.SetUser(TestUsers.Operator());
        Assert.True(h.Item(NavigationItemType.Reports).IsVisible);

        h.Context.SetUser(TestUsers.Build("Operator", TestUsers.OperatorSeedPermissions.Where(p => p != Permissions.ReportView).ToArray()));

        Assert.False(h.Item(NavigationItemType.Reports).IsVisible);
        Assert.False(h.Item(NavigationItemType.Transactions).IsVisible);
    }
}
