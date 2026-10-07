using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using SmartPS.Models.GateControl;
using SmartPS.Models.Parking;
using SmartPS.Services.GateControl;

namespace SmartPS.Tests.Integration;

/// <summary>N2: classification + slot preview run in the database (no full ticket load) and stay ≤ 200 ms with 10k customers.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class ClassificationPerformanceTests : IClassFixture<PostgresDatabaseFixture>
{
    private const int CustomerCount = 10_000;

    private readonly PostgresDatabaseFixture _db;
    private readonly ResidentVisitorData _data;

    public ClassificationPerformanceTests(PostgresDatabaseFixture db)
    {
        _db = db;
        _data = new ResidentVisitorData(db);
    }

    private async Task SeedBulkAsync(int vehicleTypeId)
    {
        // 10k resident customers, one active vehicle and one valid ticket each (plates PF0000001..PF0010000).
        await _db.ExecuteAsync($"""
            INSERT INTO "Customers" ("FullName","PhoneNumber","DefaultLicensePlate","Type","CreatedAt","IsActive","IsResident","ApartmentCode")
            SELECT 'Perf ' || i, '07' || lpad(i::text, 8, '0'), 'PF' || lpad(i::text, 7, '0'), 3, now(), true, true, 'P-' || i
            FROM generate_series(1, {CustomerCount}) i;
            INSERT INTO "CustomerVehicles" ("CustomerId","LicensePlate","VehicleTypeId","IsActive","CreatedAt")
            SELECT c."CustomerId", c."DefaultLicensePlate", @vt, true, now() FROM "Customers" c WHERE c."FullName" LIKE 'Perf %';
            INSERT INTO "MonthlyTickets" ("TicketCode","CustomerId","RegisteredLicensePlate","VehicleTypeId","StartDate","EndDate","MonthlyPrice","Status","CreatedAt")
            SELECT 'MT-PERF-' || c."CustomerId", c."CustomerId", c."DefaultLicensePlate", @vt, now() - interval '5 day', now() + interval '25 day', 120000, 0, now()
            FROM "Customers" c WHERE c."FullName" LIKE 'Perf %';
            ANALYZE "Customers"; ANALYZE "CustomerVehicles"; ANALYZE "MonthlyTickets"; ANALYZE "BlacklistEntries";
            """, ("vt", vehicleTypeId));
    }

    [Fact]
    public async Task N2_classify_and_suggest_median_is_at_most_200_ms_with_10k_customers()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        await _data.CreateZoneAsync(ZoneAudience.ResidentOnly, vt, 5);
        await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 5);
        await SeedBulkAsync(vt);
        Assert.True(await _data.CountAsync("SELECT count(*) FROM \"MonthlyTickets\" WHERE \"TicketCode\" LIKE 'MT-PERF-%'") == CustomerCount);

        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(op.Username);
        var gate = sp.GetRequiredService<IGateControlService>();

        async Task<long> MeasureAsync(string plate)
        {
            var sw = Stopwatch.StartNew();
            await using (var db = _db.CreateContext())
            {
                var classification = await GateClassificationQueries.ClassifyAsync(db, LicensePlateNormalizer.Normalize(plate), DateTime.UtcNow);
                Assert.Equal(VehicleCategory.Resident, classification.Category);
                var slot = await gate.SuggestAvailableSlotAsync(vt, classification.Category);
                Assert.NotNull(slot);
            }

            sw.Stop();
            return sw.ElapsedMilliseconds;
        }

        // Warm-up (connection pool, query plans)
        await MeasureAsync("PF-0000001");
        await MeasureAsync("PF-0000002");

        var samples = new List<long>();
        foreach (var i in new[] { 4321, 9999, 17, 5000, 777 })
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
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(op.Username);
        var gate = sp.GetRequiredService<IGateControlService>();
        await gate.ClassifyVehicleAsync("51F12345"); // warm-up

        var samples = new List<long>();
        for (var i = 0; i < 5; i++)
        {
            var sw = Stopwatch.StartNew();
            var c = await gate.ClassifyVehicleAsync("51F-123.45");
            sw.Stop();
            Assert.Equal(VehicleCategory.Resident, c.Category);
            samples.Add(sw.ElapsedMilliseconds);
        }

        samples.Sort();
        Assert.True(samples[2] <= 200, $"median {samples[2]} ms");
    }
}
