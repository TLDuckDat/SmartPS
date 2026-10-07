namespace SmartPS.Tests.Unit;

/// <summary>R2 (5 new permissions), R9 (module grouping), R14 (action catalogue), AC-15 (permission count; 26 after the resident/visitor flow).</summary>
public class PermissionsConstantsTests
{
    [Fact]
    public void GetAll_returns_23_distinct_permissions_including_the_5_new_ones()
    {
        var all = Permissions.GetAll();

        Assert.Equal(TestUsers.TotalPermissionCount, all.Count);
        Assert.Equal(all.Count, all.Distinct(StringComparer.Ordinal).Count());
        foreach (var p in TestUsers.LegacyPermissions.Concat(TestUsers.NewPermissions))
        {
            Assert.Contains(p, all);
        }
    }

    [Fact]
    public void New_permission_constants_have_spec_values()
    {
        Assert.Equal("Payment.Refund", Permissions.PaymentRefund);
        Assert.Equal("Audit.View", Permissions.AuditView);
        Assert.Equal("Audit.Verify", Permissions.AuditVerify);
        Assert.Equal("Settings.Manage", Permissions.SettingsManage);
        Assert.Equal("Incident.Manage", Permissions.IncidentManage);
    }

    [Fact]
    public void ModuleOrder_is_the_R9_grouping()
    {
        Assert.Equal(
            new[] { "User", "Role", "Parking", "Pricing", "Report", "Shift", "Payment", "Audit", "Settings", "Incident", "Customer", "Blacklist" },
            Permissions.ModuleOrder);
    }

    [Fact]
    public void Every_permission_belongs_to_a_known_module()
    {
        foreach (var p in Permissions.GetAll())
        {
            Assert.Contains(Permissions.GetModule(p), Permissions.ModuleOrder);
        }
    }

    [Theory]
    [InlineData("Audit.View", "Audit")]
    [InlineData("Parking.CheckIn", "Parking")]
    [InlineData("Shift.Adjust", "Shift")]
    [InlineData("Settings.Manage", "Settings")]
    public void GetModule_returns_prefix_before_first_dot(string permission, string module)
    {
        Assert.Equal(module, Permissions.GetModule(permission));
    }

    [Theory]
    [InlineData("NoDot")]
    [InlineData("")]
    public void GetModule_without_dot_throws(string permission)
    {
        Assert.Throws<ArgumentException>(() => Permissions.GetModule(permission));
    }

    [Fact]
    public void AuditActions_All_is_the_R14_catalogue_in_declaration_order()
    {
        Assert.Equal(new[]
        {
            "AUTH_LOGIN_SUCCESS", "AUTH_LOGIN_FAILED", "AUTH_LOGOUT", "ACCESS_DENIED",
            "USER_CREATE", "USER_UPDATE", "USER_DELETE",
            "ROLE_PERMISSIONS_UPDATE", "PARKING_CHECKIN", "PARKING_CHECKOUT",
            "PAYMENT_REFUND", "PAYMENT_CANCEL", "SHIFT_OPEN", "SHIFT_CLOSE", "SHIFT_REVIEW", "SHIFT_ADJUSTMENT", "AUDIT_VERIFY",
            "CUSTOMER_CREATE", "CUSTOMER_UPDATE", "CUSTOMER_VEHICLE_ADD", "CUSTOMER_VEHICLE_REMOVE",
            "TICKET_CREATE", "TICKET_RENEW", "TICKET_SUSPEND", "TICKET_RESUME",
            "BLACKLIST_ADD", "BLACKLIST_REMOVE", "GATE_BLACKLIST_BLOCKED", "GATE_BLACKLIST_EXIT_WARNING", "ZONE_UPDATE"
        }, AuditActions.All);
    }

    [Fact]
    public void AuditActions_fit_the_Action_column()
    {
        Assert.All(AuditActions.All, a => Assert.InRange(a.Length, 1, AuditRecordFactory.MaxActionLength));
    }

    [Fact]
    public void AuditOutcome_values_are_stable()
    {
        Assert.Equal(0, (int)AuditOutcome.Success);
        Assert.Equal(1, (int)AuditOutcome.Denied);
        Assert.Equal(2, (int)AuditOutcome.Failed);
        Assert.Equal(3, Enum.GetValues<AuditOutcome>().Length);
    }
}
