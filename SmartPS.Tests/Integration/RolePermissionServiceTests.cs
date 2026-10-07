using Microsoft.Extensions.DependencyInjection;
using SmartPS.Data;
using SmartPS.Models.Navigation;
using SmartPS.Services.RolePermissions;

namespace SmartPS.Tests.Integration;

/// <summary>R9–R11, AC-4 (with seed re-run, m11), AC-5, R7 reload after a matrix save.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class RolePermissionServiceTests : IClassFixture<PostgresDatabaseFixture>
{
    private readonly PostgresDatabaseFixture _db;

    public RolePermissionServiceTests(PostgresDatabaseFixture db)
    {
        _db = db;
    }

    private static IRolePermissionService Matrix(IServiceProvider sp) => sp.GetRequiredService<IRolePermissionService>();

    private async Task SetGrantsAsync(string roleName, IEnumerable<string> permissions)
    {
        await _db.ExecuteAsync("DELETE FROM \"RolePermissions\" rp USING \"Roles\" r WHERE rp.\"RoleId\" = r.\"RoleId\" AND r.\"RoleName\" = @r", ("r", roleName));
        foreach (var p in permissions)
        {
            await _db.ExecuteAsync(
                "INSERT INTO \"RolePermissions\" (\"RoleId\",\"PermissionId\") SELECT r.\"RoleId\", p.\"PermissionId\" FROM \"Roles\" r, \"Permissions\" p WHERE r.\"RoleName\" = @r AND p.\"PermissionName\" = @p",
                ("r", roleName), ("p", p));
        }
    }

    [Fact]
    public async Task Matrix_lists_roles_by_id_permissions_by_module_and_admin_has_everything()
    {
        _db.RequireAvailable();
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAdminAsync();

        var matrix = await Matrix(sp).GetMatrixAsync();

        Assert.Equal(matrix.Roles.OrderBy(r => r.RoleId).Select(r => r.RoleId), matrix.Roles.Select(r => r.RoleId));
        Assert.Contains(matrix.Roles, r => r.RoleName == "Admin" && r.IsSystemAdmin);
        Assert.Contains(matrix.Roles, r => r.RoleName == "Manager" && !r.IsSystemAdmin);
        Assert.Contains(matrix.Roles, r => r.RoleName == "Operator" && !r.IsSystemAdmin);

        Assert.Equal(23, matrix.Permissions.Count);
        var expectedOrder = matrix.Permissions
            .OrderBy(p => Permissions.ModuleOrder.ToList().IndexOf(p.Module))
            .ThenBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => p.Name);
        Assert.Equal(expectedOrder, matrix.Permissions.Select(p => p.Name));
        Assert.All(matrix.Permissions, p => Assert.Equal(Permissions.GetModule(p.Name), p.Module));

        var admin = matrix.Roles.Single(r => r.IsSystemAdmin);
        Assert.Equal(Permissions.GetAll().ToHashSet(), matrix.GrantsByRoleId[admin.RoleId].ToHashSet());
        var op = matrix.Roles.Single(r => r.RoleName == "Operator");
        Assert.Equal(TestUsers.OperatorSeedPermissions.ToHashSet(), matrix.GrantsByRoleId[op.RoleId].ToHashSet());
    }

    [Fact]
    public async Task AC4_removing_Report_View_from_Operator_persists_audits_survives_seed_and_hides_menus()
    {
        _db.RequireAvailable();
        var operatorRoleId = await TestUsers.RoleIdAsync(_db.Factory, "Operator");
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        var original = await TestUsers.GrantsAsync(_db.Factory, "Operator");
        Assert.Contains(Permissions.ReportView, original);

        try
        {
            using var adminSession = IntegrationServices.Create(_db);
            await adminSession.LoginAdminAsync();
            var idBefore = await AuditDb.MaxIdAsync(_db.Factory);
            var desired = original.Where(p => p != Permissions.ReportView).ToList();

            // Step 1: Admin saves the matrix without Report.View for Operator.
            var changes = await Matrix(adminSession).SaveAsync(new Dictionary<int, IReadOnlyCollection<string>> { [operatorRoleId] = desired });

            var change = Assert.Single(changes);
            Assert.Equal(operatorRoleId, change.RoleId);
            Assert.Equal(new[] { Permissions.ReportView }, change.Removed);
            Assert.Empty(change.Added);

            // Step 2: RolePermissions reflects it; other roles untouched.
            Assert.DoesNotContain(Permissions.ReportView, await TestUsers.GrantsAsync(_db.Factory, "Operator"));
            Assert.Contains(Permissions.ReportView, await TestUsers.GrantsAsync(_db.Factory, "Manager"));

            // Step 3: ROLE_PERMISSIONS_UPDATE lists removed ["Report.View"], added [].
            var row = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.RolePermissionsUpdate));
            Assert.Equal(AuditOutcome.Success, row.Outcome);
            Assert.Equal("Role", row.EntityType);
            Assert.Equal(operatorRoleId.ToString(), row.EntityId);
            var details = AuditDb.Details(row);
            Assert.Equal("Operator", AuditDb.String(details, "roleName"));
            Assert.Equal(new[] { "Report.View" }, AuditDb.StringArray(details, "removed"));
            Assert.Empty(AuditDb.StringArray(details, "added"));

            // Step 4: simulated app restart (seed re-run) must not re-grant it (m4).
            await using (var ctx = _db.CreateContext())
            {
                await DbInitializer.InitializeAsync(ctx);
            }

            Assert.DoesNotContain(Permissions.ReportView, await TestUsers.GrantsAsync(_db.Factory, "Operator"));

            // Step 5: Operator logs in again → no Report.View, Reports/Transactions hidden.
            using var opSession = IntegrationServices.Create(_db);
            await opSession.LoginAsync(op.Username);
            Assert.False(opSession.Perms().HasPermission(Permissions.ReportView));
            Assert.False(NavigationAccessPolicy.CanAccess(NavigationItemType.Reports, opSession.Perms()));
            Assert.False(NavigationAccessPolicy.CanAccess(NavigationItemType.Transactions, opSession.Perms()));
            Assert.True(NavigationAccessPolicy.CanAccess(NavigationItemType.Overview, opSession.Perms()));
        }
        finally
        {
            // Step 6: restore the grant.
            await SetGrantsAsync("Operator", original);
        }
    }

    [Fact]
    public async Task AC5_manager_can_read_matrix_but_save_is_denied_and_audited()
    {
        _db.RequireAvailable();
        var manager = await TestUsers.CreateAsync(_db.Factory, "Manager");
        var operatorRoleId = await TestUsers.RoleIdAsync(_db.Factory, "Operator");
        var before = await TestUsers.GrantsAsync(_db.Factory, "Operator");
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(manager.Username);

        var matrix = await Matrix(sp).GetMatrixAsync();
        Assert.NotEmpty(matrix.Roles);
        Assert.True(sp.Perms().HasPermission(Permissions.RoleView));
        Assert.False(sp.Perms().HasPermission(Permissions.RoleManage));

        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);
        var ex = await Assert.ThrowsAsync<PermissionDeniedException>(() => Matrix(sp).SaveAsync(
            new Dictionary<int, IReadOnlyCollection<string>> { [operatorRoleId] = new[] { Permissions.ParkingView } }));

        Assert.Contains(Permissions.RoleManage, ex.RequiredPermissions);
        Assert.Equal(before, await TestUsers.GrantsAsync(_db.Factory, "Operator"));
        var rows = await AuditDb.RowsAfterAsync(_db.Factory, idBefore);
        var denied = Assert.Single(rows);
        Assert.Equal(AuditActions.AccessDenied, denied.Action);
        Assert.Equal(AuditOutcome.Denied, denied.Outcome);
        Assert.Equal(manager.UserId, denied.UserId);
    }

    [Fact]
    public async Task Operator_without_Role_View_cannot_read_matrix()
    {
        _db.RequireAvailable();
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(op.Username);
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var ex = await Assert.ThrowsAsync<PermissionDeniedException>(() => Matrix(sp).GetMatrixAsync());

        Assert.Contains(Permissions.RoleView, ex.RequiredPermissions);
        Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.AccessDenied));
    }

    [Fact]
    public async Task Save_rejects_admin_role_unknown_role_and_unknown_permission()
    {
        _db.RequireAvailable();
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAdminAsync();
        var adminRoleId = await TestUsers.RoleIdAsync(_db.Factory, "Admin");
        var operatorRoleId = await TestUsers.RoleIdAsync(_db.Factory, "Operator");
        var adminBefore = await TestUsers.GrantsAsync(_db.Factory, "Admin");
        var opBefore = await TestUsers.GrantsAsync(_db.Factory, "Operator");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Matrix(sp).SaveAsync(
            new Dictionary<int, IReadOnlyCollection<string>> { [adminRoleId] = new[] { Permissions.UserView } }));
        await Assert.ThrowsAsync<ArgumentException>(() => Matrix(sp).SaveAsync(
            new Dictionary<int, IReadOnlyCollection<string>> { [987654] = new[] { Permissions.UserView } }));
        await Assert.ThrowsAsync<ArgumentException>(() => Matrix(sp).SaveAsync(
            new Dictionary<int, IReadOnlyCollection<string>> { [operatorRoleId] = opBefore.Append("Fake.Permission").ToArray() }));

        Assert.Equal(adminBefore, await TestUsers.GrantsAsync(_db.Factory, "Admin"));
        Assert.Equal(opBefore, await TestUsers.GrantsAsync(_db.Factory, "Operator"));
    }

    [Fact]
    public async Task Saving_unchanged_grants_returns_no_changes_and_writes_no_audit_row()
    {
        _db.RequireAvailable();
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAdminAsync();
        var operatorRoleId = await TestUsers.RoleIdAsync(_db.Factory, "Operator");
        var current = await TestUsers.GrantsAsync(_db.Factory, "Operator");
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var changes = await Matrix(sp).SaveAsync(new Dictionary<int, IReadOnlyCollection<string>> { [operatorRoleId] = current.ToArray() });

        Assert.Empty(changes);
        Assert.Empty(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.RolePermissionsUpdate));
    }

    [Fact]
    public async Task Added_and_removed_lists_are_sorted_and_one_row_per_changed_role()
    {
        _db.RequireAvailable();
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAdminAsync();
        var operatorRoleId = await TestUsers.RoleIdAsync(_db.Factory, "Operator");
        var managerRoleId = await TestUsers.RoleIdAsync(_db.Factory, "Manager");
        var opBefore = await TestUsers.GrantsAsync(_db.Factory, "Operator");
        var mgrBefore = await TestUsers.GrantsAsync(_db.Factory, "Manager");
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        try
        {
            var opDesired = opBefore.Concat(new[] { Permissions.ReportExport, Permissions.IncidentManage }).ToArray();
            var mgrDesired = mgrBefore.Where(p => p != Permissions.UserView && p != Permissions.AuditView).ToArray();

            var changes = await Matrix(sp).SaveAsync(new Dictionary<int, IReadOnlyCollection<string>>
            {
                [operatorRoleId] = opDesired,
                [managerRoleId] = mgrDesired
            });

            Assert.Equal(2, changes.Count);
            var rows = await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.RolePermissionsUpdate);
            Assert.Equal(2, rows.Count);
            var opRow = Assert.Single(rows, r => r.EntityId == operatorRoleId.ToString());
            Assert.Equal(new[] { "Incident.Manage", "Report.Export" }, AuditDb.StringArray(AuditDb.Details(opRow), "added"));
            Assert.Empty(AuditDb.StringArray(AuditDb.Details(opRow), "removed"));
            var mgrRow = Assert.Single(rows, r => r.EntityId == managerRoleId.ToString());
            Assert.Equal(new[] { "Audit.View", "User.View" }, AuditDb.StringArray(AuditDb.Details(mgrRow), "removed"));
            Assert.Equal(opDesired.ToHashSet(), await TestUsers.GrantsAsync(_db.Factory, "Operator"));
            Assert.Equal(mgrDesired.ToHashSet(), await TestUsers.GrantsAsync(_db.Factory, "Manager"));
        }
        finally
        {
            await SetGrantsAsync("Operator", opBefore);
            await SetGrantsAsync("Manager", mgrBefore);
        }
    }

    [Fact]
    public async Task R7_saving_own_role_reloads_current_user_permissions()
    {
        // R7: permissions are reloaded after saving the matrix (no re-login needed).
        _db.RequireAvailable();
        var role = await TestUsers.CreateRoleAsync(_db.Factory, TestUsers.UniqueName("rm"), Permissions.RoleView, Permissions.RoleManage);
        var user = await TestUsers.CreateAsync(_db.Factory, role.RoleName);
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(user.Username);
        Assert.False(sp.Perms().HasPermission(Permissions.ReportExport));

        await Matrix(sp).SaveAsync(new Dictionary<int, IReadOnlyCollection<string>>
        {
            [role.RoleId] = new[] { Permissions.RoleView, Permissions.RoleManage, Permissions.ReportExport }
        });

        Assert.True(sp.Perms().HasPermission(Permissions.ReportExport));
        Assert.Contains(Permissions.ReportExport, sp.GetRequiredService<ICurrentUserContext>().Permissions);
    }
}
