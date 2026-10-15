using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using SmartPS.Models.GateControl;
using SmartPS.Models.Parking;
using SmartPS.Services.GateControl;

namespace SmartPS.Tests.Integration;

/// <summary>
/// N2 / plan risk R3: owner + ticket lookup on the household model (raw plates normalized in SQL, no functional index on
/// the household module's tables) plus the slot preview stay ≤ 200 ms (median) with 5,000 households/vehicles/tickets.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class ClassificationPerformanceTests : IClassFixture<PostgresDatabaseFixture>
{
    private const int OwnerCount = 5_000;

    private readonly PostgresDatabaseFixture _db;
    private readonly GateTestData _data;

    public ClassificationPerformanceTests(PostgresDatabaseFixture db)
    {
        _db = db;
        _data = new GateTestData(db);
    }

    /// <summary>
    /// 5k households, one customer each, one active vehicle each stored RAW as "PF-000.0001" style, one valid ticket each
    /// (normalized plates PF0000001..PF0005000).
    /// </summary>
    private async Task SeedBulkAsync(int vehicleTypeId)
    {
        await _db.ExecuteAsync($"""
            INSERT INTO "Households" ("ApartmentCode","IsActive","CreatedAt")
            SELECT 'PF-' || i, true, now() FROM generate_series(1, {OwnerCount}) i;
            INSERT INTO "Customers" ("FullName","PhoneNumber","Type","HouseholdId","CreatedAt","IsActive")
            SELECT 'Perf ' || h."ApartmentCode", '07' || lpad(substr(h."ApartmentCode", 4), 8, '0'), 1, h."HouseholdId", now(), true
            FROM "Households" h WHERE h."ApartmentCode" LIKE 'PF-%';
            INSERT INTO "Vehicles" ("LicensePlate","VehicleTypeId","OwnerCustomerId","IsActive","CreatedAt")
            SELECT 'PF-' || substr(lpad(substr(c."FullName", 9), 7, '0'), 1, 3) || '.' || substr(lpad(substr(c."FullName", 9), 7, '0'), 4),
                   @vt, c."CustomerId", true, now() - interval '30 day'
            FROM "Customers" c WHERE c."FullName" LIKE 'Perf PF-%';
            INSERT INTO "MonthlyTickets" ("TicketCode","CustomerId","VehicleId","RegisteredLicensePlate","DurationMonths","VehicleTypeId","StartDate","EndDate","MonthlyPrice","Status","CreatedAt")
            SELECT 'MT-PERF-' || v."VehicleId", v."OwnerCustomerId", v."VehicleId", v."LicensePlate", 1, @vt, now() - interval '5 day', now() + interval '25 day', 100000, 0, now()
            FROM "Vehicles" v WHERE v."LicensePlate" LIKE 'PF-%';
            ANALYZE "Households"; ANALYZE "Customers"; ANALYZE "Vehicles"; ANALYZE "MonthlyTickets"; ANALYZE "BlacklistedVehicles";
            """, ("vt", vehicleTypeId));
    }

    [Fact]
    public async Task N2_owner_lookup_classify_and_suggest_median_is_at_most_200_ms_with_5k_owners()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        await _data.CreateZoneAsync(ZoneAudience.ResidentOnly, vt, 5);
        await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 5);
        await SeedBulkAsync(vt);
        Assert.Equal(OwnerCount, await _data.CountAsync("SELECT count(*) FROM \"MonthlyTickets\" WHERE \"TicketCode\" LIKE 'MT-PERF-%'"));
        Assert.Equal(1, await _data.CountAsync("SELECT count(*) FROM \"Vehicles\" WHERE \"LicensePlate\" = 'PF-000.4321'"));

        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(op.Username);
        var gate = sp.GetRequiredService<IGateControlService>();
        var reader = new PlateOwnershipReader();

        async Task<long> MeasureAsync(string plate)
        {
            var sw = Stopwatch.StartNew();
            await using (var db = _db.CreateContext())
            {
                var norm = LicensePlateNormalizer.Normalize(plate);
                var now = DateTime.UtcNow;
                var owner = await reader.ReadAsync(db, norm, now);
                var classification = VehicleClassifier.Classify(norm, null, owner, vt, now);
                Assert.Equal(VehicleCategory.Resident, classification.Category);
                Assert.True(classification.IsMonthlyPass);
                var slot = await gate.SuggestAvailableSlotAsync(vt, classification.Category);
                Assert.NotNull(slot);
            }

            sw.Stop();
            return sw.ElapsedMilliseconds;
        }

        // Warm-up (connection pool, query plans)
        await MeasureAsync("PF0000001");
        await MeasureAsync("pf-000.0002");

        var samples = new List<long>();
        foreach (var i in new[] { 4321, 4999, 17, 2500, 777 })
        {
            samples.Add(await MeasureAsync($"pf{i:D7}"));
        }

        samples.Sort();
        var median = samples[samples.Count / 2];
        Assert.True(median <= 200, $"median {median} ms over samples [{string.Join(", ", samples)}]");
    }

    [Fact]
    public async Task Gate_preview_classification_is_also_fast()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var resident = await _data.CreateResidentAsync(vt);
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(op.Username);
        var gate = sp.GetRequiredService<IGateControlService>();
        await gate.ClassifyVehicleAsync(resident.NormalizedPlate); // warm-up

        var samples = new List<long>();
        for (var i = 0; i < 5; i++)
        {
            var sw = Stopwatch.StartNew();
            var c = await gate.ClassifyVehicleAsync(resident.RawPlate);
            sw.Stop();
            Assert.Equal(VehicleCategory.Resident, c.Category);
            samples.Add(sw.ElapsedMilliseconds);
        }

        samples.Sort();
        Assert.True(samples[2] <= 200, $"median {samples[2]} ms");
    }
}
