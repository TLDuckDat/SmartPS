using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SmartPS.Data;
using SmartPS.Models.Parking;

namespace SmartPS.Tests.Integration;

/// <summary>
/// SQL Server baseline (ConfigureSqlServerDatabase + InitialSqlServer) replaces the PostgreSQL migration chain:
/// chain shape and no pending model changes; database settings (collation, RCSI); hand-written objects (plate
/// normalizer, append-only trigger, truncate guard); indexes the gate and reports rely on; the seed (spec §6.1,
/// rerunnable, m4: removed grants stay removed); Down to an empty schema and Up again.
/// </summary>
[Collection(DatabaseCollection.Name)]
[Trait("Category", "Integration")]
public class SqlServerMigrationTests : IClassFixture<SqlServerDatabaseFixture>
{
    private readonly SqlServerDatabaseFixture _db;

    public SqlServerMigrationTests(SqlServerDatabaseFixture db)
    {
        _db = db;
    }

    private static async Task MigrateToAsync(string connectionString, string? target)
    {
        await using var ctx = SqlServerDatabaseFixture.CreateContext(connectionString);
        await ctx.GetService<IMigrator>().MigrateAsync(target);
    }

    private static async Task ExecAsync(string connectionString, string sql)
    {
        await using var ctx = SqlServerDatabaseFixture.CreateContext(connectionString);
        await ctx.Database.ExecuteSqlRawAsync(sql);
    }

    private static Task<long> CountAsync(string cs, string sql) => SqlServerDatabaseFixture.ScalarAsync<long>(cs, sql);

    private static async Task<HashSet<string>> GrantsAsync(string connectionString, string roleName)
    {
        await using var ctx = SqlServerDatabaseFixture.CreateContext(connectionString);
        var names = await ctx.RolePermissions
            .Where(rp => rp.Role.RoleName == roleName)
            .Select(rp => rp.Permission.PermissionName)
            .ToListAsync();
        return names.ToHashSet(StringComparer.Ordinal);
    }

    private static Task<long> IndexCountAsync(string cs, string table, string index)
        => CountAsync(cs, $"SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.{table}') AND name = '{index}'");

    // ---- chain shape ----------------------------------------------------------------------------------------------------

    [Fact]
    public void Baseline_is_configure_then_initial_and_the_model_has_no_pending_changes()
    {
        using var ctx = SqlServerDatabaseFixture.CreateContext(SqlServerDatabaseFixture.DefaultConnectionString);
        var migrations = ctx.Database.GetMigrations().ToList();

        Assert.Equal(2, migrations.Count);
        Assert.EndsWith("_ConfigureSqlServerDatabase", migrations[0], StringComparison.Ordinal);
        Assert.EndsWith("_InitialSqlServer", migrations[1], StringComparison.Ordinal);
        Assert.False(ctx.Database.HasPendingModelChanges(), "dotnet ef migrations has-pending-model-changes must report none");
    }

    [Fact]
    public void Model_keeps_the_gate_and_report_indexes()
    {
        using var ctx = SqlServerDatabaseFixture.CreateContext(SqlServerDatabaseFixture.DefaultConnectionString);
        var session = ctx.Model.FindEntityType(typeof(ParkingSession))!;

        Assert.Single(session.GetIndexes(), i => i.GetDatabaseName() == "IX_ParkingSessions_CheckOutTime" && !i.IsUnique);
        Assert.Single(session.GetIndexes(), i => i.GetDatabaseName() == "IX_ParkingSessions_SlotId_Active" && i.IsUnique
                                                 && i.GetFilter() == "[Status] = 0 AND [SlotId] IS NOT NULL");
    }

    // ---- fresh database (fixture = empty DB configured, migrated + seeded) ----------------------------------------------

