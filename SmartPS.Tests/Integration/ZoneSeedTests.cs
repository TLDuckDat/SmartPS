using SmartPS.Data;

namespace SmartPS.Tests.Integration;

/// <summary>
/// B3 / AC-R10: the seed creates the resident-only zone ZONE_R (4 motorbike + 2 car slots), keeps ZONE_A/ZONE_B Mixed,
/// seeds no customer/vehicle/ticket/blacklist rows of its own, and re-running it changes nothing (and keeps an audience
/// changed in the UI).
/// </summary>
[Collection(DatabaseCollection.Name)]
[Trait("Category", "Integration")]
public class ZoneSeedTests : IClassFixture<SqlServerDatabaseFixture>
{
    private readonly SqlServerDatabaseFixture _db;

    public ZoneSeedTests(SqlServerDatabaseFixture db)
    {
        _db = db;
    }

    private static Task<long> CountAsync(string cs, string sql) => SqlServerDatabaseFixture.ScalarAsync<long>(cs, sql);

    private Task<long> CountAsync(string sql) => CountAsync(_db.ConnectionString, sql);

    private static async Task<Dictionary<string, long>> SnapshotAsync(string cs)
    {
        var result = new Dictionary<string, long>();
        foreach (var table in new[] { "ParkingZones", "ParkingSlots", "Households", "Customers", "Vehicles", "MonthlyTickets", "BlacklistedVehicles", "PricingRules", "Permissions", "RolePermissions" })
        {
            result[table] = await CountAsync(cs, $"SELECT count(*) FROM \"{table}\"");
        }

        return result;
    }

    private static async Task SeedAsync(string cs)
    {
        await using var ctx = SqlServerDatabaseFixture.CreateContext(cs);
        await DbInitializer.InitializeAsync(ctx);
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
    public async Task Seed_creates_no_customers_vehicles_tickets_or_blacklist_rows()
    {
        _db.RequireAvailable();

        Assert.Equal(0, await CountAsync("SELECT count(*) FROM \"Customers\""));
        Assert.Equal(0, await CountAsync("SELECT count(*) FROM \"Vehicles\""));
        Assert.Equal(0, await CountAsync("SELECT count(*) FROM \"MonthlyTickets\""));
        Assert.Equal(0, await CountAsync("SELECT count(*) FROM \"BlacklistedVehicles\""));
        Assert.Equal(0, await CountAsync("SELECT count(*) FROM \"Permissions\" WHERE \"PermissionName\" LIKE 'Customer.%' OR \"PermissionName\" LIKE 'Blacklist.%'"));
    }

    [Fact]
    public async Task Rerunning_the_seed_changes_nothing()
    {
        _db.RequireAvailable();
        var cs = await _db.CreateSiblingDatabaseAsync("zone_seed");
        await SeedAsync(cs);

        var before = await SnapshotAsync(cs);
        await SeedAsync(cs);

        Assert.Equal(before, await SnapshotAsync(cs));
        Assert.Equal(3, before["ParkingZones"]);
    }

    [Fact]
    public async Task Rerunning_the_seed_keeps_an_audience_changed_in_the_UI()
    {
        _db.RequireAvailable();
        var cs = await _db.CreateSiblingDatabaseAsync("zone_seed_keep");
        await SeedAsync(cs);
        await using (var ctx = SqlServerDatabaseFixture.CreateContext(cs))
        {
            await Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.ExecuteSqlRawAsync(ctx.Database,
                "UPDATE \"ParkingZones\" SET \"Audience\" = 0 WHERE \"ZoneCode\" = 'ZONE_R'; UPDATE \"ParkingZones\" SET \"Audience\" = 2 WHERE \"ZoneCode\" = 'ZONE_A';");
        }

        await SeedAsync(cs);

        Assert.Equal(0, await CountAsync(cs, "SELECT \"Audience\" FROM \"ParkingZones\" WHERE \"ZoneCode\" = 'ZONE_R'"));
        Assert.Equal(2, await CountAsync(cs, "SELECT \"Audience\" FROM \"ParkingZones\" WHERE \"ZoneCode\" = 'ZONE_A'"));
    }
}
