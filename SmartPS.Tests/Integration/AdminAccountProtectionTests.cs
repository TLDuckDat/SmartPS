using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartPS.DTOs.Auth;
using SmartPS.Models.Auth;
using SmartPS.Services.Auth;

namespace SmartPS.Tests.Integration;

/// <summary>
/// Fix round 1 — FX1 (spec §6.9, challenge CH1/CH2): any change to an account that holds the Admin role
/// (password, lock/unlock, full name, role) or deleting it requires the actor to be a system Admin.
/// FX3 (CH4): the admin-target check is re-evaluated inside the audited transaction (TOCTOU).
/// Denial = PermissionDeniedException(Reason "AdminRoleRequired", RequiredPermissions ["Role:Admin"]) + one ACCESS_DENIED row.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class AdminAccountProtectionTests : IClassFixture<PostgresDatabaseFixture>
{
    private readonly PostgresDatabaseFixture _db;

    public AdminAccountProtectionTests(PostgresDatabaseFixture db)
    {
        _db = db;
    }

    /// <summary>Non-admin role holding every user-management permission (the strongest non-admin actor).</summary>
    private async Task<User> CreateUserManagerAsync()
    {
        var role = await TestUsers.CreateRoleAsync(_db.Factory, TestUsers.UniqueName("usermgr"),
            Permissions.UserView, Permissions.UserCreate, Permissions.UserEdit, Permissions.UserDelete);
        return await TestUsers.CreateAsync(_db.Factory, role.RoleName);
    }

    private async Task AssertAdminRoleDeniedAsync(PermissionDeniedException ex, long idBefore, User actor, int targetUserId)
    {
        Assert.Equal("AdminRoleRequired", ex.Reason);
        Assert.Equal(new[] { SystemRoles.AdminRoleRequirement }, ex.RequiredPermissions);

        var rows = await AuditDb.RowsAfterAsync(_db.Factory, idBefore);
        var denied = Assert.Single(rows, r => r.Action == AuditActions.AccessDenied);
        Assert.Equal(AuditOutcome.Denied, denied.Outcome);
        Assert.Equal(actor.UserId, denied.UserId);
        Assert.Equal(targetUserId.ToString(), denied.EntityId);
        Assert.Equal(new[] { "Role:Admin" }, AuditDb.StringArray(AuditDb.Details(denied), "requiredPermissions"));
        Assert.Equal("AdminRoleRequired", AuditDb.String(AuditDb.Details(denied), "reason"));
        Assert.DoesNotContain(rows, r => r.Outcome == AuditOutcome.Success &&
                                         (r.Action == AuditActions.UserUpdate || r.Action == AuditActions.UserDelete));
    }

    [Fact]
    public async Task FX1_CH1_non_admin_cannot_reset_an_admin_password()
    {
        _db.RequireAvailable();
        var actor = await CreateUserManagerAsync();
        var victim = await TestUsers.CreateAsync(_db.Factory, "Admin");
        var hashBefore = await _db.ScalarAsync<string>("SELECT \"PasswordHash\" FROM \"Users\" WHERE \"UserId\" = @id", ("id", victim.UserId));
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(actor.Username);
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var ex = await Assert.ThrowsAsync<PermissionDeniedException>(() => sp.Auth().UpdateUserAsync(new UpdateUserRequest
        {
            UserId = victim.UserId, FullName = victim.FullName, RoleId = victim.RoleId, IsActive = true, NewPassword = "Pwned@123"
        }));

        await AssertAdminRoleDeniedAsync(ex, idBefore, actor, victim.UserId);
        Assert.Equal(hashBefore, await _db.ScalarAsync<string>("SELECT \"PasswordHash\" FROM \"Users\" WHERE \"UserId\" = @id", ("id", victim.UserId)));
        using var attacker = IntegrationServices.Create(_db);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            attacker.Auth().LoginAsync(new LoginRequest { Username = victim.Username, Password = "Pwned@123" }));
    }

    [Fact]
    public async Task FX1_CH2_non_admin_cannot_lock_an_admin_even_when_other_admins_exist()
    {
        _db.RequireAvailable();
        var actor = await CreateUserManagerAsync();
        var victim = await TestUsers.CreateAsync(_db.Factory, "Admin");
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(actor.Username);
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var ex = await Assert.ThrowsAsync<PermissionDeniedException>(() => sp.Auth().UpdateUserAsync(new UpdateUserRequest
        {
            UserId = victim.UserId, FullName = victim.FullName, RoleId = victim.RoleId, IsActive = false
        }));

        await AssertAdminRoleDeniedAsync(ex, idBefore, actor, victim.UserId);
        Assert.True((await TestUsers.LoadAsync(_db.Factory, victim.UserId)).IsActive);
    }

    [Fact]
    public async Task FX1_non_admin_cannot_unlock_an_inactive_admin()
    {
        _db.RequireAvailable();
        var actor = await CreateUserManagerAsync();
        var victim = await TestUsers.CreateAsync(_db.Factory, "Admin", isActive: false);
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(actor.Username);
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var ex = await Assert.ThrowsAsync<PermissionDeniedException>(() => sp.Auth().UpdateUserAsync(new UpdateUserRequest
        {
            UserId = victim.UserId, FullName = victim.FullName, RoleId = victim.RoleId, IsActive = true
        }));

        await AssertAdminRoleDeniedAsync(ex, idBefore, actor, victim.UserId);
        Assert.False((await TestUsers.LoadAsync(_db.Factory, victim.UserId)).IsActive);
    }

    [Fact]
    public async Task FX1_non_admin_cannot_rename_an_admin()
    {
        _db.RequireAvailable();
        var actor = await CreateUserManagerAsync();
        var victim = await TestUsers.CreateAsync(_db.Factory, "Admin");
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(actor.Username);
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var ex = await Assert.ThrowsAsync<PermissionDeniedException>(() => sp.Auth().UpdateUserAsync(new UpdateUserRequest
        {
            UserId = victim.UserId, FullName = "Renamed By Non Admin", RoleId = victim.RoleId, IsActive = true
        }));

        await AssertAdminRoleDeniedAsync(ex, idBefore, actor, victim.UserId);
        Assert.Equal(victim.FullName, (await TestUsers.LoadAsync(_db.Factory, victim.UserId)).FullName);
    }

    [Fact]
    public async Task FX1_non_admin_cannot_delete_an_admin()
    {
        _db.RequireAvailable();
        var actor = await CreateUserManagerAsync();
        var victim = await TestUsers.CreateAsync(_db.Factory, "Admin");
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(actor.Username);
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var ex = await Assert.ThrowsAsync<PermissionDeniedException>(() => sp.Auth().DeleteUserAsync(victim.UserId));

        await AssertAdminRoleDeniedAsync(ex, idBefore, actor, victim.UserId);
        await using var ctx = _db.CreateContext();
        Assert.True(await ctx.Users.AnyAsync(u => u.UserId == victim.UserId));
    }

    [Fact]
    public async Task FX1_non_admin_can_still_edit_non_admin_accounts()
    {
        // Positive control: the new rule only concerns Admin-role accounts.
        _db.RequireAvailable();
        var actor = await CreateUserManagerAsync();
        var target = await TestUsers.CreateAsync(_db.Factory, "Operator");
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(actor.Username);

        Assert.True(await sp.Auth().UpdateUserAsync(new UpdateUserRequest
        {
            UserId = target.UserId, FullName = "Edited Operator", RoleId = target.RoleId, IsActive = false, NewPassword = "Reset#123"
        }));

        var after = await TestUsers.LoadAsync(_db.Factory, target.UserId);
        Assert.Equal("Edited Operator", after.FullName);
        Assert.False(after.IsActive);
    }

    [Fact]
    public async Task FX1_system_admin_can_reset_another_admins_password_and_lock_it()
    {
        _db.RequireAvailable();
        var victim = await TestUsers.CreateAsync(_db.Factory, "Admin");
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAdminAsync();

        Assert.True(await sp.Auth().UpdateUserAsync(new UpdateUserRequest
        {
            UserId = victim.UserId, FullName = victim.FullName, RoleId = victim.RoleId, IsActive = true, NewPassword = "Rotated#123"
        }));
        Assert.True(await sp.Auth().UpdateUserAsync(new UpdateUserRequest
        {
            UserId = victim.UserId, FullName = victim.FullName, RoleId = victim.RoleId, IsActive = false
        }));

        Assert.False((await TestUsers.LoadAsync(_db.Factory, victim.UserId)).IsActive);
    }

    [Fact]
    public async Task FX3_CH4_target_flipped_to_admin_before_the_transaction_is_denied_inside_it()
    {
        // TOCTOU: the pre-check sees an Operator; just before the audited transaction the target becomes Admin.
        _db.RequireAvailable();
        var actor = await CreateUserManagerAsync();
        var target = await TestUsers.CreateAsync(_db.Factory, "Operator");
        var adminRoleId = await TestUsers.RoleIdAsync(_db.Factory, "Admin");
        var managerRoleId = await TestUsers.RoleIdAsync(_db.Factory, "Manager");
        var cs = _db.ConnectionString;

        void FlipTargetToAdmin()
        {
            using var conn = new NpgsqlConnection(cs);
            conn.Open();
            using var cmd = new NpgsqlCommand("UPDATE \"Users\" SET \"RoleId\" = @r WHERE \"UserId\" = @u", conn);
            cmd.Parameters.AddWithValue("r", adminRoleId);
            cmd.Parameters.AddWithValue("u", target.UserId);
            cmd.ExecuteNonQuery();
        }

        using var sp = IntegrationServices.Create(_db, IntegrationServices.HookBeforeBegin(FlipTargetToAdmin));
        await sp.LoginAsync(actor.Username);
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var ex = await Assert.ThrowsAsync<PermissionDeniedException>(() => sp.Auth().UpdateUserAsync(new UpdateUserRequest
        {
            UserId = target.UserId, FullName = target.FullName, RoleId = managerRoleId, IsActive = true
        }));

        await AssertAdminRoleDeniedAsync(ex, idBefore, actor, target.UserId);
        Assert.Equal(adminRoleId, (await TestUsers.LoadAsync(_db.Factory, target.UserId)).RoleId);
        Assert.False(AuditService.IsLockHeldInCurrentFlow);
    }

    [Fact]
    public async Task FX3_target_flipped_to_admin_before_delete_transaction_is_denied_inside_it()
    {
        _db.RequireAvailable();
        var actor = await CreateUserManagerAsync();
        var target = await TestUsers.CreateAsync(_db.Factory, "Operator");
        var adminRoleId = await TestUsers.RoleIdAsync(_db.Factory, "Admin");
        var cs = _db.ConnectionString;

        void FlipTargetToAdmin()
        {
            using var conn = new NpgsqlConnection(cs);
            conn.Open();
            using var cmd = new NpgsqlCommand("UPDATE \"Users\" SET \"RoleId\" = @r WHERE \"UserId\" = @u", conn);
            cmd.Parameters.AddWithValue("r", adminRoleId);
            cmd.Parameters.AddWithValue("u", target.UserId);
            cmd.ExecuteNonQuery();
        }

        using var sp = IntegrationServices.Create(_db, IntegrationServices.HookBeforeBegin(FlipTargetToAdmin));
        await sp.LoginAsync(actor.Username);
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var ex = await Assert.ThrowsAsync<PermissionDeniedException>(() => sp.Auth().DeleteUserAsync(target.UserId));

        await AssertAdminRoleDeniedAsync(ex, idBefore, actor, target.UserId);
        await using var ctx = _db.CreateContext();
        Assert.True(await ctx.Users.AnyAsync(u => u.UserId == target.UserId));
    }
}