    [Fact]
    public async Task Fresh_database_is_case_sensitive_vietnamese_with_read_committed_snapshot()
    {
        _db.RequireAvailable();
        var cs = _db.ConnectionString;

        Assert.Equal(SmartPsDbContext.DatabaseCollation,
            await SqlServerDatabaseFixture.ScalarAsync<string>(cs, "SELECT CONVERT(nvarchar(128), DATABASEPROPERTYEX(DB_NAME(), 'Collation'))"));
        Assert.True(await SqlServerDatabaseFixture.ScalarAsync<bool>(cs,
            "SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = DB_NAME()"));
        // Phân biệt hoa/thường như PostgreSQL
        Assert.Equal(0, await CountAsync(cs, "SELECT COUNT(*) FROM \"Users\" WHERE \"Username\" = 'ADMIN'"));
        Assert.Equal(1, await CountAsync(cs, "SELECT COUNT(*) FROM \"Users\" WHERE \"Username\" = 'admin'"));
    }

    [Theory]
    [InlineData("51F-123.45", "51F12345")]
    [InlineData(" 30k 555 55 ", "30K55555")]
    [InlineData("xé-1/2_b", "X12B")]
    public async Task Plate_normalizer_function_matches_LicensePlateNormalizer(string raw, string expected)
    {
        _db.RequireAvailable();

        Assert.Equal(expected, SmartPS.Services.GateControl.LicensePlateNormalizer.Normalize(raw));
        Assert.Equal(expected, await SqlServerDatabaseFixture.ScalarAsync<string>(_db.ConnectionString,
            "SELECT dbo.fn_NormalizePlate(@p)", ("p", raw)));
    }

    [Fact]
    public async Task Fresh_database_has_audience_default_0_and_the_gate_and_report_indexes()
    {
        _db.RequireAvailable();
        var cs = _db.ConnectionString;

        Assert.Equal(1, await IndexCountAsync(cs, "ParkingSessions", "IX_ParkingSessions_SlotId_Active"));
        Assert.Equal(1, await IndexCountAsync(cs, "ParkingSessions", "IX_ParkingSessions_SlotId"));
        Assert.Equal(1, await IndexCountAsync(cs, "ParkingSessions", "IX_ParkingSessions_CheckOutTime"));
        Assert.Equal(1, await CountAsync(cs, """
            SELECT COUNT(*) FROM sys.indexes
            WHERE object_id = OBJECT_ID('dbo.ParkingSessions') AND name = 'IX_ParkingSessions_SlotId_Active'
              AND is_unique = 1 AND has_filter = 1 AND filter_definition = '([Status]=(0) AND [SlotId] IS NOT NULL)'
            """));

        // A zone inserted without Audience gets Mixed
        await ExecAsync(cs, "INSERT INTO \"ParkingZones\" (\"ZoneCode\",\"ZoneName\",\"TotalCapacity\") VALUES ('ZDEF1','default audience',0)");
        Assert.Equal(0, await CountAsync(cs, "SELECT \"Audience\" FROM \"ParkingZones\" WHERE \"ZoneCode\" = 'ZDEF1'"));
    }

