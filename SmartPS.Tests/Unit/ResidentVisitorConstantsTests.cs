namespace SmartPS.Tests.Unit;

/// <summary>R8 (3 new permissions), R9 (13 new audit actions in the fixed order after AUDIT_VERIFY, Task 3 appends later).</summary>
public class ResidentVisitorConstantsTests
{
    [Fact]
    public void New_permission_constants_have_spec_values()
    {
        Assert.Equal("Customer.View", Permissions.CustomerView);
        Assert.Equal("Customer.Manage", Permissions.CustomerManage);
        Assert.Equal("Blacklist.Manage", Permissions.BlacklistManage);
    }

    [Fact]
    public void GetAll_contains_the_3_new_permissions_and_has_26_entries()
    {
        var all = Permissions.GetAll();

        Assert.Equal(26, all.Count);
        Assert.Equal(TestUsers.TotalPermissionCount, all.Count);
        Assert.Superset(TestUsers.CustomerPermissions.ToHashSet(StringComparer.Ordinal), all.ToHashSet(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("Customer.View", "Customer")]
    [InlineData("Customer.Manage", "Customer")]
    [InlineData("Blacklist.Manage", "Blacklist")]
    public void New_permissions_belong_to_the_new_modules(string permission, string module)
    {
        Assert.Equal(module, Permissions.GetModule(permission));
        Assert.Contains(module, Permissions.ModuleOrder);
    }

    [Fact]
    public void ModuleOrder_ends_with_Customer_then_Blacklist()
    {
        var order = Permissions.ModuleOrder.ToList();
        Assert.Equal(order.IndexOf("Incident") + 1, order.IndexOf("Customer"));
        Assert.Equal(order.IndexOf("Customer") + 1, order.IndexOf("Blacklist"));
    }

    [Fact]
    public void Audit_action_constants_have_spec_values()
    {
        Assert.Equal("CUSTOMER_CREATE", AuditActions.CustomerCreate);
        Assert.Equal("CUSTOMER_UPDATE", AuditActions.CustomerUpdate);
        Assert.Equal("CUSTOMER_VEHICLE_ADD", AuditActions.CustomerVehicleAdd);
        Assert.Equal("CUSTOMER_VEHICLE_REMOVE", AuditActions.CustomerVehicleRemove);
        Assert.Equal("TICKET_CREATE", AuditActions.TicketCreate);
        Assert.Equal("TICKET_RENEW", AuditActions.TicketRenew);
        Assert.Equal("TICKET_SUSPEND", AuditActions.TicketSuspend);
        Assert.Equal("TICKET_RESUME", AuditActions.TicketResume);
        Assert.Equal("BLACKLIST_ADD", AuditActions.BlacklistAdd);
        Assert.Equal("BLACKLIST_REMOVE", AuditActions.BlacklistRemove);
        Assert.Equal("GATE_BLACKLIST_BLOCKED", AuditActions.GateBlacklistBlocked);
        Assert.Equal("GATE_BLACKLIST_EXIT_WARNING", AuditActions.GateBlacklistExitWarning);
        Assert.Equal("ZONE_UPDATE", AuditActions.ZoneUpdate);
    }

    [Fact]
    public void The_13_actions_follow_AUDIT_VERIFY_in_declaration_order()
    {
        var all = AuditActions.All.ToList();
        var start = all.IndexOf(AuditActions.AuditVerify) + 1;

        Assert.True(start > 0);
        Assert.Equal(ResidentVisitorAuditActions.All, all.Skip(start).Take(13));
        Assert.Equal(13, ResidentVisitorAuditActions.All.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void New_actions_fit_the_Action_column()
    {
        Assert.All(ResidentVisitorAuditActions.All, a => Assert.InRange(a.Length, 1, AuditRecordFactory.MaxActionLength));
    }
}
