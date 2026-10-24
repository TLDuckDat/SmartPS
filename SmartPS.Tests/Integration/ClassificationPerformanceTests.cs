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
[Collection(DatabaseCollection.Name)]
[Trait("Category", "Integration")]
public class ClassificationPerformanceTests : IClassFixture<SqlServerDatabaseFixture>
{
    private const int OwnerCount = 5_000;

    private readonly SqlServerDatabaseFixture _db;
    private readonly GateTestData _data;

    public ClassificationPerformanceTests(SqlServerDatabaseFixture db)
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
            WITH nums AS (
                SELECT TOP ({OwnerCount}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i
                FROM sys.all_columns a CROSS JOIN sys.all_columns b
            )
            INSERT INTO "Households" ("ApartmentCode","IsActive","CreatedAt")
            SELECT N'PF-' + CAST(i AS nvarchar(10)), 1, SYSUTCDATETIME() FROM nums;
            INSERT INTO "Customers" ("FullName","PhoneNumber","Type","HouseholdId","CreatedAt","IsActive")
            SELECT N'Perf ' + h."ApartmentCode", N'07' + RIGHT(N'00000000' + SUBSTRING(h."ApartmentCode", 4, 20), 8), 1, h."HouseholdId", SYSUTCDATETIME(), 1
            FROM "Households" h WHERE h."ApartmentCode" LIKE N'PF-%';
            INSERT INTO "Vehicles" ("LicensePlate","VehicleTypeId","OwnerCustomerId","IsActive","CreatedAt")
            SELECT N'PF-' + SUBSTRING(p.digits, 1, 3) + N'.' + SUBSTRING(p.digits, 4, 20),
                   @vt, c."CustomerId", 1, DATEADD(day, -30, SYSUTCDATETIME())
            FROM "Customers" c
            CROSS APPLY (SELECT RIGHT(N'0000000' + SUBSTRING(c."FullName", 9, 20), 7) AS digits) p
            WHERE c."FullName" LIKE N'Perf PF-%';
            INSERT INTO "MonthlyTickets" ("TicketCode","CustomerId","VehicleId","RegisteredLicensePlate","DurationMonths","VehicleTypeId","StartDate","EndDate","MonthlyPrice","Status","CreatedAt")
            SELECT N'MT-PERF-' + CAST(v."VehicleId" AS nvarchar(10)), v."OwnerCustomerId", v."VehicleId", v."LicensePlate", 1, @vt,
                   DATEADD(day, -5, SYSUTCDATETIME()), DATEADD(day, 25, SYSUTCDATETIME()), 100000, 0, SYSUTCDATETIME()
            FROM "Vehicles" v WHERE v."LicensePlate" LIKE N'PF-%';
            UPDATE STATISTICS "Households"; UPDATE STATISTICS "Customers"; UPDATE STATISTICS "Vehicles";
            UPDATE STATISTICS "MonthlyTickets"; UPDATE STATISTICS "BlacklistedVehicles";
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
