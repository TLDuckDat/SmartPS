using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartPS.DTOs.Auth;
using SmartPS.Models.Auth;
using SmartPS.Services.Auth;

namespace SmartPS.Tests.Integration;

/// <summary>
/// AuthService against PostgreSQL: AC-3, AC-6 (+6b / m5), AC-7 (R7), AC-8, E3, E5, E6, R14 user events, R18.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class AuthServiceIntegrationTests : IClassFixture<PostgresDatabaseFixture>
{
    /// <summary>Well below the 15 s lock timeout that a self-deadlock (M2) would cost.</summary>
    private static readonly TimeSpan NoLockWait = TimeSpan.FromSeconds(5);

    private readonly PostgresDatabaseFixture _db;

    public AuthServiceIntegrationTests(PostgresDatabaseFixture db)
    {
        _db = db;
    }

    private ServiceProvider NewSession() => IntegrationServices.Create(_db);

    private async Task<Role> CreateHelperRoleAsync()
        => await TestUsers.CreateRoleAsync(_db.Factory, TestUsers.UniqueName("helper"),
            Permissions.UserView, Permissions.UserCreate, Permissions.UserEdit);

    private static void AssertNoSecrets(AuditLog row, params string[] secrets)
    {
        Assert.DoesNotContain("$2a$", row.Details, StringComparison.Ordinal);
        Assert.DoesNotContain("$2b$", row.Details, StringComparison.Ordinal);
        Assert.DoesNotContain("passwordHash", row.Details, StringComparison.OrdinalIgnoreCase);
        foreach (var secret in secrets)
        {
            Assert.DoesNotContain(secret, row.Details, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task AC3_operator_calling_RegisterAsync_is_denied_creates_nothing_and_audits_once()
    {
        // AC-3: Given a user without User.Create, When RegisterAsync directly, Then denied, no new user, exactly 1 ACCESS_DENIED/Denied.
        _db.RequireAvailable();
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        using var sp = NewSession();
        await sp.LoginAsync(op.Username);
        var newName = TestUsers.UniqueName("ac3");
        var operatorRoleId = await TestUsers.RoleIdAsync(_db.Factory, "Operator");
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var ex = await Assert.ThrowsAsync<PermissionDeniedException>(() => sp.Auth().RegisterAsync(new RegisterRequest
        {
            Username = newName,
            Password = "Test@123",
            FullName = "Should Not Exist",
            RoleId = operatorRoleId
        }));

        Assert.Contains(Permissions.UserCreate, ex.RequiredPermissions);
        await using (var ctx = _db.CreateContext())
        {
            Assert.False(await ctx.Users.AnyAsync(u => u.Username == newName));
        }

        var rows = await AuditDb.RowsAfterAsync(_db.Factory, idBefore);
        var denied = Assert.Single(rows);
        Assert.Equal(AuditActions.AccessDenied, denied.Action);
        Assert.Equal(AuditOutcome.Denied, denied.Outcome);
        Assert.Equal(op.UserId, denied.UserId);
        Assert.Equal(op.Username, denied.Username);
        Assert.Equal("Operator", denied.RoleName);
        Assert.Equal(new[] { Permissions.UserCreate }, AuditDb.StringArray(AuditDb.Details(denied), "requiredPermissions"));
    }

    [Fact]
    public async Task AC6_last_active_admin_cannot_be_demoted_locked_or_deleted()
    {
        // AC-6 (plan §6.1 "final concrete rule"): actor admin3 is logged in (snapshot = system admin) but inactive in DB,
        // so seeded 'admin' is the last active admin.
        _db.RequireAvailable();
        var adminRoleId = await TestUsers.RoleIdAsync(_db.Factory, "Admin");
        var managerRoleId = await TestUsers.RoleIdAsync(_db.Factory, "Manager");
        var activeSnapshot = await SnapshotAdminActivityAsync(adminRoleId);
        var admin3 = await TestUsers.CreateAsync(_db.Factory, "Admin");
        using var sp = NewSession();
        await sp.LoginAsync(admin3.Username);

        try
        {
            await _db.ExecuteAsync("UPDATE \"Users\" SET \"IsActive\" = false WHERE \"RoleId\" = @r AND \"Username\" <> 'admin'", ("r", adminRoleId));
            var seed = await TestUsers.LoadByUsernameAsync(_db.Factory, "admin");
            Assert.True(seed.IsActive);
            var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

            var sw = Stopwatch.StartNew();
            var demote = await Assert.ThrowsAsync<AdminProtectionException>(() => sp.Auth().UpdateUserAsync(new UpdateUserRequest
            {
                UserId = seed.UserId, FullName = seed.FullName, RoleId = managerRoleId, IsActive = true
            }));
            Assert.Equal(AdminProtectionReason.LastActiveAdmin, demote.Reason);
            Assert.True(sw.Elapsed < NoLockWait, $"demote rejection took {sw.Elapsed}");

            sw.Restart();
            var lockOut = await Assert.ThrowsAsync<AdminProtectionException>(() => sp.Auth().UpdateUserAsync(new UpdateUserRequest
            {
                UserId = seed.UserId, FullName = seed.FullName, RoleId = adminRoleId, IsActive = false
            }));
            Assert.Equal(AdminProtectionReason.LastActiveAdmin, lockOut.Reason);
            Assert.True(sw.Elapsed < NoLockWait, $"lock rejection took {sw.Elapsed}");

            sw.Restart();
            var delete = await Assert.ThrowsAsync<AdminProtectionException>(() => sp.Auth().DeleteUserAsync(seed.UserId));
            Assert.Equal(AdminProtectionReason.LastActiveAdmin, delete.Reason);
            Assert.True(sw.Elapsed < NoLockWait, $"delete rejection took {sw.Elapsed}");
            Assert.False(string.IsNullOrWhiteSpace(delete.Message));

            // DB unchanged
            var after = await TestUsers.LoadByUsernameAsync(_db.Factory, "admin");
            Assert.Equal(adminRoleId, after.RoleId);
            Assert.True(after.IsActive);
            Assert.Equal(seed.FullName, after.FullName);

            // One Failed row per attempt, none Success
            var rows = await AuditDb.RowsAfterAsync(_db.Factory, idBefore);
            var updates = rows.Where(r => r.Action == AuditActions.UserUpdate).ToList();
            var deletes = rows.Where(r => r.Action == AuditActions.UserDelete).ToList();
            Assert.Equal(2, updates.Count);
            Assert.Single(deletes);
            Assert.All(updates.Concat(deletes), r =>
            {
                Assert.Equal(AuditOutcome.Failed, r.Outcome);
                Assert.Equal(seed.UserId.ToString(), r.EntityId);
                Assert.Equal(admin3.UserId, r.UserId);
                Assert.Equal("LastActiveAdmin", AuditDb.String(AuditDb.Details(r), "reason"));
            });
        }
        finally
        {
            await RestoreAdminActivityAsync(activeSnapshot);
        }
    }

    [Fact]
    public async Task AC6_user_cannot_lock_demote_or_delete_self()
    {
        _db.RequireAvailable();
        var managerRoleId = await TestUsers.RoleIdAsync(_db.Factory, "Manager");
        var adminRoleId = await TestUsers.RoleIdAsync(_db.Factory, "Admin");
        var self = await TestUsers.CreateAsync(_db.Factory, "Admin");
        using var sp = NewSession();
        await sp.LoginAsync(self.Username);
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var lockSelf = await Assert.ThrowsAsync<AdminProtectionException>(() => sp.Auth().UpdateUserAsync(new UpdateUserRequest
        {
            UserId = self.UserId, FullName = self.FullName, RoleId = adminRoleId, IsActive = false
        }));
        Assert.Equal(AdminProtectionReason.SelfDeactivate, lockSelf.Reason);

        var demoteSelf = await Assert.ThrowsAsync<AdminProtectionException>(() => sp.Auth().UpdateUserAsync(new UpdateUserRequest
        {
            UserId = self.UserId, FullName = self.FullName, RoleId = managerRoleId, IsActive = true
        }));
        Assert.Equal(AdminProtectionReason.SelfRoleChange, demoteSelf.Reason);

        var deleteSelf = await Assert.ThrowsAsync<AdminProtectionException>(() => sp.Auth().DeleteUserAsync(self.UserId));
        Assert.Equal(AdminProtectionReason.SelfDelete, deleteSelf.Reason);

        var after = await TestUsers.LoadAsync(_db.Factory, self.UserId);
        Assert.True(after.IsActive);
        Assert.Equal(adminRoleId, after.RoleId);

        var rows = await AuditDb.RowsAfterAsync(_db.Factory, idBefore);
        Assert.DoesNotContain(rows, r => r.Outcome == AuditOutcome.Success && (r.Action == AuditActions.UserUpdate || r.Action == AuditActions.UserDelete));
        var reasons = rows.Where(r => r.Outcome == AuditOutcome.Failed).Select(r => AuditDb.String(AuditDb.Details(r), "reason")).ToList();
        Assert.Contains("SelfDeactivate", reasons);
        Assert.Contains("SelfRoleChange", reasons);
        Assert.Contains("SelfDelete", reasons);
    }

    [Fact]
    public async Task AC6b_non_admin_cannot_assign_or_remove_the_admin_role()
    {
        // Spec §6.5 / m5: only a system admin may grant or revoke the Admin role.
        _db.RequireAvailable();
        var helperRole = await CreateHelperRoleAsync();
        var adminRoleId = await TestUsers.RoleIdAsync(_db.Factory, "Admin");
        var operatorRoleId = await TestUsers.RoleIdAsync(_db.Factory, "Operator");
        var helper = await TestUsers.CreateAsync(_db.Factory, helperRole.RoleName);
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        var otherAdmin = await TestUsers.CreateAsync(_db.Factory, "Admin");
        using var sp = NewSession();
        await sp.LoginAsync(helper.Username);
        var newName = TestUsers.UniqueName("ac6b");
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var create = await Assert.ThrowsAsync<PermissionDeniedException>(() => sp.Auth().RegisterAsync(new RegisterRequest
        {
            Username = newName, Password = "Test@123", FullName = "Wannabe Admin", RoleId = adminRoleId
        }));
        Assert.Equal("AdminRoleRequired", create.Reason);
        Assert.Contains(SystemRoles.AdminRoleRequirement, create.RequiredPermissions);

        var promote = await Assert.ThrowsAsync<PermissionDeniedException>(() => sp.Auth().UpdateUserAsync(new UpdateUserRequest
        {
            UserId = op.UserId, FullName = op.FullName, RoleId = adminRoleId, IsActive = true
        }));
        Assert.Equal("AdminRoleRequired", promote.Reason);

        var demote = await Assert.ThrowsAsync<PermissionDeniedException>(() => sp.Auth().UpdateUserAsync(new UpdateUserRequest
        {
            UserId = otherAdmin.UserId, FullName = otherAdmin.FullName, RoleId = operatorRoleId, IsActive = true
        }));
        Assert.Equal("AdminRoleRequired", demote.Reason);

        await using (var ctx = _db.CreateContext())
        {
            Assert.False(await ctx.Users.AnyAsync(u => u.Username == newName));
        }

        Assert.Equal(operatorRoleId, (await TestUsers.LoadAsync(_db.Factory, op.UserId)).RoleId);
        Assert.Equal(adminRoleId, (await TestUsers.LoadAsync(_db.Factory, otherAdmin.UserId)).RoleId);

        var denied = await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.AccessDenied);
        Assert.Equal(3, denied.Count);
        Assert.All(denied, r =>
        {
            Assert.Equal(AuditOutcome.Denied, r.Outcome);
            Assert.Equal(helper.UserId, r.UserId);
            Assert.Equal(new[] { "Role:Admin" }, AuditDb.StringArray(AuditDb.Details(r), "requiredPermissions"));
            Assert.Equal("AdminRoleRequired", AuditDb.String(AuditDb.Details(r), "reason"));
        });
        Assert.Empty(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.UserCreate));
        Assert.DoesNotContain(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.UserUpdate), r => r.Outcome == AuditOutcome.Success);
    }

    [Fact]
    public async Task AC6b_non_admin_with_User_Create_can_create_operator_and_cannot_lock_self()
    {
        _db.RequireAvailable();
        var helperRole = await CreateHelperRoleAsync();
        var helper = await TestUsers.CreateAsync(_db.Factory, helperRole.RoleName);
        using var sp = NewSession();
        await sp.LoginAsync(helper.Username);
        var newName = TestUsers.UniqueName("byhelper");
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        Assert.True(await sp.Auth().RegisterAsync(new RegisterRequest
        {
            Username = newName, Password = "Test@123", FullName = "Created By Helper",
            RoleId = await TestUsers.RoleIdAsync(_db.Factory, "Operator")
        }));

        var created = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.UserCreate));
        Assert.Equal(AuditOutcome.Success, created.Outcome);
        Assert.Equal(helper.UserId, created.UserId);

        // AC-6 second half: user X cannot lock X.
        var ex = await Assert.ThrowsAsync<AdminProtectionException>(() => sp.Auth().UpdateUserAsync(new UpdateUserRequest
        {
            UserId = helper.UserId, FullName = helper.FullName, RoleId = helperRole.RoleId, IsActive = false
        }));
        Assert.Equal(AdminProtectionReason.SelfDeactivate, ex.Reason);
        Assert.True((await TestUsers.LoadAsync(_db.Factory, helper.UserId)).IsActive);
    }

    [Fact]
    public async Task System_admin_can_promote_and_demote_when_other_admins_remain()
    {
        _db.RequireAvailable();
        var adminRoleId = await TestUsers.RoleIdAsync(_db.Factory, "Admin");
        var operatorRoleId = await TestUsers.RoleIdAsync(_db.Factory, "Operator");
        var target = await TestUsers.CreateAsync(_db.Factory, "Operator");
        using var sp = NewSession();
        await sp.LoginAdminAsync();
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        Assert.True(await sp.Auth().UpdateUserAsync(new UpdateUserRequest { UserId = target.UserId, FullName = target.FullName, RoleId = adminRoleId, IsActive = true }));
        Assert.Equal(adminRoleId, (await TestUsers.LoadAsync(_db.Factory, target.UserId)).RoleId);
        Assert.True(await sp.Auth().UpdateUserAsync(new UpdateUserRequest { UserId = target.UserId, FullName = target.FullName, RoleId = operatorRoleId, IsActive = true }));
        Assert.Equal(operatorRoleId, (await TestUsers.LoadAsync(_db.Factory, target.UserId)).RoleId);

        var updates = await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.UserUpdate);
        Assert.Equal(2, updates.Count);
        Assert.All(updates, r => Assert.Equal(AuditOutcome.Success, r.Outcome));
        Assert.Equal("Admin", AuditDb.String(AuditDb.Details(updates[0]).GetProperty("after"), "roleName"));
        Assert.Equal("Operator", AuditDb.String(AuditDb.Details(updates[0]).GetProperty("before"), "roleName"));
    }

    [Fact]
    public async Task AC7_admin_self_edit_keeps_permissions_and_reloads_role()
    {
        // AC-7 / R7: the :186 bug replaced CurrentUser.Role with a Role without permissions.
        _db.RequireAvailable();
        var self = await TestUsers.CreateAsync(_db.Factory, "Admin");
        using var sp = NewSession();
        await sp.LoginAsync(self.Username);

        Assert.True(await sp.Auth().UpdateUserAsync(new UpdateUserRequest
        {
            UserId = self.UserId, FullName = "Renamed Admin", RoleId = self.RoleId, IsActive = true
        }));

        Assert.True(sp.Perms().HasPermission(Permissions.UserView));
        Assert.Equal("Renamed Admin", sp.Auth().CurrentUser!.FullName);
        Assert.NotEmpty(sp.Auth().CurrentUser!.Role.RolePermissions);
        Assert.Equal("Renamed Admin", sp.GetRequiredService<ICurrentUserContext>().User!.FullName);
    }

    [Fact]
    public async Task AC7_non_admin_self_edit_keeps_permissions()
    {
        // Stronger variant: a non-admin has no admin bypass, so only a real permission reload keeps User.View.
        _db.RequireAvailable();
        var role = await CreateHelperRoleAsync();
        var self = await TestUsers.CreateAsync(_db.Factory, role.RoleName);
        using var sp = NewSession();
        await sp.LoginAsync(self.Username);
        Assert.True(sp.Perms().HasPermission(Permissions.UserView));

        Assert.True(await sp.Auth().UpdateUserAsync(new UpdateUserRequest
        {
            UserId = self.UserId, FullName = "Renamed Helper", RoleId = role.RoleId, IsActive = true
        }));

        Assert.True(sp.Perms().HasPermission(Permissions.UserView));
        Assert.True(sp.Perms().HasPermission(Permissions.UserEdit));
        Assert.False(sp.Perms().HasPermission(Permissions.UserDelete));
        Assert.Equal("Renamed Helper", sp.Auth().CurrentUser!.FullName);
    }

    [Fact]
    public async Task AC8_login_success_and_wrong_password_are_audited_without_the_password()
    {
        _db.RequireAvailable();
        const string wrongPassword = "WrongPass#4711";
        var user = await TestUsers.CreateAsync(_db.Factory, "Operator");
        using var sp = NewSession();
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        await sp.LoginAsync(user.Username);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            sp.Auth().LoginAsync(new LoginRequest { Username = user.Username, Password = wrongPassword }));

        var rows = await AuditDb.RowsAfterAsync(_db.Factory, idBefore);
        var success = Assert.Single(rows, r => r.Action == AuditActions.AuthLoginSuccess);
        Assert.Equal(AuditOutcome.Success, success.Outcome);
        Assert.Equal(user.UserId, success.UserId);
        Assert.Equal(user.Username, success.Username);
        Assert.Equal("Operator", success.RoleName);

        var failed = Assert.Single(rows, r => r.Action == AuditActions.AuthLoginFailed);
        Assert.Equal(AuditOutcome.Failed, failed.Outcome);
        Assert.Equal(user.UserId, failed.UserId);
        var details = AuditDb.Details(failed);
        Assert.Equal(user.Username, AuditDb.String(details, "attemptedUsername"));
        Assert.Equal("InvalidPassword", AuditDb.String(details, "reason"));

        Assert.All(rows, r => AssertNoSecrets(r, wrongPassword, TestUsers.DefaultPassword));
    }

    [Fact]
    public async Task E3_unknown_username_is_audited_with_null_user_id_and_normalized_name()
    {
        _db.RequireAvailable();
        using var sp = NewSession();
        var attempted = "  Ghost_" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant() + "  ";
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            sp.Auth().LoginAsync(new LoginRequest { Username = attempted, Password = "Whatever#1" }));

        var row = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore));
        Assert.Equal(AuditActions.AuthLoginFailed, row.Action);
        Assert.Equal(AuditOutcome.Failed, row.Outcome);
        Assert.Null(row.UserId);
        Assert.Equal(attempted.Trim().ToLowerInvariant(), row.Username); // A11
        Assert.Equal(string.Empty, row.RoleName);
        Assert.Equal("UnknownUser", AuditDb.String(AuditDb.Details(row), "reason"));
        Assert.Equal(attempted.Trim().ToLowerInvariant(), AuditDb.String(AuditDb.Details(row), "attemptedUsername"));
        AssertNoSecrets(row, "Whatever#1");
        Assert.False(sp.Auth().IsLoggedIn);
    }

    [Fact]
    public async Task Inactive_user_login_is_audited_with_reason_Inactive()
    {
        _db.RequireAvailable();
        var user = await TestUsers.CreateAsync(_db.Factory, "Operator", isActive: false);
        using var sp = NewSession();
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sp.Auth().LoginAsync(new LoginRequest { Username = user.Username, Password = TestUsers.DefaultPassword }));

        var row = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore));
        Assert.Equal(AuditActions.AuthLoginFailed, row.Action);
        Assert.Equal(user.UserId, row.UserId);
        Assert.Equal("Inactive", AuditDb.String(AuditDb.Details(row), "reason"));
        Assert.False(sp.Perms().IsAuthenticated);
    }

    [Fact]
    public async Task Logout_is_audited_and_clears_all_permissions()
    {
        _db.RequireAvailable();
        var user = await TestUsers.CreateAsync(_db.Factory, "Operator");
        using var sp = NewSession();
        await sp.LoginAsync(user.Username);
        Assert.True(sp.Perms().HasPermission(Permissions.ParkingView));
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        await sp.Auth().LogoutAsync();

        var row = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore));
        Assert.Equal(AuditActions.AuthLogout, row.Action);
        Assert.Equal(AuditOutcome.Success, row.Outcome);
        Assert.Equal(user.UserId, row.UserId);
        Assert.Null(sp.Auth().CurrentUser);
        Assert.False(sp.Perms().IsAuthenticated);
        Assert.False(sp.Perms().HasPermission(Permissions.ParkingView));
    }

    [Fact]
    public async Task User_create_update_delete_write_success_rows_with_spec_details_and_no_secrets()
    {
        // R14 + §2.6 detail shapes; R18 (no hash, only passwordChanged).
        _db.RequireAvailable();
        using var sp = NewSession();
        var admin = await sp.LoginAdminAsync();
        var operatorRoleId = await TestUsers.RoleIdAsync(_db.Factory, "Operator");
        var username = TestUsers.UniqueName("crud");
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        Assert.True(await sp.Auth().RegisterAsync(new RegisterRequest
        {
            Username = username, Password = "First#Pass1", FullName = "Crud User", RoleId = operatorRoleId
        }));
        var created = await TestUsers.LoadByUsernameAsync(_db.Factory, username);

        Assert.True(await sp.Auth().UpdateUserAsync(new UpdateUserRequest
        {
            UserId = created.UserId, FullName = "Crud User Renamed", RoleId = operatorRoleId, IsActive = true, NewPassword = "Second#Pass2"
        }));
        Assert.True(await sp.Auth().DeleteUserAsync(created.UserId));

        await using (var ctx = _db.CreateContext())
        {
            Assert.False(await ctx.Users.AnyAsync(u => u.UserId == created.UserId));
        }

        var rows = await AuditDb.RowsAfterAsync(_db.Factory, idBefore);
        var create = Assert.Single(rows, r => r.Action == AuditActions.UserCreate);
        var update = Assert.Single(rows, r => r.Action == AuditActions.UserUpdate);
        var delete = Assert.Single(rows, r => r.Action == AuditActions.UserDelete);
        foreach (var row in new[] { create, update, delete })
        {
            Assert.Equal(AuditOutcome.Success, row.Outcome);
            Assert.Equal("User", row.EntityType);
            Assert.Equal(created.UserId.ToString(), row.EntityId);
            Assert.Equal(admin.UserId, row.UserId);
            AssertNoSecrets(row, "First#Pass1", "Second#Pass2");
        }

        AuditDb.HasKeys(create, "username", "fullName", "roleId", "roleName", "isActive");
        Assert.Equal("Operator", AuditDb.String(AuditDb.Details(create), "roleName"));

        AuditDb.HasKeys(update, "before", "after", "passwordChanged");
        var u = AuditDb.Details(update);
        Assert.True(u.GetProperty("passwordChanged").GetBoolean());
        Assert.Equal("Crud User", AuditDb.String(u.GetProperty("before"), "fullName"));
        Assert.Equal("Crud User Renamed", AuditDb.String(u.GetProperty("after"), "fullName"));

        AuditDb.HasKeys(delete, "username", "fullName", "roleName");
        Assert.Equal(username, AuditDb.String(AuditDb.Details(delete), "username"));
    }

    [Fact]
    public async Task A9_duplicate_username_is_rejected_before_the_transaction_and_not_audited()
    {
        _db.RequireAvailable();
        var existing = await TestUsers.CreateAsync(_db.Factory, "Operator");
        using var sp = NewSession();
        await sp.LoginAdminAsync();
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sp.Auth().RegisterAsync(new RegisterRequest
        {
            Username = existing.Username, Password = "Test@123", FullName = "Dup", RoleId = existing.RoleId
        }));

        Assert.Empty(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.UserCreate));
    }

    [Fact]
    public async Task E6_deleting_user_with_history_gives_friendly_error_and_failed_audit()
    {
        _db.RequireAvailable();
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        using (var opSession = NewSession())
        {
            await opSession.LoginAsync(op.Username);
            await ParkingFlows.OpenShiftAsync(opSession, op.UserId);
        }

        using var sp = NewSession();
        await sp.LoginAdminAsync();
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var ex = await Assert.ThrowsAsync<UserHasHistoryException>(() => sp.Auth().DeleteUserAsync(op.UserId));

        Assert.Equal(op.UserId, ex.UserId);
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
        await using (var ctx = _db.CreateContext())
        {
            Assert.True(await ctx.Users.AnyAsync(u => u.UserId == op.UserId));
        }

        var row = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.UserDelete));
        Assert.Equal(AuditOutcome.Failed, row.Outcome);
        Assert.Equal(op.UserId.ToString(), row.EntityId);
    }

    [Fact]
    public async Task E5_locked_user_keeps_loaded_permissions_until_next_login()
    {
        // E5 (accepted behaviour): locking takes effect at the next login.
        _db.RequireAvailable();
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        using var opSession = NewSession();
        await opSession.LoginAsync(op.Username);
        using var adminSession = NewSession();
        await adminSession.LoginAdminAsync();

        Assert.True(await adminSession.Auth().UpdateUserAsync(new UpdateUserRequest
        {
            UserId = op.UserId, FullName = op.FullName, RoleId = op.RoleId, IsActive = false
        }));

        Assert.True(opSession.Perms().HasPermission(Permissions.ParkingCheckIn));
        using var nextLogin = NewSession();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            nextLogin.Auth().LoginAsync(new LoginRequest { Username = op.Username, Password = TestUsers.DefaultPassword }));
    }

    private async Task<List<(int UserId, bool IsActive)>> SnapshotAdminActivityAsync(int adminRoleId)
    {
        await using var ctx = _db.CreateContext();
        var rows = await ctx.Users.AsNoTracking().Where(u => u.RoleId == adminRoleId).Select(u => new { u.UserId, u.IsActive }).ToListAsync();
        return rows.Select(r => (r.UserId, r.IsActive)).ToList();
    }

    private async Task RestoreAdminActivityAsync(List<(int UserId, bool IsActive)> snapshot)
    {
        foreach (var (userId, isActive) in snapshot)
        {
            await _db.ExecuteAsync("UPDATE \"Users\" SET \"IsActive\" = @a WHERE \"UserId\" = @id", ("a", isActive), ("id", userId));
        }
    }
}
