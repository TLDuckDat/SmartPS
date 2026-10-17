using Microsoft.Extensions.DependencyInjection;
using SmartPS.Models.GateControl;
using SmartPS.Models.Parking;
using SmartPS.Services.GateControl;
using SmartPS.Services.ParkingZones;

namespace SmartPS.Tests.Integration;

/// <summary>
/// B6 / B7: each gate-flow audit action is produced by its real operation with the expected outcome, entity type and
/// detail keys, inside the hash chain. GATE_BLACKLIST_BLOCKED never copies the free-text blacklist reason or notes.
/// PARKING_CHECKIN carries the gate category.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class ResidentVisitorAuditCatalogTests : IClassFixture<PostgresDatabaseFixture>
{
    private sealed record Expectation(AuditOutcome Outcome, string EntityType, string[] Keys);

    private static readonly IReadOnlyDictionary<string, Expectation> Catalogue = new Dictionary<string, Expectation>
    {
        [AuditActions.GateBlacklistBlocked] = new(AuditOutcome.Denied, "LicensePlate", new[] { "licensePlate", "normalizedPlate", "hadValidTicket" }),
        [AuditActions.ZoneUpdate] = new(AuditOutcome.Success, "ParkingZone", new[] { "zoneCode", "before", "after" }),
    };

    private const string SecretReason = "Chủ xe Nguyễn Văn Bí Mật, SĐT 0909123456";
    private const string SecretNotes = "CCCD 079099887766";

    private readonly PostgresDatabaseFixture _db;
    private readonly GateTestData _data;

    public ResidentVisitorAuditCatalogTests(PostgresDatabaseFixture db)
    {
        _db = db;
        _data = new GateTestData(db);
    }

    public static TheoryData<string> Actions()
    {
        var data = new TheoryData<string>();
        foreach (var action in ResidentVisitorAuditActions.All)
        {
            data.Add(action);
        }

        return data;
    }

    [Fact]
    public void Catalogue_covers_every_gate_action_and_they_are_in_AuditActions_All()
    {
        Assert.Equal(ResidentVisitorAuditActions.All.OrderBy(a => a, StringComparer.Ordinal), Catalogue.Keys.OrderBy(a => a, StringComparer.Ordinal));
        Assert.Equal(2, Catalogue.Count);
        Assert.Superset(ResidentVisitorAuditActions.All.ToHashSet(StringComparer.Ordinal), AuditActions.All.ToHashSet(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public async Task Operation_produces_expected_audit_row(string action)
    {
        _db.RequireAvailable();
        var expectation = Catalogue[action];

        var (idBefore, expectedEntityId) = await ProduceAsync(action);

        var rows = await AuditDb.RowsAfterAsync(_db.Factory, idBefore, action);
        var row = Assert.Single(rows);
        Assert.Equal(expectation.Outcome, row.Outcome);
        Assert.Equal(expectation.EntityType, row.EntityType);
        Assert.Equal(expectedEntityId, row.EntityId);
        Assert.NotNull(row.UserId);
        AuditDb.HasKeys(row, expectation.Keys);
        Assert.Equal(AuditHashing.ComputeHash(row.PrevHash, row), row.Hash);

        foreach (var r in await AuditDb.RowsAfterAsync(_db.Factory, idBefore))
        {
            Assert.DoesNotContain(SecretReason, r.Details, StringComparison.Ordinal);
            Assert.DoesNotContain(SecretNotes, r.Details, StringComparison.Ordinal);
            Assert.DoesNotContain("0909123456", r.Details, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Blocked_audit_row_has_no_reason_key_and_records_the_normalized_plate()
    {
        _db.RequireAvailable();
        var (idBefore, normalized) = await ProduceAsync(AuditActions.GateBlacklistBlocked);

        var row = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.GateBlacklistBlocked));
        var details = AuditDb.Details(row);
        Assert.False(details.TryGetProperty("reason", out _), $"blocked audit must not carry the free-text reason: {row.Details}");
        Assert.False(details.TryGetProperty("notes", out _), $"blocked audit must not carry the free-text notes: {row.Details}");
        Assert.Equal(normalized, details.GetProperty("normalizedPlate").GetString());
        Assert.False(details.GetProperty("hadValidTicket").GetBoolean());
    }

    [Fact]
    public async Task Check_in_audit_carries_the_gate_category_customer_and_zone()
    {
        _db.RequireAvailable();
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(op.Username);
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var zone = await _data.CreateZoneAsync(ZoneAudience.ResidentOnly, vt, 1);
        var resident = await _data.CreateResidentAsync(vt);
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var result = await sp.GetRequiredService<IGateControlService>().ProcessCheckInAsync(
            new GateCheckInRequest { LicensePlate = resident.NormalizedPlate, VehicleTypeId = vt });

        Assert.True(result.Success, result.Message);
        var row = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.ParkingCheckIn));
        AuditDb.HasKeys(row, "licensePlate", "ticketCode", "vehicleTypeId", "slotCode", "isMonthlyPass", "category", "customerId", "zoneCode");
        var details = AuditDb.Details(row);
        Assert.Equal("Resident", details.GetProperty("category").GetString());
        Assert.Equal(resident.CustomerId, details.GetProperty("customerId").GetInt32());
        Assert.Equal(zone.ZoneCode, details.GetProperty("zoneCode").GetString());
        Assert.False(details.GetProperty("isMonthlyPass").GetBoolean());
    }

    /// <summary>Runs the producing operation; returns the audit watermark taken just before it and the expected EntityId.</summary>
    private async Task<(long IdBefore, string EntityId)> ProduceAsync(string action)
    {
        var manager = await TestUsers.CreateAsync(_db.Factory, "Manager");
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(manager.Username);
        var gate = sp.GetRequiredService<IGateControlService>();
        Task<long> Mark() => AuditDb.MaxIdAsync(_db.Factory);

        switch (action)
        {
            case AuditActions.GateBlacklistBlocked:
            {
                var vt = await _data.CreateIsolatedVehicleTypeAsync();
                await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
                var plate = GateTestData.UniqueNormalizedPlate();
                await _data.AddBlacklistAsync(GateTestData.Raw(plate), SecretReason, notes: SecretNotes);
                var id = await Mark();
                var result = await gate.ProcessCheckInAsync(new GateCheckInRequest { LicensePlate = plate, VehicleTypeId = vt });
                Assert.True(result.IsBlacklisted, result.Message);
                return (id, plate);
            }

            case AuditActions.ZoneUpdate:
            {
                var vt = await _data.CreateIsolatedVehicleTypeAsync();
                var zone = await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
                var id = await Mark();
                Assert.True((await sp.GetRequiredService<IParkingZoneService>().UpdateAudienceAsync(zone.ZoneId, ZoneAudience.VisitorOnly)).Success);
                return (id, zone.ZoneId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            default:
                throw new InvalidOperationException($"No producer for {action}");
        }
    }
}
