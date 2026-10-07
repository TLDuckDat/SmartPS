using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SmartPS.Data;

namespace SmartPS.Tests.Integration;

/// <summary>
/// AC-15 / N2 / spec §6.1 and §6.6: the AddRbacAndAuditTrail migration keeps legacy data, adds the 5 permissions,
/// grants Admin everything and Manager the new defaults; Down() removes table, triggers, function and permissions and
/// restores Manager to exactly Shift.*. Runs on sibling databases (A10). Also the seed CTE behaviour (m4).
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class MigrationTests : IClassFixture<PostgresDatabaseFixture>
{
    private const string PreviousMigration = "20261004152712_AddShiftManagement";
    private const string LegacyAdminHash = "$2a$11$4zQCT6o4m1i7fStbvC18teLVORe5LvV5BscyAQW/.QPh.bWeOKpgW";

    private readonly PostgresDatabaseFixture _db;

    public MigrationTests(PostgresDatabaseFixture db)
    {
        _db = db;
    }

    private static async Task MigrateToAsync(string connectionString, string? target)
    {
        await using var ctx = PostgresDatabaseFixture.CreateContext(connectionString);
        var migrator = ctx.GetService<IMigrator>();
        await migrator.MigrateAsync(target);
    }

    private static async Task ExecAsync(string connectionString, string sql)
    {
        await using var ctx = PostgresDatabaseFixture.CreateContext(connectionString);
        await ctx.Database.ExecuteSqlRawAsync(sql);
    }

    private static async Task<HashSet<string>> GrantsAsync(string connectionString, string roleName)
    {
        await using var ctx = PostgresDatabaseFixture.CreateContext(connectionString);
        var names = await ctx.RolePermissions
            .Where(rp => rp.Role.RoleName == roleName)
            .Select(rp => rp.Permission.PermissionName)
            .ToListAsync();
        return names.ToHashSet(StringComparer.Ordinal);
    }

    private static async Task<HashSet<string>> PermissionNamesAsync(string connectionString)
    {
        await using var ctx = PostgresDatabaseFixture.CreateContext(connectionString);
        return (await ctx.Permissions.Select(p => p.PermissionName).ToListAsync()).ToHashSet(StringComparer.Ordinal);
    }

    private static Task<long> CountAsync(string connectionString, string sql)
        => PostgresDatabaseFixture.ScalarAsync<long>(connectionString, sql);

    /// <summary>
    /// Legacy data as it existed at fb7303a (18 permissions, Manager = Shift.*, Operator = 7 grants).
    /// AddShiftManagement already inserts the 5 Shift.* permissions (and grants them to existing Admin/Manager roles),
    /// so the setup tolerates rows that the earlier migrations created.
    /// </summary>
    private static async Task InsertLegacyDataAsync(string cs, bool preInsertAuditView = false)
    {
        var permissionValues = string.Join(",", TestUsers.LegacyPermissions.Select(p => $"('{p}','{p}')"));
        await ExecAsync(cs, $"INSERT INTO \"Permissions\" (\"PermissionName\",\"Description\") VALUES {permissionValues} ON CONFLICT (\"PermissionName\") DO NOTHING;");
        if (preInsertAuditView)
        {
            await ExecAsync(cs, "INSERT INTO \"Permissions\" (\"PermissionName\",\"Description\") VALUES ('Audit.View','pre-existing');");
        }

        await ExecAsync(cs, "INSERT INTO \"Roles\" (\"RoleName\",\"Description\") VALUES ('Admin','a'),('Manager','m'),('Operator','o');");
        await ExecAsync(cs, """
            INSERT INTO "RolePermissions" ("RoleId","PermissionId")
            SELECT r."RoleId", p."PermissionId" FROM "Roles" r CROSS JOIN "Permissions" p WHERE r."RoleName"='Admin'
            ON CONFLICT ("RoleId","PermissionId") DO NOTHING;
            INSERT INTO "RolePermissions" ("RoleId","PermissionId")
            SELECT r."RoleId", p."PermissionId" FROM "Roles" r JOIN "Permissions" p ON p."PermissionName" LIKE 'Shift.%' WHERE r."RoleName"='Manager'
            ON CONFLICT ("RoleId","PermissionId") DO NOTHING;
            INSERT INTO "RolePermissions" ("RoleId","PermissionId")
            SELECT r."RoleId", p."PermissionId" FROM "Roles" r JOIN "Permissions" p
              ON p."PermissionName" IN ('Parking.View','Parking.CheckIn','Parking.CheckOut','Report.View','Shift.View','Shift.Open','Shift.Close')
            WHERE r."RoleName"='Operator'
            ON CONFLICT ("RoleId","PermissionId") DO NOTHING;
            """);
        await ExecAsync(cs, $"""
            INSERT INTO "Users" ("Username","PasswordHash","FullName","RoleId","IsActive")
            SELECT 'legacy_admin', '{LegacyAdminHash}', 'Legacy Admin', r."RoleId", true FROM "Roles" r WHERE r."RoleName"='Admin';
            INSERT INTO "Users" ("Username","PasswordHash","FullName","RoleId","IsActive")
            SELECT 'legacy_op', '{LegacyAdminHash}', 'Legacy Operator', r."RoleId", false FROM "Roles" r WHERE r."RoleName"='Operator';
            """);
    }

    private static async Task AssertLegacyUsersIntactAsync(string cs)
    {
        await using var ctx = PostgresDatabaseFixture.CreateContext(cs);
        var users = await ctx.Users.AsNoTracking().Include(u => u.Role).OrderBy(u => u.Username).ToListAsync();
        Assert.Equal(new[] { "legacy_admin", "legacy_op" }, users.Select(u => u.Username).ToArray());
        Assert.All(users, u => Assert.Equal(LegacyAdminHash, u.PasswordHash));
        Assert.Equal("Admin", users[0].Role.RoleName);
        Assert.True(users[0].IsActive);
        Assert.Equal("Operator", users[1].Role.RoleName);
        Assert.False(users[1].IsActive);
        Assert.Equal(3, await ctx.Roles.CountAsync());
    }

    [Fact]
    public async Task AC15_up_keeps_legacy_data_and_adds_permissions_then_down_restores_previous_state()
    {
        _db.RequireAvailable();
        var cs = await _db.CreateSiblingDatabaseAsync("migration");

        // Given a database at the previous migration with legacy data
        await MigrateToAsync(cs, PreviousMigration);
        await InsertLegacyDataAsync(cs);

        // When the new migration runs
        await MigrateToAsync(cs, null);

        // Then legacy users/roles are intact, 5 permissions are added, Admin has all, Manager has the §6.1 defaults
        await AssertLegacyUsersIntactAsync(cs);
        var permissions = await PermissionNamesAsync(cs);
        Assert.Equal(23, permissions.Count);
        Assert.Superset(TestUsers.NewPermissions.ToHashSet(), permissions);
        Assert.Equal(permissions, await GrantsAsync(cs, "Admin"));
        Assert.Equal(TestUsers.ManagerDefaultPermissions.ToHashSet(StringComparer.Ordinal), await GrantsAsync(cs, "Manager"));
        Assert.Equal(TestUsers.OperatorSeedPermissions.ToHashSet(StringComparer.Ordinal), await GrantsAsync(cs, "Operator"));

        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM pg_tables WHERE tablename = 'AuditLogs'"));
        Assert.Equal(3, await CountAsync(cs,
            "SELECT count(*) FROM pg_trigger WHERE tgname IN ('TR_AuditLogs_NoUpdate','TR_AuditLogs_NoDelete','TR_AuditLogs_NoTruncate') AND NOT tgisinternal"));
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM pg_proc WHERE proname = 'fn_AuditLogs_BlockMutation'"));
        var lastMigration = await PostgresDatabaseFixture.ScalarAsync<string>(cs,
            "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\" DESC LIMIT 1");
        Assert.EndsWith("_AddRbacAndAuditTrail", lastMigration, StringComparison.Ordinal);

        // When Down() runs
        await MigrateToAsync(cs, PreviousMigration);

        // Then the table, triggers, function and the 5 permissions are gone and Manager is exactly Shift.* again
        Assert.Equal(0, await CountAsync(cs, "SELECT count(*) FROM pg_tables WHERE tablename = 'AuditLogs'"));
        Assert.Equal(0, await CountAsync(cs, "SELECT count(*) FROM pg_proc WHERE proname = 'fn_AuditLogs_BlockMutation'"));
        Assert.Equal(0, await CountAsync(cs, "SELECT count(*) FROM pg_trigger WHERE tgname LIKE 'TR_AuditLogs_%'"));
        var permissionsAfterDown = await PermissionNamesAsync(cs);
        Assert.Equal(TestUsers.LegacyPermissions.ToHashSet(StringComparer.Ordinal), permissionsAfterDown);
        Assert.Equal(
            new HashSet<string>(StringComparer.Ordinal) { "Shift.View", "Shift.Open", "Shift.Close", "Shift.Review", "Shift.Adjust" },
            await GrantsAsync(cs, "Manager"));
        Assert.Equal(TestUsers.LegacyPermissions.ToHashSet(StringComparer.Ordinal), await GrantsAsync(cs, "Admin"));
        Assert.Equal(TestUsers.OperatorSeedPermissions.ToHashSet(StringComparer.Ordinal), await GrantsAsync(cs, "Operator"));
        await AssertLegacyUsersIntactAsync(cs);

        // And Up can be applied again after Down
        await MigrateToAsync(cs, null);
        Assert.Equal(23, (await PermissionNamesAsync(cs)).Count);
    }

    [Fact]
    public async Task N2_up_is_idempotent_when_a_new_permission_already_exists()
    {
        _db.RequireAvailable();
        var cs = await _db.CreateSiblingDatabaseAsync("idem");
        await MigrateToAsync(cs, PreviousMigration);
        await InsertLegacyDataAsync(cs, preInsertAuditView: true);

        await MigrateToAsync(cs, null);

        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM \"Permissions\" WHERE \"PermissionName\" = 'Audit.View'"));
        Assert.Equal(23, (await PermissionNamesAsync(cs)).Count);
        Assert.Contains("Audit.View", await GrantsAsync(cs, "Admin"));
        Assert.Contains("Audit.View", await GrantsAsync(cs, "Manager"));
        await AssertLegacyUsersIntactAsync(cs);
    }

    [Fact]
    public async Task Fresh_database_seed_matches_spec_6_1_and_is_rerunnable()
    {
        // Fixture DB = MigrateAsync + seed on an empty database.
        _db.RequireAvailable();

        Assert.Equal(23, (await PermissionNamesAsync(_db.ConnectionString)).Count);
        Assert.Equal(23, (await GrantsAsync(_db.ConnectionString, "Admin")).Count);
        Assert.Equal(TestUsers.ManagerDefaultPermissions.ToHashSet(StringComparer.Ordinal), await GrantsAsync(_db.ConnectionString, "Manager"));
        Assert.Equal(TestUsers.OperatorSeedPermissions.ToHashSet(StringComparer.Ordinal), await GrantsAsync(_db.ConnectionString, "Operator"));

        var grantsBefore = await CountAsync(_db.ConnectionString, "SELECT count(*) FROM \"RolePermissions\"");
        await using (var ctx = _db.CreateContext())
        {
            await DbInitializer.InitializeAsync(ctx);
        }

        Assert.Equal(grantsBefore, await CountAsync(_db.ConnectionString, "SELECT count(*) FROM \"RolePermissions\""));
        Assert.Equal(1, await CountAsync(_db.ConnectionString, "SELECT count(*) FROM \"Users\" WHERE \"Username\" = 'admin'"));
    }

    [Fact]
    public async Task M4_seed_does_not_regrant_removed_permissions_to_existing_roles()
    {
        // m4: a grant removed through the matrix must not come back on the next app start (seed re-run).
        _db.RequireAvailable();
        var cs = await _db.CreateSiblingDatabaseAsync("seed");
        await using (var ctx = PostgresDatabaseFixture.CreateContext(cs))
        {
            await DbInitializer.InitializeAsync(ctx);
        }

        await ExecAsync(cs, """
            DELETE FROM "RolePermissions" rp USING "Roles" r, "Permissions" p
            WHERE rp."RoleId" = r."RoleId" AND rp."PermissionId" = p."PermissionId"
              AND ((r."RoleName" = 'Operator' AND p."PermissionName" = 'Report.View')
                OR (r."RoleName" = 'Manager' AND p."PermissionName" = 'Audit.View'));
            """);

        await using (var ctx = PostgresDatabaseFixture.CreateContext(cs))
        {
            await DbInitializer.InitializeAsync(ctx);
        }

        Assert.DoesNotContain("Report.View", await GrantsAsync(cs, "Operator"));
        Assert.DoesNotContain("Audit.View", await GrantsAsync(cs, "Manager"));
        Assert.Equal(23, (await GrantsAsync(cs, "Admin")).Count);
    }
}
