using System.Reflection;
using SmartPS.Models.Parking;

namespace SmartPS.Tests.Unit;

/// <summary>
/// B7 / AC-R7 / AC-R8: the gate flow adds no permission (the 23 RBAC permissions stay as they are) and only the two
/// gate/zone audit actions, appended after AUDIT_VERIFY in this order.
/// </summary>
public class ResidentVisitorConstantsTests
{
    [Fact]
    public void Permission_catalogue_is_the_23_RBAC_permissions_without_customer_or_blacklist_entries()
    {
        var all = Permissions.GetAll();

        Assert.Equal(23, all.Count);
        Assert.Equal(
            TestUsers.LegacyPermissions.Concat(TestUsers.NewPermissions).OrderBy(p => p, StringComparer.Ordinal),
            all.OrderBy(p => p, StringComparer.Ordinal));
        Assert.DoesNotContain(all, p => p.StartsWith("Customer.", StringComparison.Ordinal));
        Assert.DoesNotContain(all, p => p.StartsWith("Blacklist.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("CustomerView")]
    [InlineData("CustomerManage")]
    [InlineData("BlacklistManage")]
    public void Removed_permission_constants_do_not_exist(string fieldName)
    {
        Assert.Null(typeof(Permissions).GetField(fieldName, BindingFlags.Public | BindingFlags.Static));
    }

    [Fact]
    public void ModuleOrder_ends_with_Incident()
    {
        Assert.Equal("Incident", Permissions.ModuleOrder[^1]);
        Assert.DoesNotContain("Customer", Permissions.ModuleOrder);
        Assert.DoesNotContain("Blacklist", Permissions.ModuleOrder);
    }

    [Fact]
    public void Gate_audit_action_constants_have_spec_values()
    {
        Assert.Equal("GATE_BLACKLIST_BLOCKED", AuditActions.GateBlacklistBlocked);
        Assert.Equal("ZONE_UPDATE", AuditActions.ZoneUpdate);
    }

    [Fact]
    public void The_2_gate_actions_follow_AUDIT_VERIFY_in_declaration_order()
    {
        var all = AuditActions.All.ToList();
        var start = all.IndexOf(AuditActions.AuditVerify) + 1;

        Assert.True(start > 0);
        Assert.Equal(ResidentVisitorAuditActions.All, all.Skip(start).Take(2));
        Assert.Equal(19, all.Count);
    }

    [Theory]
    [InlineData("CUSTOMER_CREATE")]
    [InlineData("CUSTOMER_UPDATE")]
    [InlineData("CUSTOMER_VEHICLE_ADD")]
    [InlineData("CUSTOMER_VEHICLE_REMOVE")]
    [InlineData("TICKET_CREATE")]
    [InlineData("TICKET_RENEW")]
    [InlineData("TICKET_SUSPEND")]
    [InlineData("TICKET_RESUME")]
    [InlineData("BLACKLIST_ADD")]
    [InlineData("BLACKLIST_REMOVE")]
    [InlineData("GATE_BLACKLIST_EXIT_WARNING")]
    public void Removed_management_actions_are_not_in_the_catalogue(string action)
    {
        Assert.DoesNotContain(action, AuditActions.All);
    }

    [Fact]
    public void New_actions_fit_the_Action_column()
    {
        Assert.All(ResidentVisitorAuditActions.All, a => Assert.InRange(a.Length, 1, AuditRecordFactory.MaxActionLength));
    }

    [Fact]
    public void CustomerType_has_exactly_Resident_0_and_External_1()
    {
        Assert.Equal(0, (int)CustomerType.Resident);
        Assert.Equal(1, (int)CustomerType.External);
        Assert.Equal(new[] { "Resident", "External" }, Enum.GetNames<CustomerType>());
    }

    [Fact]
    public void ZoneAudience_values_are_stable()
    {
        Assert.Equal(0, (int)ZoneAudience.Mixed);
        Assert.Equal(1, (int)ZoneAudience.ResidentOnly);
        Assert.Equal(2, (int)ZoneAudience.VisitorOnly);
        Assert.Equal(3, Enum.GetValues<ZoneAudience>().Length);
    }
}