    [Fact]
    public async Task Unique_index_rejects_a_second_active_session_on_a_slot_but_allows_completed_ones()
    {
        _db.RequireAvailable();
        var cs = _db.ConnectionString;
        await ExecAsync(cs, """
            INSERT INTO "VehicleTypes" ("TypeName","Description") VALUES ('IdxMoto','m');
            INSERT INTO "ParkingSlots" ("SlotCode","ZoneName","VehicleTypeId","Status")
            SELECT 'IDX-01','idx', v."VehicleTypeId", 1 FROM "VehicleTypes" v WHERE v."TypeName"='IdxMoto';
            INSERT INTO "ParkingSessions" ("TicketCode","LicensePlate","VehicleTypeId","SlotId","CheckInTime","Status","IsMonthlyPass","CustomerType","TotalFee","PaymentMethod")
            SELECT 'TK-IDX-1','IDXA1', v."VehicleTypeId", s."SlotId", SYSUTCDATETIME(), 0, 0, 1, 0, 0 FROM "VehicleTypes" v, "ParkingSlots" s WHERE v."TypeName"='IdxMoto' AND s."SlotCode"='IDX-01';
            INSERT INTO "ParkingSessions" ("TicketCode","LicensePlate","VehicleTypeId","SlotId","CheckInTime","CheckOutTime","Status","IsMonthlyPass","CustomerType","TotalFee","PaymentMethod")
            SELECT 'TK-IDX-2','IDXB1', v."VehicleTypeId", s."SlotId", SYSUTCDATETIME(), SYSUTCDATETIME(), 1, 0, 1, 0, 0 FROM "VehicleTypes" v, "ParkingSlots" s WHERE v."TypeName"='IdxMoto' AND s."SlotCode"='IDX-01';
            INSERT INTO "ParkingSessions" ("TicketCode","LicensePlate","VehicleTypeId","SlotId","CheckInTime","Status","IsMonthlyPass","CustomerType","TotalFee","PaymentMethod")
            SELECT 'TK-IDX-3','IDXC1', v."VehicleTypeId", NULL, SYSUTCDATETIME(), 0, 0, 1, 0, 0 FROM "VehicleTypes" v WHERE v."TypeName"='IdxMoto';
            INSERT INTO "ParkingSessions" ("TicketCode","LicensePlate","VehicleTypeId","SlotId","CheckInTime","Status","IsMonthlyPass","CustomerType","TotalFee","PaymentMethod")
            SELECT 'TK-IDX-4','IDXD1', v."VehicleTypeId", NULL, SYSUTCDATETIME(), 0, 0, 1, 0, 0 FROM "VehicleTypes" v WHERE v."TypeName"='IdxMoto';
            """);

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => ExecAsync(cs, """
            INSERT INTO "ParkingSessions" ("TicketCode","LicensePlate","VehicleTypeId","SlotId","CheckInTime","Status","IsMonthlyPass","CustomerType","TotalFee","PaymentMethod")
            SELECT 'TK-IDX-5','IDXE1', v."VehicleTypeId", s."SlotId", SYSUTCDATETIME(), 0, 0, 1, 0, 0 FROM "VehicleTypes" v, "ParkingSlots" s WHERE v."TypeName"='IdxMoto' AND s."SlotCode"='IDX-01';
            """));

