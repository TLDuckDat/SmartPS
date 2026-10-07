using SmartPS.Models.Auth;
using SmartPS.Models.Navigation;

namespace SmartPS.Tests.Unit;

/// <summary>R3 sidebar mapping, AC-1 (Operator sidebar), AC-2 (no user → nothing visible), AC-14 (Audit menu hidden without Audit.View).</summary>
public class NavigationAccessPolicyTests
{
    private static IPermissionService For(User? user)
    {
        var context = new CurrentUserContext();
        context.SetUser(user);
        return new PermissionService(context);
    }

    private static HashSet<NavigationItemType> Visible(IPermissionService permissions)
        => Enum.GetValues<NavigationItemType>().Where(i => NavigationAccessPolicy.CanAccess(i, permissions)).ToHashSet();

    public static TheoryData<NavigationItemType, string[]> R3Table => new()
    {
        { NavigationItemType.Overview, new[] { "Parking.View" } },
        { NavigationItemType.ParkingMap, new[] { "Parking.View" } },
        { NavigationItemType.Incidents, new[] { "Parking.View" } },
        { NavigationItemType.GateControl, new[] { "Parking.CheckIn", "Parking.CheckOut" } },
        { NavigationItemType.Shifts, new[] { "Shift.View" } },
        { NavigationItemType.Customers, new[] { "Customer.View" } },
        { NavigationItemType.Reports, new[] { "Report.View" } },
        { NavigationItemType.Transactions, new[] { "Report.View" } },
        { NavigationItemType.Pricing, new[] { "Pricing.Manage" } },
        { NavigationItemType.UserManagement, new[] { "User.View" } },
        { NavigationItemType.RolePermissions, new[] { "Role.View" } },
        { NavigationItemType.AuditLog, new[] { "Audit.View" } },
        { NavigationItemType.Settings, new[] { "Settings.Manage" } },
    };

    [Theory]
    [MemberData(nameof(R3Table))]
    public void Required_permissions_match_spec_R3_table(NavigationItemType item, string[] expected)
    {
        Assert.Equal(expected.OrderBy(x => x, StringComparer.Ordinal), NavigationAccessPolicy.GetRequiredPermissions(item).OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void Every_navigation_item_declares_at_least_one_permission()
    {
        foreach (var item in Enum.GetValues<NavigationItemType>())
        {
            Assert.NotEmpty(NavigationAccessPolicy.GetRequiredPermissions(item));
        }

        Assert.Equal(13, Enum.GetValues<NavigationItemType>().Length);
    }

    [Fact]
    public void Operator_seed_sees_exactly_the_AC1_items()
    {
        // AC-1: Given Operator (seed), Then visible = Overview, GateControl, Shifts, ParkingMap, Incidents, Reports, Transactions
        // (+ Customers, read-only, since the resident/visitor flow grants Customer.View to Operator).
        var visible = Visible(For(TestUsers.Operator()));

        Assert.Equal(new HashSet<NavigationItemType>
        {
            NavigationItemType.Overview, NavigationItemType.GateControl, NavigationItemType.Shifts, NavigationItemType.ParkingMap,
            NavigationItemType.Incidents, NavigationItemType.Reports, NavigationItemType.Transactions,
            NavigationItemType.Customers
        }, visible);

        foreach (var hidden in new[]
                 {
                     NavigationItemType.UserManagement, NavigationItemType.RolePermissions, NavigationItemType.AuditLog,
                     NavigationItemType.Settings, NavigationItemType.Pricing
                 })
        {
            Assert.DoesNotContain(hidden, visible);
        }
    }

    [Fact]
    public void Null_user_sees_nothing()
    {
        // AC-2
        Assert.Empty(Visible(For(null)));
    }

    [Fact]
    public void System_admin_sees_everything()
    {
        Assert.Equal(Enum.GetValues<NavigationItemType>().ToHashSet(), Visible(For(TestUsers.AdminWithoutGrants())));
    }

    [Fact]
    public void Manager_defaults_see_everything_except_settings()
    {
        // Spec §6.1 Manager defaults include Role.View and Audit.View but not Settings.Manage.
        var visible = Visible(For(TestUsers.Manager()));

        Assert.Equal(Enum.GetValues<NavigationItemType>().Where(i => i != NavigationItemType.Settings).ToHashSet(), visible);
    }

    [Fact]
    public void GateControl_is_any_of_check_in_or_check_out()
    {
        Assert.True(NavigationAccessPolicy.CanAccess(NavigationItemType.GateControl, For(TestUsers.Build("Exit", Permissions.ParkingCheckOut))));
        Assert.True(NavigationAccessPolicy.CanAccess(NavigationItemType.GateControl, For(TestUsers.Build("Entry", Permissions.ParkingCheckIn))));
        Assert.False(NavigationAccessPolicy.CanAccess(NavigationItemType.GateControl, For(TestUsers.Build("Viewer", Permissions.ParkingView))));
    }

    [Fact]
    public void Operator_without_Report_View_loses_reports_and_transactions()
    {
        // AC-4 (policy part): after Report.View is removed from Operator.
        var grants = TestUsers.OperatorSeedPermissions.Where(p => p != Permissions.ReportView).ToArray();
        var visible = Visible(For(TestUsers.Build("Operator", grants)));

        Assert.DoesNotContain(NavigationItemType.Reports, visible);
        Assert.DoesNotContain(NavigationItemType.Transactions, visible);
        Assert.Contains(NavigationItemType.Overview, visible);
    }

    [Fact]
    public void Audit_menu_requires_Audit_View()
    {
        // AC-14: user without Audit.View does not see the audit screen.
        Assert.False(NavigationAccessPolicy.CanAccess(NavigationItemType.AuditLog, For(TestUsers.Build("X", Permissions.AuditVerify))));
        Assert.True(NavigationAccessPolicy.CanAccess(NavigationItemType.AuditLog, For(TestUsers.Build("Y", Permissions.AuditView))));
    }
}
