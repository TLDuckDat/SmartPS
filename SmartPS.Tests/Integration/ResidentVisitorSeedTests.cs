using SmartPS.Data;
using SmartPS.Services.Audit;

namespace SmartPS.Tests.Integration;

/// <summary>R22 / A13 / m4 / m5: seed zones, residents, tickets (+ one Create purchase each), blacklist; idempotent re-runs that keep UI changes.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class ResidentVisitorSeedTests : IClassFixture<PostgresDatabaseFixture>
{
    private static readonly string[] SeedPlates =
    {
        "51F12345", "59T112345", "29B188888", "30F99999", "29H155555", "29D212345", "30A67890", "59X312345"
    };

    private static readonly (string Phone, bool Resident, string? Apartment, string[] Plates)[] SeedCustomers =
    {
        ("0988123456", true, "A-1205", new[] { "51F12345", "59T112345" }),
        ("0912888999", true, "A-0803", new[] { "29B188888" }),
        ("0977345678", true, "B-1510", new[] { "30F99999", "29H155555" }),
        ("0904567890", true, "B-0402", new[] { "29D212345" }),
        ("0936789012", true, "C-2101", new[] { "30A67890" }),
        ("0911222333", false, null, new[] { "59X312345" }),
    };

    private readonly PostgresDatabaseFixture _db;

    public ResidentVisitorSeedTests(PostgresDatabaseFixture db)
    {
        _db = db;
    }

    private static Task<long> CountAsync(string cs, string sql, params (string, object?)[] p) => PostgresDatabaseFixture.ScalarAsync<long>(cs, sql, p);

    private Task<long> CountAsync(string sql, params (string, object?)[] p) => CountAsync(_db.ConnectionString, sql, p);

    private static async Task<Dictionary<string, long>> SnapshotAsync(string cs)
    {
        var result = new Dictionary<string, long>();
        foreach (var table in new[] { "ParkingZones", "ParkingSlots", "Customers", "CustomerVehicles", "MonthlyTickets", "MonthlyTicketPurchases", "BlacklistEntries", "CustomerTiers", "RolePermissions" })
        {
            result[table] = await CountAsync(cs, $"SELECT count(*) FROM \"{table}\"");
        }

        return result;
    }

    [Fact]
    public async Task Zone_R_is_resident_only_with_4_motorbike_and_2_car_slots()
    {
        _db.RequireAvailable();

        Assert.Equal(1, await CountAsync("SELECT count(*) FROM \"ParkingZones\" WHERE \"ZoneCode\"='ZONE_R' AND \"Audience\" = 1 AND \"ZoneName\" = 'Khu Cư dân (B2)'"));
        Assert.Equal(4, await CountAsync("""
            SELECT count(*) FROM "ParkingSlots" s JOIN "ParkingZones" z ON z."ZoneId" = s."ZoneId" JOIN "VehicleTypes" v ON v."VehicleTypeId" = s."VehicleTypeId"
            WHERE z."ZoneCode"='ZONE_R' AND v."TypeName"='Xe máy' AND s."SlotCode" IN ('R-M01','R-M02','R-M03','R-M04')
            """));
        Assert.Equal(2, await CountAsync("""
            SELECT count(*) FROM "ParkingSlots" s JOIN "ParkingZones" z ON z."ZoneId" = s."ZoneId" JOIN "VehicleTypes" v ON v."VehicleTypeId" = s."VehicleTypeId"
            WHERE z."ZoneCode"='ZONE_R' AND v."TypeName"='Xe ô tô' AND s."SlotCode" IN ('R-C01','R-C02')
            """));
        Assert.Equal(6, await CountAsync("SELECT count(*) FROM \"ParkingSlots\" s JOIN \"ParkingZones\" z ON z.\"ZoneId\" = s.\"ZoneId\" WHERE z.\"ZoneCode\"='ZONE_R'"));
        Assert.Equal(0, await CountAsync("SELECT count(*) FROM \"ParkingZones\" WHERE \"ZoneCode\" IN ('ZONE_A','ZONE_B') AND \"Audience\" <> 0"));
        Assert.Equal(2, await CountAsync("SELECT count(*) FROM \"ParkingZones\" WHERE \"ZoneCode\" IN ('ZONE_A','ZONE_B')"));
    }

    [Fact]
    public async Task Five_residents_and_one_non_resident_with_their_vehicles()
    {
        _db.RequireAvailable();

        foreach (var (phone, resident, apartment, plates) in SeedCustomers)
        {
            Assert.Equal(1, await CountAsync(
                "SELECT count(*) FROM \"Customers\" WHERE \"PhoneNumber\" = @p AND \"IsResident\" = @r AND \"Type\" = @t AND \"IsActive\" AND \"ApartmentCode\" IS NOT DISTINCT FROM @a",
                ("p", phone), ("r", resident), ("t", resident ? 3 : 0), ("a", apartment)));
            foreach (var plate in plates)
            {
                Assert.True(1 == await CountAsync(
                    "SELECT count(*) FROM \"CustomerVehicles\" cv JOIN \"Customers\" c ON c.\"CustomerId\" = cv.\"CustomerId\" WHERE c.\"PhoneNumber\" = @p AND cv.\"LicensePlate\" = @plate AND cv.\"IsActive\"",
                    ("p", phone), ("plate", plate)), $"{plate} is not an active vehicle of {phone}");
            }
        }

        Assert.Equal(5, await CountAsync("SELECT count(*) FROM \"Customers\" WHERE \"IsResident\" AND \"Notes\" = 'Dữ liệu mẫu'"));
        Assert.Equal(1, await CountAsync("SELECT count(*) FROM \"Customers\" WHERE NOT \"IsResident\" AND \"Notes\" = 'Dữ liệu mẫu'"));
        Assert.Equal(1, await CountAsync("SELECT count(*) FROM \"Customers\" WHERE \"PhoneNumber\" = '0988123456' AND \"Building\" = 'A'"));
        Assert.Equal(SeedPlates.Length, await CountAsync("SELECT count(*) FROM \"CustomerVehicles\" WHERE \"LicensePlate\" = ANY(@p)", ("p", SeedPlates)));
    }

    [Fact]
    public async Task Seed_tickets_cover_valid_expiring_soon_and_expired_states_on_VN_midnights()
    {
        _db.RequireAvailable();
        var now = DateTime.UtcNow;

        Assert.Equal(6, await CountAsync("SELECT count(*) FROM \"MonthlyTickets\" WHERE \"TicketCode\" LIKE 'MT-SEED-%'"));
        Assert.Equal(1, await CountAsync("SELECT count(*) FROM \"MonthlyTickets\" WHERE \"TicketCode\"='MT-SEED-001' AND \"RegisteredLicensePlate\"='51F12345' AND \"EndDate\" > @n", ("n", now.AddDays(7))));
        Assert.Equal(1, await CountAsync("SELECT count(*) FROM \"MonthlyTickets\" WHERE \"TicketCode\"='MT-SEED-002' AND \"RegisteredLicensePlate\"='29B188888' AND \"EndDate\" > @n AND \"EndDate\" <= @s",
            ("n", now), ("s", now.AddDays(7))));
        Assert.Equal(1, await CountAsync("SELECT count(*) FROM \"MonthlyTickets\" WHERE \"TicketCode\"='MT-SEED-003' AND \"RegisteredLicensePlate\"='30F99999' AND \"EndDate\" < @n", ("n", now)));
        Assert.Equal(1, await CountAsync("SELECT count(*) FROM \"MonthlyTickets\" WHERE \"TicketCode\"='MT-SEED-006' AND \"RegisteredLicensePlate\"='59X312345' AND \"EndDate\" > @n AND \"StartDate\" <= @n", ("n", now)));

        // All seed tickets: Active status, plan price, plate is an active vehicle of the ticket's customer, VN-midnight boundaries.
        Assert.Equal(0, await CountAsync("""
            SELECT count(*) FROM "MonthlyTickets" t
            LEFT JOIN "MonthlyTicketPlans" p ON p."PlanId" = t."PlanId"
            WHERE t."TicketCode" LIKE 'MT-SEED-%' AND (
                 t."Status" <> 0 OR p."PlanId" IS NULL OR t."MonthlyPrice" <> p."TotalPrice" OR t."VehicleTypeId" <> p."VehicleTypeId"
              OR NOT EXISTS (SELECT 1 FROM "CustomerVehicles" cv WHERE cv."CustomerId" = t."CustomerId" AND cv."LicensePlate" = t."RegisteredLicensePlate" AND cv."IsActive")
              OR ((t."EndDate" AT TIME ZONE 'UTC') + interval '7 hours')::time <> time '00:00'
              OR ((t."StartDate" AT TIME ZONE 'UTC') + interval '7 hours')::time <> time '00:00')
            """));
    }

    [Fact]
    public async Task M4_exactly_one_create_purchase_per_seed_ticket_matching_the_ticket()
    {
        _db.RequireAvailable();

        Assert.Equal(6, await CountAsync("""
            SELECT count(*) FROM "MonthlyTicketPurchases" pu JOIN "MonthlyTickets" t ON t."TicketId" = pu."TicketId"
            WHERE t."TicketCode" LIKE 'MT-SEED-%'
            """));
        Assert.Equal(6, await CountAsync("""
            SELECT count(*) FROM "MonthlyTicketPurchases" pu
            JOIN "MonthlyTickets" t ON t."TicketId" = pu."TicketId"
            JOIN "Users" u ON u."UserId" = pu."CreatedByUserId"
            WHERE t."TicketCode" LIKE 'MT-SEED-%' AND pu."Kind" = 0 AND pu."Price" = t."MonthlyPrice"
              AND pu."PeriodStartUtc" = t."StartDate" AND pu."PeriodEndUtc" = t."EndDate"
              AND pu."PlanId" IS NOT DISTINCT FROM t."PlanId" AND u."Username" = 'admin'
            """));
    }

    [Fact]
    public async Task Two_active_blacklist_entries_and_resident_tier()
    {
        _db.RequireAvailable();

        Assert.Equal(2, await CountAsync("""
            SELECT count(*) FROM "BlacklistEntries" b JOIN "Users" u ON u."UserId" = b."CreatedByUserId"
            WHERE b."LicensePlate" IN ('29A99999','30G11111') AND b."IsActive" AND length(b."Reason") > 0 AND u."Username" = 'admin'
            """));
        Assert.Equal(1, await CountAsync("SELECT count(*) FROM \"CustomerTiers\" WHERE \"CustomerType\" = 3"));
    }

    [Fact]
    public async Task Rerunning_the_seed_changes_nothing()
    {
        _db.RequireAvailable();
        var cs = await _db.CreateSiblingDatabaseAsync("rv_seed");
        await using (var ctx = PostgresDatabaseFixture.CreateContext(cs))
        {
            await DbInitializer.InitializeAsync(ctx);
        }

        var before = await SnapshotAsync(cs);
        await using (var ctx = PostgresDatabaseFixture.CreateContext(cs))
        {
            await DbInitializer.InitializeAsync(ctx);
        }

        Assert.Equal(before, await SnapshotAsync(cs));
        Assert.Equal(6, before["MonthlyTicketPurchases"]);
    }

    [Fact]
    public async Task Rerunning_the_seed_keeps_removed_blacklist_entries_and_changed_audiences()
    {
        _db.RequireAvailable();
        var cs = await _db.CreateSiblingDatabaseAsync("rv_seed_keep");
        await using (var ctx = PostgresDatabaseFixture.CreateContext(cs))
        {
            await DbInitializer.InitializeAsync(ctx);
        }

        await using (var ctx = PostgresDatabaseFixture.CreateContext(cs))
        {
            await Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.ExecuteSqlRawAsync(ctx.Database, """
                UPDATE "BlacklistEntries" SET "IsActive" = false, "RemovedAt" = now(), "RemoveReason" = 'paid' WHERE "LicensePlate" = '30G11111';
                UPDATE "ParkingZones" SET "Audience" = 0 WHERE "ZoneCode" = 'ZONE_R';
                UPDATE "CustomerVehicles" SET "IsActive" = false, "RemovedAt" = now() WHERE "LicensePlate" = '29H155555';
                """);
        }

        await using (var ctx = PostgresDatabaseFixture.CreateContext(cs))
        {
            await DbInitializer.InitializeAsync(ctx);
        }

        Assert.Equal(0, await CountAsync(cs, "SELECT count(*) FROM \"BlacklistEntries\" WHERE \"LicensePlate\" = '30G11111' AND \"IsActive\""));
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM \"BlacklistEntries\" WHERE \"LicensePlate\" = '30G11111'"));
        Assert.Equal(0, await CountAsync(cs, "SELECT count(*) FROM \"ParkingZones\" WHERE \"ZoneCode\" = 'ZONE_R' AND \"Audience\" <> 0"));
        Assert.Equal(0, await CountAsync(cs, "SELECT count(*) FROM \"CustomerVehicles\" WHERE \"LicensePlate\" = '29H155555' AND \"IsActive\""));
        Assert.Equal(1, await CountAsync(cs, "SELECT count(*) FROM \"CustomerVehicles\" WHERE \"LicensePlate\" = '29H155555'"));
    }

    [Fact]
    public async Task Seed_ticket_end_dates_are_relative_to_today_VN()
    {
        _db.RequireAvailable();
        var todayStart = AuditTime.VietnamDateStartUtc(DateOnly.FromDateTime(AuditTime.ToVietnamTime(DateTime.UtcNow)));

        Assert.Equal(1, await CountAsync("SELECT count(*) FROM \"MonthlyTickets\" WHERE \"TicketCode\"='MT-SEED-001' AND \"EndDate\" = @e", ("e", todayStart.AddDays(60))));
        Assert.Equal(1, await CountAsync("SELECT count(*) FROM \"MonthlyTickets\" WHERE \"TicketCode\"='MT-SEED-002' AND \"EndDate\" = @e", ("e", todayStart.AddDays(3))));
        Assert.Equal(1, await CountAsync("SELECT count(*) FROM \"MonthlyTickets\" WHERE \"TicketCode\"='MT-SEED-003' AND \"EndDate\" = @e", ("e", todayStart.AddDays(-5))));
    }
}