        Assert.Contains("IX_ParkingSessions_SlotId_Active", ex.ToString(), StringComparison.Ordinal);
        Assert.True(SmartPS.Services.GateControl.GateSlotAllocator.IsActiveSlotUniqueViolation(ex));
    }

    // ---- seed ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Fresh_database_seed_matches_spec_6_1_and_is_rerunnable()
    {
        _db.RequireAvailable();
        var cs = _db.ConnectionString;

        await using (var ctx = _db.CreateContext())
        {
            Assert.Equal(23, await ctx.Permissions.CountAsync());
        }

        Assert.Equal(23, (await GrantsAsync(cs, "Admin")).Count);
        Assert.Equal(TestUsers.ManagerDefaultPermissions.ToHashSet(StringComparer.Ordinal), await GrantsAsync(cs, "Manager"));
        Assert.Equal(TestUsers.OperatorSeedPermissions.ToHashSet(StringComparer.Ordinal), await GrantsAsync(cs, "Operator"));
        Assert.Equal("Quản trị viên Hệ thống", await SqlServerDatabaseFixture.ScalarAsync<string>(cs,
            "SELECT \"FullName\" FROM \"Users\" WHERE \"Username\" = 'admin'"));

        var grantsBefore = await CountAsync(cs, "SELECT COUNT(*) FROM \"RolePermissions\"");
        var slotsBefore = await CountAsync(cs, "SELECT COUNT(*) FROM \"ParkingSlots\"");
        await using (var ctx = _db.CreateContext())
        {
            await DbInitializer.InitializeAsync(ctx);
        }

        Assert.Equal(grantsBefore, await CountAsync(cs, "SELECT COUNT(*) FROM \"RolePermissions\""));
        Assert.Equal(slotsBefore, await CountAsync(cs, "SELECT COUNT(*) FROM \"ParkingSlots\""));
        Assert.Equal(1, await CountAsync(cs, "SELECT COUNT(*) FROM \"Users\" WHERE \"Username\" = 'admin'"));
        Assert.Equal(1, await CountAsync(cs, "SELECT COUNT(*) FROM \"ParkingSettings\""));
    }

    [Fact]
    public async Task M4_seed_does_not_regrant_removed_permissions_to_existing_roles()
    {
        // m4: a grant removed through the matrix must not come back on the next app start (seed re-run).
        _db.RequireAvailable();
        var cs = await _db.CreateSiblingDatabaseAsync("seed");
        await using (var ctx = SqlServerDatabaseFixture.CreateContext(cs))
        {
            await DbInitializer.InitializeAsync(ctx);
        }

        await ExecAsync(cs, """
            DELETE rp FROM "RolePermissions" rp
            JOIN "Roles" r ON rp."RoleId" = r."RoleId"
            JOIN "Permissions" p ON rp."PermissionId" = p."PermissionId"
            WHERE (r."RoleName" = 'Operator' AND p."PermissionName" = 'Report.View')
               OR (r."RoleName" = 'Manager' AND p."PermissionName" = 'Audit.View');
            """);

        await using (var ctx = SqlServerDatabaseFixture.CreateContext(cs))
        {
            await DbInitializer.InitializeAsync(ctx);
        }

        Assert.DoesNotContain("Report.View", await GrantsAsync(cs, "Operator"));
        Assert.DoesNotContain("Audit.View", await GrantsAsync(cs, "Manager"));
        Assert.Equal(23, (await GrantsAsync(cs, "Admin")).Count);
    }

    // ---- empty database / down and up ---------------------------------------------------------------------------------

    [Fact]
    public async Task Database_created_with_the_server_default_collation_is_configured_before_migrating()
    {
        // Database tạo tay (ví dụ từ SSMS) mang collation mặc định của máy chủ: DbInitializer đổi collation và bật RCSI
        _db.RequireAvailable();
        var cs = await _db.CreateSiblingDatabaseAsync("plain");
        var name = new SqlConnectionStringBuilder(cs).InitialCatalog;
        var master = new SqlConnectionStringBuilder(cs) { InitialCatalog = "master", Pooling = false }.ConnectionString;
        await SqlServerDatabaseFixture.ScalarAsync<int>(master,
            $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; ALTER DATABASE [{name}] COLLATE SQL_Latin1_General_CP1_CI_AS; ALTER DATABASE [{name}] SET MULTI_USER; SELECT 0;");
        SqlConnection.ClearAllPools();

        await using (var ctx = SqlServerDatabaseFixture.CreateContext(cs))
        {
            await DbInitializer.InitializeAsync(ctx);
        }

        Assert.Equal(SmartPsDbContext.DatabaseCollation,
            await SqlServerDatabaseFixture.ScalarAsync<string>(cs, "SELECT CONVERT(nvarchar(128), DATABASEPROPERTYEX(DB_NAME(), 'Collation'))"));
        Assert.True(await SqlServerDatabaseFixture.ScalarAsync<bool>(cs, "SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = DB_NAME()"));
        await using (var ctx = SqlServerDatabaseFixture.CreateContext(cs))
        {
            Assert.Empty(await ctx.Database.GetPendingMigrationsAsync());
            Assert.Equal(23, await ctx.Permissions.CountAsync());
        }
    }

    [Fact]
    public async Task Down_to_empty_then_up_again_restores_the_schema()
    {
        _db.RequireAvailable();
        var cs = await _db.CreateSiblingDatabaseAsync("downup");
        await using (var ctx = SqlServerDatabaseFixture.CreateContext(cs))
        {
            await DbInitializer.InitializeAsync(ctx);
        }

        await MigrateToAsync(cs, Migration.InitialDatabase);
        Assert.Equal(0, await CountAsync(cs, "SELECT COUNT(*) FROM sys.tables WHERE name <> '__EFMigrationsHistory'"));
        Assert.Equal(0, await CountAsync(cs, "SELECT COUNT(*) FROM sys.objects WHERE name IN ('fn_NormalizePlate', 'AuditLogsTruncateGuard')"));

        await MigrateToAsync(cs, null);
        await using (var ctx = SqlServerDatabaseFixture.CreateContext(cs))
        {
            Assert.Empty(await ctx.Database.GetPendingMigrationsAsync());
        }

        Assert.Equal(1, await IndexCountAsync(cs, "ParkingSessions", "IX_ParkingSessions_SlotId_Active"));
        Assert.Equal(1, await CountAsync(cs, "SELECT COUNT(*) FROM sys.triggers WHERE name = 'TR_AuditLogs_BlockMutation'"));
    }
}
