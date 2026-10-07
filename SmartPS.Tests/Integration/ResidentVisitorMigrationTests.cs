using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SmartPS.Tests.Integration;

/// <summary>
/// AC-13 / R2 / R8 / m3 / ADDENDUM A: the AddResidentVisitorFlow migration on a database with legacy data
/// (DefaultLicensePlate → CustomerVehicles, ticket plates normalized, duplicate active sessions unlinked, permissions granted),
/// then Down() removes everything it added and Up can run again. Runs on sibling databases.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class ResidentVisitorMigrationTests : IClassFixture<PostgresDatabaseFixture>
{
    /// <summary>Last migration before the resident/visitor flow (RBAC/audit task head).</summary>
    private const string Task2HeadMigration = "20261007184617_HardenAuditTriggers";
    private const string ShiftMigration = "20261004152712_AddShiftManagement";
    private const string MigrationSuffix = "_AddResidentVisitorFlow";

    private readonly PostgresDatabaseFixture _db;

    public ResidentVisitorMigrationTests(PostgresDatabaseFixture db)
    {
        _db = db;
    }

    private static async Task MigrateToAsync(string cs, string? target)
    {
        await using var ctx = PostgresDatabaseFixture.CreateContext(cs);
        await ctx.GetService<IMigrator>().MigrateAsync(target);
    }

    private static async Task ExecAsync(string cs, string sql)
    {
        await using var ctx = PostgresDatabaseFixture.CreateContext(cs);
        await ctx.Database.ExecuteSqlRawAsync(sql);
    }

    private static Task<long> CountAsync(string cs, string sql) => PostgresDatabaseFixture.ScalarAsync<long>(cs, sql);

    private static Task<string?> TextAsync(string cs, string sql) => PostgresDatabaseFixture.ScalarAsync<string>(cs, sql);

    private static async Task<HashSet<string>> GrantsAsync(string cs, string roleName)
    {
        await using var ctx = PostgresDatabaseFixture.CreateContext(cs);
        return (await ctx.RolePermissions.Where(rp => rp.Role.RoleName == roleName).Select(rp => rp.Permission.PermissionName).ToListAsync())
            .ToHashSet(StringComparer.Ordinal);
    }

    private static async Task<HashSet<string>> PermissionNamesAsync(string cs)
    {
        await using var ctx = PostgresDatabaseFixture.CreateContext(cs);
        return (await ctx.Permissions.Select(p => p.PermissionName).ToListAsync()).ToHashSet(StringComparer.Ordinal);
    }

    private static Task<long> IndexCountAsync(string cs, string indexName)
        => CountAsync(cs, $"SELECT count(*) FROM pg_indexes WHERE indexname = '{indexName}'");

    private static Task<long> ColumnCountAsync(string cs, string table, string column)
        => CountAsync(cs, $"SELECT count(*) FROM information_schema.columns WHERE table_name = '{table}' AND column_name = '{column}'");

    private static Task<long> TableCountAsync(string cs, string table)
        => CountAsync(cs, $"SELECT count(*) FROM pg_tables WHERE tablename = '{table}'");

    private static long Id(long? value) => value ?? throw new InvalidOperationException("id not found");

    /// <summary>Database at the RBAC head with roles, legacy permissions and legacy parking data; returns ids.</summary>
    private async Task<(string Cs, long C1, long C2, long C3, long C4, long OlderSession, long NewerSession, long SlotId)> LegacyDatabaseAsync(string suffix)
    {
        var cs = await _db.CreateSiblingDatabaseAsync(suffix);

        await MigrateToAsync(cs, ShiftMigration);
        var permissionValues = string.Join(",", TestUsers.LegacyPermissions.Select(p => $"('{p}','{p}')"));
        await ExecAsync(cs, $"INSERT INTO \"Permissions\" (\"PermissionName\",\"Description\") VALUES {permissionValues} ON CONFLICT (\"PermissionName\") DO NOTHING;");
        await ExecAsync(cs, "INSERT INTO \"Roles\" (\"RoleName\",\"Description\") VALUES ('Admin','a'),('Manager','m'),('Operator','o');");
        await ExecAsync(cs, """
            INSERT INTO "RolePermissions" ("RoleId","PermissionId")
            SELECT r."RoleId", p."PermissionId" FROM "Roles" r CROSS JOIN "Permissions" p WHERE r."RoleName"='Admin'
            ON CONFLICT ("RoleId","PermissionId") DO NOTHING;
            INSERT INTO "RolePermissions" ("RoleId","PermissionId")
            SELECT r."RoleId", p."PermissionId" FROM "Roles" r JOIN "Permissions" p
              ON p."PermissionName" IN ('Parking.View','Parking.CheckIn','Parking.CheckOut','Report.View','Shift.View','Shift.Open','Shift.Close')
            WHERE r."RoleName"='Operator'
            ON CONFLICT ("RoleId","PermissionId") DO NOTHING;
            """);
        await MigrateToAsync(cs, Task2HeadMigration);

        await ExecAsync(cs, """
            INSERT INTO "VehicleTypes" ("TypeName","Description") VALUES ('Xe máy','m'),('Xe ô tô','o');
            INSERT INTO "Users" ("Username","PasswordHash","FullName","RoleId","IsActive")
            SELECT 'legacy_admin', 'x', 'Legacy Admin', r."RoleId", true FROM "Roles" r WHERE r."RoleName"='Admin';
            INSERT INTO "ParkingZones" ("ZoneCode","ZoneName","TotalCapacity","Description","VehicleTypeId")
            SELECT 'ZONE_L', 'Khu cũ', 2, 'legacy', v."VehicleTypeId" FROM "VehicleTypes" v WHERE v."TypeName"='Xe máy';
            INSERT INTO "ParkingSlots" ("SlotCode","ZoneName","ZoneId","VehicleTypeId","Status")
            SELECT 'L-01', z."ZoneName", z."ZoneId", z."VehicleTypeId", 1 FROM "ParkingZones" z WHERE z."ZoneCode"='ZONE_L';
            INSERT INTO "Customers" ("FullName","PhoneNumber","DefaultLicensePlate","Type","CreatedAt","IsActive","VehicleTypeId")
            SELECT 'C1', '0900000001', '30A-123.45', 2, now() - interval '4 day', true, v."VehicleTypeId" FROM "VehicleTypes" v WHERE v."TypeName"='Xe máy';
            INSERT INTO "Customers" ("FullName","PhoneNumber","DefaultLicensePlate","Type","CreatedAt","IsActive")
            VALUES ('C2', '0900000002', '', 0, now() - interval '3 day', true);
            INSERT INTO "Customers" ("FullName","PhoneNumber","DefaultLicensePlate","Type","CreatedAt","IsActive","VehicleTypeId")
            SELECT 'C3', '0900000003', '29B1-555.55', 0, now() - interval '2 day', true, v."VehicleTypeId" FROM "VehicleTypes" v WHERE v."TypeName"='Xe máy';
            INSERT INTO "Customers" ("FullName","PhoneNumber","DefaultLicensePlate","Type","CreatedAt","IsActive","VehicleTypeId")
            SELECT 'C4', '0900000004', '29B1-555.55', 0, now() - interval '1 day', true, v."VehicleTypeId" FROM "VehicleTypes" v WHERE v."TypeName"='Xe máy';
            INSERT INTO "MonthlyTickets" ("TicketCode","CustomerId","RegisteredLicensePlate","VehicleTypeId","StartDate","EndDate","MonthlyPrice","Status","CreatedAt")
            SELECT 'MT-LEGACY-1', c."CustomerId", '30A-123.45', c."VehicleTypeId", now() - interval '10 day', now() + interval '20 day', 120000, 0, now()
            FROM "Customers" c WHERE c."FullName"='C1';
            INSERT INTO "ParkingSessions" ("TicketCode","LicensePlate","VehicleTypeId","SlotId","CheckInTime","Status")
            SELECT 'TK-OLD', '59X-111.11', s."VehicleTypeId", s."SlotId", now() - interval '2 hour', 0 FROM "ParkingSlots" s WHERE s."SlotCode"='L-01';
            INSERT INTO "ParkingSessions" ("TicketCode","LicensePlate","VehicleTypeId","SlotId","CheckInTime","Status")
            SELECT 'TK-NEW', '59X-222.22', s."VehicleTypeId", s."SlotId", now() - interval '1 hour', 0 FROM "ParkingSlots" s WHERE s."SlotCode"='L-01';
            INSERT INTO "ParkingSessions" ("TicketCode","LicensePlate","VehicleTypeId","SlotId","CheckInTime","CheckOutTime","Status")
            SELECT 'TK-DONE', '59X-333.33', s."VehicleTypeId", s."SlotId", now() - interval '5 hour', now() - interval '4 hour', 1 FROM "ParkingSlots" s WHERE s."SlotCode"='L-01';
            """);

        async Task<long> CustomerAsync(string name) => Id(await PostgresDatabaseFixture.ScalarAsync<long?>(cs, $"SELECT \"CustomerId\" FROM \"Customers\" WHERE \"FullName\"='{name}'"));
        async Task<long> SessionAsync(string code) => Id(await PostgresDatabaseFixture.ScalarAsync<long?>(cs, $"SELECT \"SessionId\" FROM \"ParkingSessions\" WHERE \"TicketCode\"='{code}'"));
        var slotId = Id(await PostgresDatabaseFixture.ScalarAsync<long?>(cs, "SELECT \"SlotId\" FROM \"ParkingSlots\" WHERE \"SlotCode\"='L-01'"));

        return (cs, await CustomerAsync("C1"), await CustomerAsync("C2"), await CustomerAsync("C3"), await CustomerAsync("C4"),
                await SessionAsync("TK-OLD"), await SessionAsync("TK-NEW"), slotId);
    }

    private static async Task AssertLegacyCountsAsync(string cs)
    {
        Assert.Equal(4, await CountAsync(cs, "SELECT count(*) FROM \"Customers\""));
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM \"MonthlyTickets\""));
        Assert.Equal(3, await CountAsync(cs, "SELECT count(*) FROM \"ParkingSessions\""));
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM \"ParkingSlots\""));
    }

    [Fact]
    public async Task AC13_up_migrates_legacy_data_then_down_removes_everything_and_up_runs_again()
    {
        _db.RequireAvailable();
        var (cs, c1, c2, c3, c4, older, newer, slotId) = await LegacyDatabaseAsync("rv_mig");

        // When the resident/visitor migration runs
        await MigrateToAsync(cs, null);

        // Then it is the latest migration
        var last = await TextAsync(cs, "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\" DESC LIMIT 1");
        Assert.EndsWith(MigrationSuffix, last, StringComparison.Ordinal);
        Assert.True(string.CompareOrdinal(last, Task2HeadMigration) > 0, $"{last} must sort after {Task2HeadMigration}");

        // DefaultLicensePlate → CustomerVehicles (normalized), C2 has none, C3 keeps the shared plate active, C4 inactive
        Assert.Equal(1, await CountAsync(cs, $"SELECT count(*) FROM \"CustomerVehicles\" WHERE \"CustomerId\" = {c1}"));
        Assert.Equal(1, await CountAsync(cs, $"SELECT count(*) FROM \"CustomerVehicles\" WHERE \"CustomerId\" = {c1} AND \"LicensePlate\" = '30A12345' AND \"IsActive\""));
        Assert.Equal(0, await CountAsync(cs, $"SELECT count(*) FROM \"CustomerVehicles\" WHERE \"CustomerId\" = {c2}"));
        Assert.Equal(1, await CountAsync(cs, $"SELECT count(*) FROM \"CustomerVehicles\" WHERE \"CustomerId\" = {c3} AND \"LicensePlate\" = '29B155555' AND \"IsActive\""));
        Assert.Equal(1, await CountAsync(cs, $"SELECT count(*) FROM \"CustomerVehicles\" WHERE \"CustomerId\" = {c4} AND \"LicensePlate\" = '29B155555' AND NOT \"IsActive\""));
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM \"CustomerVehicles\" WHERE \"LicensePlate\" = '29B155555' AND \"IsActive\""));
        Assert.Equal(1, await CountAsync(cs, $"SELECT count(*) FROM \"CustomerVehicles\" WHERE \"CustomerId\" = {c1} AND \"VehicleTypeId\" = (SELECT \"VehicleTypeId\" FROM \"VehicleTypes\" WHERE \"TypeName\"='Xe máy')"));

        // Ticket plate normalized; legacy rows kept; residency columns defaulted
        Assert.Equal("30A12345", await TextAsync(cs, "SELECT \"RegisteredLicensePlate\" FROM \"MonthlyTickets\" WHERE \"TicketCode\"='MT-LEGACY-1'"));
        await AssertLegacyCountsAsync(cs);
        Assert.Equal(0, await CountAsync(cs, "SELECT count(*) FROM \"Customers\" WHERE \"IsResident\" OR \"ApartmentCode\" IS NOT NULL"));
        Assert.Equal("30A-123.45", await TextAsync(cs, $"SELECT \"DefaultLicensePlate\" FROM \"Customers\" WHERE \"CustomerId\" = {c1}"));

        // Older duplicate active session unlinked from the slot; newer and completed ones keep it
        Assert.Equal(0, await CountAsync(cs, $"SELECT count(*) FROM \"ParkingSessions\" WHERE \"SessionId\" = {older} AND \"SlotId\" IS NOT NULL"));
        Assert.Equal(1, await CountAsync(cs, $"SELECT count(*) FROM \"ParkingSessions\" WHERE \"SessionId\" = {newer} AND \"SlotId\" = {slotId}"));
        Assert.Equal(1, await CountAsync(cs, $"SELECT count(*) FROM \"ParkingSessions\" WHERE \"TicketCode\" = 'TK-DONE' AND \"SlotId\" = {slotId}"));

        // Permissions (R8)
        var permissions = await PermissionNamesAsync(cs);
        Assert.Equal(TestUsers.TotalPermissionCount, permissions.Count);
        Assert.Superset(TestUsers.CustomerPermissions.ToHashSet(StringComparer.Ordinal), permissions);
        Assert.Equal(permissions, await GrantsAsync(cs, "Admin"));
        Assert.Superset(TestUsers.CustomerPermissions.ToHashSet(StringComparer.Ordinal), await GrantsAsync(cs, "Manager"));
        var operatorGrants = await GrantsAsync(cs, "Operator");
        Assert.Contains(Permissions.CustomerView, operatorGrants);
        Assert.DoesNotContain(Permissions.CustomerManage, operatorGrants);
        Assert.DoesNotContain(Permissions.BlacklistManage, operatorGrants);

        // Zones default to Mixed
        Assert.Equal(0, await CountAsync(cs, "SELECT count(*) FROM \"ParkingZones\" WHERE \"Audience\" <> 0"));

        // Indexes (m3: the plain SlotId FK index survives next to the partial unique one)
        foreach (var index in new[]
                 {
                     "IX_ParkingSessions_SlotId", "IX_ParkingSessions_SlotId_Active", "IX_CustomerVehicles_LicensePlate_Active",
                     "IX_CustomerVehicles_CustomerId", "IX_BlacklistEntries_LicensePlate_Active", "IX_BlacklistEntries_LicensePlate",
                     "IX_MonthlyTicketPurchases_CreatedAtUtc", "IX_Customers_ApartmentCode"
                 })
        {
            Assert.True(await IndexCountAsync(cs, index) == 1, $"index {index} missing");
        }

        foreach (var partial in new[] { "IX_ParkingSessions_SlotId_Active", "IX_CustomerVehicles_LicensePlate_Active", "IX_BlacklistEntries_LicensePlate_Active" })
        {
            var def = await TextAsync(cs, $"SELECT indexdef FROM pg_indexes WHERE indexname = '{partial}'");
            Assert.Contains("UNIQUE", def, StringComparison.Ordinal);
            Assert.Contains("WHERE", def, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("UNIQUE", await TextAsync(cs, "SELECT indexdef FROM pg_indexes WHERE indexname = 'IX_ParkingSessions_SlotId'"), StringComparison.Ordinal);

        // ADDENDUM A: purchases table exists, no backfill
        Assert.Equal(1, await TableCountAsync(cs, "MonthlyTicketPurchases"));
        Assert.Equal(0, await CountAsync(cs, "SELECT count(*) FROM \"MonthlyTicketPurchases\""));

        // Given data that only the new schema can hold
        await ExecAsync(cs, $"UPDATE \"Customers\" SET \"Type\" = 3, \"IsResident\" = true, \"ApartmentCode\" = 'A-0101' WHERE \"CustomerId\" = {c1};");
        await ExecAsync(cs, """
            INSERT INTO "MonthlyTicketPurchases" ("TicketId","Kind","PlanId","Price","PeriodStartUtc","PeriodEndUtc","CreatedAtUtc","CreatedByUserId")
            SELECT t."TicketId", 0, NULL, 120000, t."StartDate", t."EndDate", now(), u."UserId"
            FROM "MonthlyTickets" t CROSS JOIN "Users" u WHERE t."TicketCode"='MT-LEGACY-1' AND u."Username"='legacy_admin';
            INSERT INTO "BlacklistEntries" ("LicensePlate","Reason","CreatedAt","IsActive") VALUES ('29A99999','x',now(),true);
            """);

        // When Down() runs
        await MigrateToAsync(cs, Task2HeadMigration);

        // Then everything the migration added is gone
        foreach (var table in new[] { "CustomerVehicles", "BlacklistEntries", "MonthlyTicketPurchases" })
        {
            Assert.True(await TableCountAsync(cs, table) == 0, $"table {table} still exists");
        }

        Assert.Equal(0, await ColumnCountAsync(cs, "Customers", "IsResident"));
        Assert.Equal(0, await ColumnCountAsync(cs, "Customers", "ApartmentCode"));
        Assert.Equal(0, await ColumnCountAsync(cs, "Customers", "Building"));
        Assert.Equal(0, await ColumnCountAsync(cs, "ParkingZones", "Audience"));
        Assert.Equal(0, await IndexCountAsync(cs, "IX_ParkingSessions_SlotId_Active"));
        Assert.Equal(1, await IndexCountAsync(cs, "IX_ParkingSessions_SlotId"));
        Assert.Equal(0, await CountAsync(cs, "SELECT count(*) FROM \"Permissions\" WHERE \"PermissionName\" IN ('Customer.View','Customer.Manage','Blacklist.Manage')"));
        Assert.Equal(23, (await PermissionNamesAsync(cs)).Count);
        Assert.Equal(TestUsers.OperatorRbacMigrationPermissions.ToHashSet(StringComparer.Ordinal), await GrantsAsync(cs, "Operator"));
        Assert.Equal(2, await CountAsync(cs, $"SELECT \"Type\" FROM \"Customers\" WHERE \"CustomerId\" = {c1}"));
        Assert.Equal(0, await CountAsync(cs, "SELECT count(*) FROM \"Customers\" WHERE \"Type\" = 3"));
        Assert.Equal("30A-123.45", await TextAsync(cs, $"SELECT \"DefaultLicensePlate\" FROM \"Customers\" WHERE \"CustomerId\" = {c1}"));
        await AssertLegacyCountsAsync(cs);
        Assert.Equal(0, await CountAsync(cs, $"SELECT count(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" LIKE '%{MigrationSuffix}'"));

        // And Up can be applied again
        await MigrateToAsync(cs, null);
        Assert.Equal(1, await TableCountAsync(cs, "CustomerVehicles"));
        Assert.Equal(TestUsers.TotalPermissionCount, (await PermissionNamesAsync(cs)).Count);
        await AssertLegacyCountsAsync(cs);
    }

    [Fact]
    public async Task Down_restores_DefaultLicensePlate_from_vehicles_when_it_is_empty()
    {
        _db.RequireAvailable();
        var (cs, _, c2, _, _, _, _, _) = await LegacyDatabaseAsync("rv_mig_dl");
        await MigrateToAsync(cs, null);

        // A vehicle added after the migration for a customer that had no legacy plate
        await ExecAsync(cs, $"""
            INSERT INTO "CustomerVehicles" ("CustomerId","LicensePlate","VehicleTypeId","IsActive","CreatedAt")
            SELECT {c2}, '88K12345', v."VehicleTypeId", true, now() FROM "VehicleTypes" v WHERE v."TypeName"='Xe máy';
            """);

        await MigrateToAsync(cs, Task2HeadMigration);

        Assert.Equal("88K12345", await TextAsync(cs, $"SELECT \"DefaultLicensePlate\" FROM \"Customers\" WHERE \"CustomerId\" = {c2}"));
    }

    [Fact]
    public async Task G7_plate_conflict_prefers_the_customer_holding_a_valid_ticket_over_a_default_plate_only_owner()
    {
        // Fix round 1, G7 (CH1-06, challenge Attacks2.H3): B holds a currently valid ticket on 77Z-999.99 (its own default
        // plate is another one); A only has 77Z-999.99 as DefaultLicensePlate. After the migration B must own the plate.
        _db.RequireAvailable();
        var cs = await _db.CreateSiblingDatabaseAsync("rv_mig_g7");
        await MigrateToAsync(cs, ShiftMigration);
        await ExecAsync(cs, "INSERT INTO \"Roles\" (\"RoleName\",\"Description\") VALUES ('Admin','a'),('Manager','m'),('Operator','o');");
        await MigrateToAsync(cs, Task2HeadMigration);
        await ExecAsync(cs, """
            INSERT INTO "VehicleTypes" ("TypeName","Description") VALUES ('Xe máy','m');
            INSERT INTO "Customers" ("FullName","PhoneNumber","DefaultLicensePlate","Type","CreatedAt","IsActive","VehicleTypeId")
            SELECT 'B-ticket-holder', '0900000002', '77Z-000.01', 0, now() - interval '5 day', true, v."VehicleTypeId" FROM "VehicleTypes" v;
            INSERT INTO "Customers" ("FullName","PhoneNumber","DefaultLicensePlate","Type","CreatedAt","IsActive","VehicleTypeId")
            SELECT 'A-default-only', '0900000001', '77Z-999.99', 0, now() - interval '4 day', true, v."VehicleTypeId" FROM "VehicleTypes" v;
            INSERT INTO "MonthlyTickets" ("TicketCode","CustomerId","RegisteredLicensePlate","VehicleTypeId","StartDate","EndDate","MonthlyPrice","Status","CreatedAt")
            SELECT 'MT-B', c."CustomerId", '77Z-999.99', c."VehicleTypeId", now() - interval '10 day', now() + interval '20 day', 120000, 0, now()
            FROM "Customers" c WHERE c."FullName"='B-ticket-holder';
            """);

        await MigrateToAsync(cs, null);

        Assert.Equal(1, await CountAsync(cs, """
            SELECT count(*) FROM "CustomerVehicles" v JOIN "Customers" c ON c."CustomerId" = v."CustomerId"
            WHERE c."FullName" = 'B-ticket-holder' AND v."LicensePlate" = '77Z99999' AND v."IsActive"
            """));
        Assert.Equal(0, await CountAsync(cs, """
            SELECT count(*) FROM "CustomerVehicles" v JOIN "Customers" c ON c."CustomerId" = v."CustomerId"
            WHERE c."FullName" = 'A-default-only' AND v."LicensePlate" = '77Z99999' AND v."IsActive"
            """));
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM \"CustomerVehicles\" WHERE \"LicensePlate\" = '77Z99999' AND \"IsActive\""));
        Assert.Equal(1, await CountAsync(cs, """
            SELECT count(*) FROM "CustomerVehicles" v JOIN "Customers" c ON c."CustomerId" = v."CustomerId"
            WHERE c."FullName" = 'B-ticket-holder' AND v."LicensePlate" = '77Z00001' AND v."IsActive"
            """));

        // B's ticket still classifies after the migration
        await using var ctx = PostgresDatabaseFixture.CreateContext(cs);
        var classification = await SmartPS.Services.GateControl.GateClassificationQueries.ClassifyAsync(ctx, "77Z99999", DateTime.UtcNow);
        Assert.Equal(SmartPS.Models.GateControl.VehicleCategory.MonthlyPass, classification.Category);
        Assert.Equal("MT-B", classification.Ticket?.TicketCode);
    }

    [Fact]
    public async Task Fresh_database_reaches_the_new_migration_with_seed()
    {
        // The fixture database = all migrations + seed.
        _db.RequireAvailable();

        await using var ctx = _db.CreateContext();
        var applied = (await ctx.Database.GetAppliedMigrationsAsync()).ToList();
        Assert.EndsWith(MigrationSuffix, applied[^1], StringComparison.Ordinal);
        Assert.Empty(await ctx.Database.GetPendingMigrationsAsync());
        Assert.Equal(TestUsers.TotalPermissionCount, (await PermissionNamesAsync(_db.ConnectionString)).Count);
        Assert.Equal(TestUsers.OperatorSeedPermissions.ToHashSet(StringComparer.Ordinal), await GrantsAsync(_db.ConnectionString, "Operator"));
        Assert.Equal(TestUsers.ManagerDefaultPermissions.ToHashSet(StringComparer.Ordinal), await GrantsAsync(_db.ConnectionString, "Manager"));
    }
}
