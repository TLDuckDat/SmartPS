using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SmartPS.Data;

namespace SmartPS.Tests.Integration;

/// <summary>
/// AC-R10 / plan §1.5 + addendum 2: the gate-flow migration <c>&lt;ts&gt;_AddGateZoneAudience</c>.
/// <list type="bullet">
/// <item>Sorts after the household (20261008011116), blacklist (20261010112452) and audit (20261013083249) migrations.</item>
/// <item>Adds <c>ParkingZones.Audience int NOT NULL DEFAULT 0</c> and the unique partial index
/// <c>IX_ParkingSessions_SlotId_Active</c> (<c>"Status" = 0 AND "SlotId" IS NOT NULL</c>), after unlinking older duplicate
/// active sessions per slot; the plain FK index <c>IX_ParkingSessions_SlotId</c> stays.</item>
/// <item>Creates no customer/vehicle/ticket/blacklist table, column or permission.</item>
/// <item>Runs on an empty database and on a database at the audit migration with data; Down/Up are valid.</item>
/// <item>The model has no pending changes.</item>
/// </list>
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class GateZoneAudienceMigrationTests : IClassFixture<PostgresDatabaseFixture>
{
    private const string HouseholdMigration = "20261008011116_AddHouseholdVehicleAndPricing";
    private const string BlacklistMigration = "20261010112452_AddBlacklistTable";
    private const string AuditMigration = "20261013083249_AddRbacAndAuditTrail";
    private const string GateSuffix = "_AddGateZoneAudience";

    private readonly PostgresDatabaseFixture _db;

    public GateZoneAudienceMigrationTests(PostgresDatabaseFixture db)
    {
        _db = db;
    }

    private static async Task MigrateToAsync(string connectionString, string? target)
    {
        await using var ctx = PostgresDatabaseFixture.CreateContext(connectionString);
        await ctx.GetService<IMigrator>().MigrateAsync(target);
    }

    private static async Task ExecAsync(string connectionString, string sql)
    {
        await using var ctx = PostgresDatabaseFixture.CreateContext(connectionString);
        await ctx.Database.ExecuteSqlRawAsync(sql);
    }

    private static Task<long> CountAsync(string cs, string sql) => PostgresDatabaseFixture.ScalarAsync<long>(cs, sql);

    private static string GateMigrationId()
    {
        using var ctx = PostgresDatabaseFixture.CreateContext(PostgresDatabaseFixture.DefaultConnectionString);
        return Assert.Single(ctx.Database.GetMigrations(), m => m.EndsWith(GateSuffix, StringComparison.Ordinal));
    }

    private static Task<long> AudienceColumnCountAsync(string cs)
        => CountAsync(cs, "SELECT count(*) FROM information_schema.columns WHERE table_name = 'ParkingZones' AND column_name = 'Audience'");

    private static Task<string?> ActiveSlotIndexDefAsync(string cs)
        => PostgresDatabaseFixture.ScalarAsync<string>(cs,
            "SELECT indexdef FROM pg_indexes WHERE tablename = 'ParkingSessions' AND indexname = 'IX_ParkingSessions_SlotId_Active'");

    // ---- chain shape ----------------------------------------------------------------------------------------------------

    [Fact]
    public void Gate_migration_exists_once_and_sorts_after_household_blacklist_and_audit_migrations()
    {
        using var ctx = PostgresDatabaseFixture.CreateContext(PostgresDatabaseFixture.DefaultConnectionString);
        var migrations = ctx.Database.GetMigrations().ToList();
        var gate = GateMigrationId();

        Assert.Contains(HouseholdMigration, migrations);
        Assert.Contains(BlacklistMigration, migrations);
        Assert.Contains(AuditMigration, migrations);
        Assert.True(string.CompareOrdinal(gate, AuditMigration) > 0, $"{gate} must sort after {AuditMigration}");
        Assert.True(string.CompareOrdinal(AuditMigration, BlacklistMigration) > 0);
        Assert.True(string.CompareOrdinal(BlacklistMigration, HouseholdMigration) > 0);
        // the reporting migration follows the gate migration directly; migrations of other modules may come after it
        var gateIndex = migrations.IndexOf(gate);
        Assert.EndsWith("_AddReportingIndexes", migrations[gateIndex + 1], StringComparison.Ordinal);
        Assert.Equal(migrations.OrderBy(m => m, StringComparer.Ordinal), migrations);
        Assert.DoesNotContain(migrations, m => m.Contains("ResidentVisitor", StringComparison.Ordinal));
        Assert.DoesNotContain(migrations, m => m.Contains("HardenAuditTriggers", StringComparison.Ordinal));
    }

    [Fact]
    public void Model_has_no_pending_changes_against_the_snapshot()
    {
        using var ctx = PostgresDatabaseFixture.CreateContext(PostgresDatabaseFixture.DefaultConnectionString);

        Assert.False(ctx.Database.HasPendingModelChanges(), "dotnet ef migrations has-pending-model-changes must report none");
    }

    // ---- fresh database (fixture = empty DB migrated + seeded) ----------------------------------------------------------

    [Fact]
    public async Task Fresh_database_has_audience_column_with_default_0_and_the_unique_active_slot_index()
    {
        _db.RequireAvailable();
        var cs = _db.ConnectionString;

        Assert.Equal(1, await CountAsync(cs, """
            SELECT count(*) FROM information_schema.columns
            WHERE table_name = 'ParkingZones' AND column_name = 'Audience' AND data_type = 'integer' AND is_nullable = 'NO' AND column_default = '0'
            """));
        var indexDef = await ActiveSlotIndexDefAsync(cs);
        Assert.NotNull(indexDef);
        Assert.Contains("UNIQUE", indexDef, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"SlotId\"", indexDef, StringComparison.Ordinal);
        Assert.Contains("\"Status\" = 0", indexDef, StringComparison.Ordinal);
        Assert.Contains("\"SlotId\" IS NOT NULL", indexDef, StringComparison.Ordinal);
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM pg_indexes WHERE tablename = 'ParkingSessions' AND indexname = 'IX_ParkingSessions_SlotId'"));
        Assert.Equal(1, await CountAsync(cs, $"SELECT count(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '{GateMigrationId()}'"));

        // A zone inserted without Audience gets Mixed
        await ExecAsync(cs, "INSERT INTO \"ParkingZones\" (\"ZoneCode\",\"ZoneName\",\"TotalCapacity\") VALUES ('ZDEF1','default audience',0)");
        Assert.Equal(0, await CountAsync(cs, "SELECT \"Audience\" FROM \"ParkingZones\" WHERE \"ZoneCode\" = 'ZDEF1'"));
    }

    [Fact]
    public async Task Gate_migration_creates_no_customer_vehicle_ticket_or_blacklist_structures_of_its_own()
    {
        _db.RequireAvailable();
        var cs = _db.ConnectionString;

        foreach (var table in new[] { "CustomerVehicles", "MonthlyTicketPurchases", "BlacklistEntries", "CustomerTiers", "MonthlyTicketPlans" })
        {
            Assert.Equal(0, await CountAsync(cs, $"SELECT count(*) FROM pg_tables WHERE tablename = '{table}'"));
        }

        foreach (var column in new[] { "IsResident", "ApartmentCode", "Building" })
        {
            Assert.Equal(0, await CountAsync(cs, $"SELECT count(*) FROM information_schema.columns WHERE table_name = 'Customers' AND column_name = '{column}'"));
        }

        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM pg_tables WHERE tablename = 'BlacklistedVehicles'"));
        Assert.Equal(23, await CountAsync(cs, "SELECT count(*) FROM \"Permissions\""));
    }

    [Fact]
    public async Task Empty_database_migrates_to_head_and_seeds_twice()
    {
        _db.RequireAvailable();
        var cs = await _db.CreateSiblingDatabaseAsync("gate_empty");

        await MigrateToAsync(cs, null);
        await using (var ctx = PostgresDatabaseFixture.CreateContext(cs))
        {
            await DbInitializer.InitializeAsync(ctx);
        }

        var zones = await CountAsync(cs, "SELECT count(*) FROM \"ParkingZones\"");
        var slots = await CountAsync(cs, "SELECT count(*) FROM \"ParkingSlots\"");
        await using (var ctx = PostgresDatabaseFixture.CreateContext(cs))
        {
            await DbInitializer.InitializeAsync(ctx);
        }

        Assert.Equal(zones, await CountAsync(cs, "SELECT count(*) FROM \"ParkingZones\""));
        Assert.Equal(slots, await CountAsync(cs, "SELECT count(*) FROM \"ParkingSlots\""));
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM \"ParkingZones\" WHERE \"ZoneCode\" = 'ZONE_R' AND \"Audience\" = 1"));
        Assert.NotNull(await ActiveSlotIndexDefAsync(cs));
    }

    // ---- upgrade from a populated database -------------------------------------------------------------------------------

    /// <summary>Household-model rows, zones/slots and sessions, including two Active sessions on one slot (dedupe case).</summary>
    private static async Task InsertPopulatedDataAsync(string cs)
    {
        await ExecAsync(cs, """
            INSERT INTO "VehicleTypes" ("TypeName","Description") VALUES ('MigMoto','m');
            INSERT INTO "Households" ("ApartmentCode","IsActive","CreatedAt") VALUES ('MIG-0101', true, now());
            INSERT INTO "Customers" ("FullName","PhoneNumber","Type","HouseholdId","CreatedAt","IsActive")
            SELECT 'Mig Owner','0900000001',0,h."HouseholdId",now(),true FROM "Households" h WHERE h."ApartmentCode"='MIG-0101';
            INSERT INTO "Vehicles" ("LicensePlate","VehicleTypeId","OwnerCustomerId","IsActive","CreatedAt")
            SELECT '51A-123.45', v."VehicleTypeId", c."CustomerId", true, now() FROM "VehicleTypes" v, "Customers" c WHERE v."TypeName"='MigMoto' AND c."FullName"='Mig Owner';
            INSERT INTO "MonthlyTickets" ("TicketCode","CustomerId","VehicleId","RegisteredLicensePlate","DurationMonths","VehicleTypeId","StartDate","EndDate","MonthlyPrice","Status","CreatedAt")
            SELECT 'MT-MIG-1', ve."OwnerCustomerId", ve."VehicleId", ve."LicensePlate", 1, ve."VehicleTypeId", now() - interval '1 day', now() + interval '29 day', 100000, 0, now()
            FROM "Vehicles" ve WHERE ve."LicensePlate"='51A-123.45';
            INSERT INTO "BlacklistedVehicles" ("LicensePlate","Reason","CreatedAt","IsActive") VALUES ('30G-111.11','mig',now(),true);
            INSERT INTO "ParkingZones" ("ZoneCode","ZoneName","TotalCapacity") VALUES ('ZMIG','Mig zone',2);
            INSERT INTO "ParkingSlots" ("SlotCode","ZoneName","ZoneId","VehicleTypeId","Status")
            SELECT 'ZMIG-0' || s, 'Mig zone', z."ZoneId", v."VehicleTypeId", 1 FROM "ParkingZones" z, "VehicleTypes" v, generate_series(1,2) s
            WHERE z."ZoneCode"='ZMIG' AND v."TypeName"='MigMoto';
            INSERT INTO "ParkingSessions" ("TicketCode","LicensePlate","VehicleTypeId","SlotId","CheckInTime","Status","IsMonthlyPass","CustomerType","TotalFee","PaymentMethod")
            SELECT 'TK-MIG-OLD','MIGOLD1', v."VehicleTypeId", s."SlotId", now() - interval '3 hour', 0, false, 1, 0, 0
            FROM "VehicleTypes" v, "ParkingSlots" s WHERE v."TypeName"='MigMoto' AND s."SlotCode"='ZMIG-01';
            INSERT INTO "ParkingSessions" ("TicketCode","LicensePlate","VehicleTypeId","SlotId","CheckInTime","Status","IsMonthlyPass","CustomerType","TotalFee","PaymentMethod")
            SELECT 'TK-MIG-NEW','MIGNEW1', v."VehicleTypeId", s."SlotId", now() - interval '1 hour', 0, false, 1, 0, 0
            FROM "VehicleTypes" v, "ParkingSlots" s WHERE v."TypeName"='MigMoto' AND s."SlotCode"='ZMIG-01';
            INSERT INTO "ParkingSessions" ("TicketCode","LicensePlate","VehicleTypeId","SlotId","CheckInTime","CheckOutTime","Status","IsMonthlyPass","CustomerType","TotalFee","PaymentMethod")
            SELECT 'TK-MIG-DONE','MIGDONE1', v."VehicleTypeId", s."SlotId", now() - interval '9 hour', now() - interval '8 hour', 1, false, 1, 5000, 0
            FROM "VehicleTypes" v, "ParkingSlots" s WHERE v."TypeName"='MigMoto' AND s."SlotCode"='ZMIG-01';
            INSERT INTO "ParkingSessions" ("TicketCode","LicensePlate","VehicleTypeId","SlotId","CheckInTime","Status","IsMonthlyPass","CustomerType","TotalFee","PaymentMethod")
            SELECT 'TK-MIG-OTHER','MIGOTH1', v."VehicleTypeId", s."SlotId", now() - interval '2 hour', 0, true, 0, 0, 0
            FROM "VehicleTypes" v, "ParkingSlots" s WHERE v."TypeName"='MigMoto' AND s."SlotCode"='ZMIG-02';
            """);
    }

    private static async Task AssertPopulatedDataPreservedAsync(string cs)
    {
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM \"Households\" WHERE \"ApartmentCode\" = 'MIG-0101' AND \"IsActive\""));
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM \"Customers\" WHERE \"FullName\" = 'Mig Owner' AND \"HouseholdId\" IS NOT NULL"));
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM \"Vehicles\" WHERE \"LicensePlate\" = '51A-123.45'"));
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM \"MonthlyTickets\" WHERE \"TicketCode\" = 'MT-MIG-1'"));
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM \"BlacklistedVehicles\" WHERE \"LicensePlate\" = '30G-111.11'"));
        Assert.Equal(4, await CountAsync(cs, "SELECT count(*) FROM \"ParkingSessions\" WHERE \"TicketCode\" LIKE 'TK-MIG-%'"));
    }

    [Fact]
    public async Task Upgrade_from_the_audit_migration_keeps_rows_dedupes_active_slots_then_down_and_up_again()
    {
        _db.RequireAvailable();
        var cs = await _db.CreateSiblingDatabaseAsync("gate_upgrade");
        await MigrateToAsync(cs, AuditMigration);
        Assert.Equal(0, await AudienceColumnCountAsync(cs));
        await InsertPopulatedDataAsync(cs);

        // Up
        await MigrateToAsync(cs, null);

        await AssertPopulatedDataPreservedAsync(cs);
        Assert.Equal(1, await AudienceColumnCountAsync(cs));
        Assert.Equal(0, await CountAsync(cs, "SELECT \"Audience\" FROM \"ParkingZones\" WHERE \"ZoneCode\" = 'ZMIG'"));
        Assert.NotNull(await ActiveSlotIndexDefAsync(cs));
        // Dedupe: exactly one active session stays on the slot (the newest), the older one is unlinked, not deleted
        Assert.Equal(1, await CountAsync(cs, """
            SELECT count(*) FROM "ParkingSessions" ps JOIN "ParkingSlots" s ON s."SlotId" = ps."SlotId"
            WHERE s."SlotCode" = 'ZMIG-01' AND ps."Status" = 0
            """));
        Assert.Equal(1, await CountAsync(cs, """
            SELECT count(*) FROM "ParkingSessions" ps JOIN "ParkingSlots" s ON s."SlotId" = ps."SlotId"
            WHERE s."SlotCode" = 'ZMIG-01' AND ps."Status" = 0 AND ps."TicketCode" = 'TK-MIG-NEW'
            """));
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM \"ParkingSessions\" WHERE \"TicketCode\" = 'TK-MIG-OLD' AND \"Status\" = 0 AND \"SlotId\" IS NULL"));
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM \"ParkingSessions\" WHERE \"TicketCode\" = 'TK-MIG-DONE' AND \"SlotId\" IS NOT NULL"));
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM \"ParkingSessions\" WHERE \"TicketCode\" = 'TK-MIG-OTHER' AND \"SlotId\" IS NOT NULL"));
        // The audit trail of the previous migration is untouched (append-only triggers still ENABLE ALWAYS)
        Assert.Equal(3, await CountAsync(cs,
            "SELECT count(*) FROM pg_trigger WHERE tgname IN ('TR_AuditLogs_NoUpdate','TR_AuditLogs_NoDelete','TR_AuditLogs_NoTruncate') AND tgenabled = 'A'"));

        // Down to the audit migration: column and unique index are gone, data and audit table stay
        await MigrateToAsync(cs, AuditMigration);
        Assert.Equal(0, await AudienceColumnCountAsync(cs));
        Assert.Null(await ActiveSlotIndexDefAsync(cs));
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM pg_tables WHERE tablename = 'AuditLogs'"));
        await AssertPopulatedDataPreservedAsync(cs);

        // Up again
        await MigrateToAsync(cs, null);
        Assert.Equal(1, await AudienceColumnCountAsync(cs));
        Assert.NotNull(await ActiveSlotIndexDefAsync(cs));
        await AssertPopulatedDataPreservedAsync(cs);
    }

    [Fact]
    public async Task Upgrade_from_the_household_migration_applies_blacklist_audit_and_gate_migrations()
    {
        _db.RequireAvailable();
        var cs = await _db.CreateSiblingDatabaseAsync("gate_from_giang");
        await MigrateToAsync(cs, HouseholdMigration);
        await ExecAsync(cs, """
            INSERT INTO "Households" ("ApartmentCode","IsActive","CreatedAt") VALUES ('HH-0001', true, now());
            INSERT INTO "ParkingZones" ("ZoneCode","ZoneName","TotalCapacity") VALUES ('ZHH','Household era zone',0);
            """);

        await MigrateToAsync(cs, null);

        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM \"Households\" WHERE \"ApartmentCode\" = 'HH-0001'"));
        Assert.Equal(0, await CountAsync(cs, "SELECT \"Audience\" FROM \"ParkingZones\" WHERE \"ZoneCode\" = 'ZHH'"));
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM pg_tables WHERE tablename = 'BlacklistedVehicles'"));
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM pg_tables WHERE tablename = 'AuditLogs'"));
        Assert.NotNull(await ActiveSlotIndexDefAsync(cs));
        var applied = await CountAsync(cs, "SELECT count(*) FROM \"__EFMigrationsHistory\"");
        await using var ctx = PostgresDatabaseFixture.CreateContext(cs);
        Assert.Equal(ctx.Database.GetMigrations().Count(), applied);
        Assert.Empty(await ctx.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task Unique_index_rejects_a_second_active_session_on_a_slot_but_allows_completed_ones()
    {
        _db.RequireAvailable();
        var cs = await _db.CreateSiblingDatabaseAsync("gate_index");
        await MigrateToAsync(cs, null);
        await ExecAsync(cs, """
            INSERT INTO "VehicleTypes" ("TypeName","Description") VALUES ('IdxMoto','m');
            INSERT INTO "ParkingSlots" ("SlotCode","ZoneName","VehicleTypeId","Status")
            SELECT 'IDX-01','idx', v."VehicleTypeId", 1 FROM "VehicleTypes" v WHERE v."TypeName"='IdxMoto';
            INSERT INTO "ParkingSessions" ("TicketCode","LicensePlate","VehicleTypeId","SlotId","CheckInTime","Status","IsMonthlyPass","CustomerType","TotalFee","PaymentMethod")
            SELECT 'TK-IDX-1','IDXA1', v."VehicleTypeId", s."SlotId", now(), 0, false, 1, 0, 0 FROM "VehicleTypes" v, "ParkingSlots" s WHERE v."TypeName"='IdxMoto' AND s."SlotCode"='IDX-01';
            INSERT INTO "ParkingSessions" ("TicketCode","LicensePlate","VehicleTypeId","SlotId","CheckInTime","CheckOutTime","Status","IsMonthlyPass","CustomerType","TotalFee","PaymentMethod")
            SELECT 'TK-IDX-2','IDXB1', v."VehicleTypeId", s."SlotId", now(), now(), 1, false, 1, 0, 0 FROM "VehicleTypes" v, "ParkingSlots" s WHERE v."TypeName"='IdxMoto' AND s."SlotCode"='IDX-01';
            INSERT INTO "ParkingSessions" ("TicketCode","LicensePlate","VehicleTypeId","SlotId","CheckInTime","Status","IsMonthlyPass","CustomerType","TotalFee","PaymentMethod")
            SELECT 'TK-IDX-3','IDXC1', v."VehicleTypeId", NULL, now(), 0, false, 1, 0, 0 FROM "VehicleTypes" v WHERE v."TypeName"='IdxMoto';
            INSERT INTO "ParkingSessions" ("TicketCode","LicensePlate","VehicleTypeId","SlotId","CheckInTime","Status","IsMonthlyPass","CustomerType","TotalFee","PaymentMethod")
            SELECT 'TK-IDX-4','IDXD1', v."VehicleTypeId", NULL, now(), 0, false, 1, 0, 0 FROM "VehicleTypes" v WHERE v."TypeName"='IdxMoto';
            """);

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => ExecAsync(cs, """
            INSERT INTO "ParkingSessions" ("TicketCode","LicensePlate","VehicleTypeId","SlotId","CheckInTime","Status","IsMonthlyPass","CustomerType","TotalFee","PaymentMethod")
            SELECT 'TK-IDX-5','IDXE1', v."VehicleTypeId", s."SlotId", now(), 0, false, 1, 0, 0 FROM "VehicleTypes" v, "ParkingSlots" s WHERE v."TypeName"='IdxMoto' AND s."SlotCode"='IDX-01';
            """));

        Assert.Contains("IX_ParkingSessions_SlotId_Active", ex.ToString(), StringComparison.Ordinal);
    }
}
