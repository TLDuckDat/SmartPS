using Microsoft.Extensions.DependencyInjection;
using SmartPS.Models.GateControl;
using SmartPS.Models.Parking;
using SmartPS.Services.GateControl;

namespace SmartPS.Tests.Integration;

/// <summary>AC-4 / E2 / R19: a blacklisted plate is blocked at check-in (no session, no slot), audited GATE_BLACKLIST_BLOCKED Denied with the reason.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class GateBlacklistCheckInTests : IClassFixture<PostgresDatabaseFixture>
{
    private const string SeedReason = "Nợ phí gửi xe nhiều lần, chưa thanh toán";

    private readonly PostgresDatabaseFixture _db;
    private readonly ResidentVisitorData _data;

    public GateBlacklistCheckInTests(PostgresDatabaseFixture db)
    {
        _db = db;
        _data = new ResidentVisitorData(db);
    }

    private async Task<(ServiceProvider Sp, int UserId)> OperatorAsync()
    {
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(op.Username);
        return (sp, op.UserId);
    }

    [Fact]
    public async Task AC4_seed_blacklisted_plate_is_blocked_with_one_denied_audit_row()
    {
        _db.RequireAvailable();
        var (sp, userId) = await OperatorAsync();
        using var spScope = sp;
        var moto = await _data.MotorbikeTypeIdAsync();
        var entryId = await PostgresDatabaseFixture.ScalarAsync<int>(_db.ConnectionString,
            "SELECT \"BlacklistEntryId\" FROM \"BlacklistEntries\" WHERE \"LicensePlate\" = '29A99999' AND \"IsActive\"");
        var occupiedBefore = await _data.OccupiedSlotCountAsync();
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var result = await sp.GetRequiredService<IGateControlService>().ProcessCheckInAsync(new GateCheckInRequest
        {
            LicensePlate = "29A-999.99",
            VehicleTypeId = moto
        });

        Assert.False(result.Success);
        Assert.True(result.IsBlacklisted);
        Assert.False(result.IsPermissionDenied);
        Assert.Equal(CheckInRejectReason.Blacklisted, result.RejectReason);
        Assert.Equal(VehicleCategory.Blacklisted, result.Category);
        Assert.Equal(SeedReason, result.BlacklistReason);
        Assert.Null(result.Session);
        Assert.Contains(SeedReason, result.Message, StringComparison.Ordinal);
        Assert.Equal(0, await _data.SessionCountForPlateAsync("29A99999"));
        Assert.Equal(occupiedBefore, await _data.OccupiedSlotCountAsync());

        var row = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.GateBlacklistBlocked));
        Assert.Equal(AuditOutcome.Denied, row.Outcome);
        Assert.Equal("BlacklistEntry", row.EntityType);
        Assert.Equal(entryId.ToString(), row.EntityId);
        Assert.Equal(userId, row.UserId);
        AuditDb.HasKeys(row, "licensePlate", "normalizedPlate", "reason", "hadValidTicket");
        var details = AuditDb.Details(row);
        Assert.Equal("29A99999", details.GetProperty("normalizedPlate").GetString());
        Assert.Equal(SeedReason, details.GetProperty("reason").GetString());
        Assert.False(details.GetProperty("hadValidTicket").GetBoolean());
        Assert.Empty(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.ParkingCheckIn));
    }

    [Fact]
    public async Task E2_blacklist_wins_over_a_valid_resident_ticket()
    {
        _db.RequireAvailable();
        var (sp, _) = await OperatorAsync();
        using var spScope = sp;
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var residentZone = await _data.CreateZoneAsync(ZoneAudience.ResidentOnly, vt, 1);
        var resident = await _data.CreateSubscriberAsync(vt, isResident: true);
        var entryId = await _data.AddBlacklistAsync(resident.Plate, "Xe được báo mất cắp");
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var result = await sp.GetRequiredService<IGateControlService>().ProcessCheckInAsync(new GateCheckInRequest
        {
            LicensePlate = ResidentVisitorData.Decorate(resident.Plate),
            VehicleTypeId = vt
        });

        Assert.False(result.Success);
        Assert.True(result.IsBlacklisted);
        Assert.Equal(CheckInRejectReason.Blacklisted, result.RejectReason);
        Assert.Equal("Xe được báo mất cắp", result.BlacklistReason);
        Assert.Equal(0, await _data.SessionCountForPlateAsync(resident.Plate));
        Assert.Equal(SlotStatus.Available, await _data.SlotStatusAsync(residentZone.SlotIds[0]));

        var row = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.GateBlacklistBlocked));
        Assert.Equal(AuditOutcome.Denied, row.Outcome);
        Assert.Equal(entryId.ToString(), row.EntityId);
        Assert.True(AuditDb.Details(row).GetProperty("hadValidTicket").GetBoolean());
        Assert.Empty(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.ParkingCheckIn));
    }

    [Fact]
    public async Task Inactive_blacklist_entry_does_not_block()
    {
        _db.RequireAvailable();
        var (sp, _) = await OperatorAsync();
        using var spScope = sp;
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
        var plate = ResidentVisitorData.UniqueNormalizedPlate();
        var entryId = await _data.AddBlacklistAsync(plate);
        await _db.ExecuteAsync("UPDATE \"BlacklistEntries\" SET \"IsActive\" = false, \"RemovedAt\" = now(), \"RemoveReason\" = 'ok' WHERE \"BlacklistEntryId\" = @id", ("id", entryId));

        var result = await sp.GetRequiredService<IGateControlService>().ProcessCheckInAsync(new GateCheckInRequest { LicensePlate = plate, VehicleTypeId = vt });

        Assert.True(result.Success, result.Message);
        Assert.False(result.IsBlacklisted);
        Assert.Equal(VehicleCategory.Visitor, result.Category);
    }
}
