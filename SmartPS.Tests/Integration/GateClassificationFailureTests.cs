using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SmartPS.Models.GateControl;
using SmartPS.Models.Parking;
using SmartPS.Services.GateControl;

namespace SmartPS.Tests.Integration;

/// <summary>
/// M1 / N3 / R19: (a) a failed classification query never produces a DB session (memory/JSONL path only, so the
/// blacklist cannot be bypassed in the database); (b) a blacklist entry added after classification is caught by the
/// re-check inside the audited block.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class GateClassificationFailureTests : IClassFixture<PostgresDatabaseFixture>
{
    private static readonly string JsonlPath = Path.Combine(AppContext.BaseDirectory, "Storage", "gate_audit_log.jsonl");

    private readonly PostgresDatabaseFixture _db;
    private readonly ResidentVisitorData _data;

    public GateClassificationFailureTests(PostgresDatabaseFixture db)
    {
        _db = db;
        _data = new ResidentVisitorData(db);
    }

    private static JsonElement? LastJsonl(string plate, string action)
    {
        if (!File.Exists(JsonlPath))
        {
            return null;
        }

        var line = File.ReadAllLines(JsonlPath)
            .LastOrDefault(l => l.Contains(plate, StringComparison.Ordinal) && l.Contains($"\"{action}\"", StringComparison.Ordinal));
        if (line is null)
        {
            return null;
        }

        using var doc = JsonDocument.Parse(line);
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task M1a_failed_blacklist_query_falls_back_to_memory_and_writes_no_db_session()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 2);
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        var hook = new CommandHookInterceptor("BlacklistEntries", occurrence: 1, throwInstead: true);
        using var sp = IntegrationServices.Create(_db, CommandHookInterceptor.Install(_db, hook));
        await sp.LoginAsync(op.Username);
        var plate = ResidentVisitorData.UniqueNormalizedPlate();
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);
        var occupiedBefore = await _data.OccupiedSlotCountAsync();

        var result = await sp.GetRequiredService<IGateControlService>().ProcessCheckInAsync(new GateCheckInRequest { LicensePlate = plate, VehicleTypeId = vt });

        Assert.Equal(1, hook.FiredCount);
        Assert.True(result.Success, result.Message);
        Assert.False(result.IsBlacklisted);
        Assert.Equal(0, await _data.SessionCountForPlateAsync(plate));
        Assert.Empty(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.ParkingCheckIn));
        Assert.Equal(occupiedBefore, await _data.OccupiedSlotCountAsync());

        var line = LastJsonl(plate, "CHECK_IN");
        Assert.NotNull(line);
        Assert.Equal(JsonValueKind.False, line!.Value.GetProperty("DbAudit").ValueKind);
    }

    [Fact]
    public async Task M1b_blacklist_added_after_classification_is_caught_inside_the_transaction()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var zone = await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
        var plate = ResidentVisitorData.UniqueNormalizedPlate();
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");

        // The first MonthlyTickets query happens during classification (after or alongside its blacklist lookup);
        // the entry is inserted right before it runs, through an unhooked connection.
        var hook = new CommandHookInterceptor("MonthlyTickets", occurrence: 1, before: async () => await _data.AddBlacklistAsync(plate, "Thêm trong lúc phân loại"));
        using var sp = IntegrationServices.Create(_db, CommandHookInterceptor.Install(_db, hook));
        await sp.LoginAsync(op.Username);
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var result = await sp.GetRequiredService<IGateControlService>().ProcessCheckInAsync(new GateCheckInRequest { LicensePlate = plate, VehicleTypeId = vt });

        Assert.Equal(1, hook.FiredCount);
        Assert.False(result.Success);
        Assert.True(result.IsBlacklisted);
        Assert.Equal(CheckInRejectReason.Blacklisted, result.RejectReason);
        Assert.Equal("Thêm trong lúc phân loại", result.BlacklistReason);
        Assert.Equal(0, await _data.SessionCountForPlateAsync(plate));
        Assert.Equal(SlotStatus.Available, await _data.SlotStatusAsync(zone.SlotIds[0]));
        Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.GateBlacklistBlocked));
        Assert.Empty(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.ParkingCheckIn));
    }
}
