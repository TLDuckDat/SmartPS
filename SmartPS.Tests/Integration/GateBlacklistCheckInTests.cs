using Microsoft.Extensions.DependencyInjection;
using SmartPS.Models.GateControl;
using SmartPS.Models.Parking;
using SmartPS.Services.GateControl;

namespace SmartPS.Tests.Integration;

/// <summary>
/// AC-R5 / B6 / addendum 2 (D3 revised): the gate blocks plates of the existing blacklist table (BlacklistedVehicles,
/// plates stored raw) — no session, no slot, barrier stays closed (Success=false), one GATE_BLACKLIST_BLOCKED Denied row
/// without the free-text reason. Inactive rows do not block. Blacklist wins over residency and tickets.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class GateBlacklistCheckInTests : IClassFixture<PostgresDatabaseFixture>
{
    private readonly PostgresDatabaseFixture _db;
    private readonly GateTestData _data;

    public GateBlacklistCheckInTests(PostgresDatabaseFixture db)
    {
        _db = db;
        _data = new GateTestData(db);
    }

    private async Task<(ServiceProvider Sp, int UserId)> OperatorAsync()
    {
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(op.Username);
        return (sp, op.UserId);
    }

    private static Task<GateCheckInResult> CheckInAsync(IServiceProvider sp, string plate, int vehicleTypeId)
        => sp.GetRequiredService<IGateControlService>().ProcessCheckInAsync(new GateCheckInRequest { LicensePlate = plate, VehicleTypeId = vehicleTypeId });

    [Fact]
    public async Task AC_R5_raw_stored_51F_123_45_blocks_gate_input_51f12345_with_one_denied_audit_row()
    {
        _db.RequireAvailable();
        var (sp, userId) = await OperatorAsync();
        using var spScope = sp;
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var zone = await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 2);
        const string Reason = "Nợ phí gửi xe nhiều lần, chưa thanh toán";
        await _data.AddBlacklistAsync("51F-123.45", Reason);
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var result = await CheckInAsync(sp, "51f12345", vt);

        Assert.False(result.Success);
        Assert.True(result.IsBlacklisted);
        Assert.False(result.IsPermissionDenied);
        Assert.Equal(CheckInRejectReason.Blacklisted, result.RejectReason);
        Assert.Equal(VehicleCategory.Blacklisted, result.Category);
        Assert.Equal(Reason, result.BlacklistReason);
        Assert.Contains(Reason, result.Message, StringComparison.Ordinal);
        Assert.Null(result.Session);
        Assert.Equal(0, await _data.SessionCountForPlateAsync("51F12345"));
        Assert.Equal(0, await _data.OccupiedSlotCountInZoneAsync(zone.ZoneId));

        var row = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.GateBlacklistBlocked));
        Assert.Equal(AuditOutcome.Denied, row.Outcome);
        Assert.Equal("LicensePlate", row.EntityType);
        Assert.Equal("51F12345", row.EntityId);
        Assert.Equal(userId, row.UserId);
        AuditDb.HasKeys(row, "licensePlate", "normalizedPlate", "hadValidTicket");
        var details = AuditDb.Details(row);
        Assert.Equal("51F12345", details.GetProperty("normalizedPlate").GetString());
        Assert.False(details.GetProperty("hadValidTicket").GetBoolean());
        Assert.DoesNotContain(Reason, row.Details, StringComparison.Ordinal);
        Assert.Empty(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.ParkingCheckIn));
    }

    [Fact]
    public async Task Blacklist_wins_over_a_resident_with_a_valid_ticket()
    {
        _db.RequireAvailable();
        var (sp, _) = await OperatorAsync();
        using var spScope = sp;
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var residentZone = await _data.CreateZoneAsync(ZoneAudience.ResidentOnly, vt, 1);
        var resident = await _data.CreateResidentAsync(vt, withTicket: true);
        await _data.AddBlacklistAsync(resident.RawPlate, "Xe được báo mất cắp", customerId: resident.CustomerId);
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var result = await CheckInAsync(sp, GateTestData.Decorate(resident.NormalizedPlate), vt);

        Assert.False(result.Success);
        Assert.True(result.IsBlacklisted);
        Assert.Equal(CheckInRejectReason.Blacklisted, result.RejectReason);
        Assert.Equal("Xe được báo mất cắp", result.BlacklistReason);
        Assert.Equal(0, await _data.SessionCountForPlateAsync(resident.NormalizedPlate));
        Assert.Equal(SlotStatus.Available, await _data.SlotStatusAsync(residentZone.SlotIds[0]));

        var row = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.GateBlacklistBlocked));
        Assert.Equal(AuditOutcome.Denied, row.Outcome);
        Assert.Equal(resident.NormalizedPlate, row.EntityId);
        Assert.True(AuditDb.Details(row).GetProperty("hadValidTicket").GetBoolean());
        Assert.Empty(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.ParkingCheckIn));
    }

    [Fact]
    public async Task Blacklist_wins_over_a_non_resident_monthly_pass()
    {
        _db.RequireAvailable();
        var (sp, _) = await OperatorAsync();
        using var spScope = sp;
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
        var monthly = await _data.CreateExternalAsync(vt, withTicket: true);
        await _data.AddBlacklistAsync(monthly.NormalizedPlate.ToLowerInvariant(), "Gây rối");

        var result = await CheckInAsync(sp, monthly.RawPlate, vt);

        Assert.False(result.Success);
        Assert.Equal(CheckInRejectReason.Blacklisted, result.RejectReason);
        Assert.Equal(0, await _data.SessionCountForPlateAsync(monthly.NormalizedPlate));
    }

    [Fact]
    public async Task Inactive_blacklist_row_does_not_block()
    {
        _db.RequireAvailable();
        var (sp, _) = await OperatorAsync();
        using var spScope = sp;
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
        var plate = GateTestData.UniqueNormalizedPlate();
        await _data.AddBlacklistAsync(GateTestData.Raw(plate), isActive: false);
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var result = await CheckInAsync(sp, plate, vt);

        Assert.True(result.Success, result.Message);
        Assert.False(result.IsBlacklisted);
        Assert.Equal(VehicleCategory.Visitor, result.Category);
        Assert.Empty(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.GateBlacklistBlocked));
    }

    [Fact]
    public async Task Deactivated_blacklist_row_stops_blocking_the_next_check_in()
    {
        _db.RequireAvailable();
        var (sp, _) = await OperatorAsync();
        using var spScope = sp;
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
        var plate = GateTestData.UniqueNormalizedPlate();
        var id = await _data.AddBlacklistAsync(GateTestData.Raw(plate));

        var blocked = await CheckInAsync(sp, plate, vt);
        await _data.SetBlacklistActiveAsync(id, false);
        var admitted = await CheckInAsync(sp, plate, vt);

        Assert.Equal(CheckInRejectReason.Blacklisted, blocked.RejectReason);
        Assert.True(admitted.Success, admitted.Message);
    }

    [Fact]
    public async Task One_active_row_among_inactive_ones_still_blocks()
    {
        _db.RequireAvailable();
        var (sp, _) = await OperatorAsync();
        using var spScope = sp;
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
        var plate = GateTestData.UniqueNormalizedPlate();
        await _data.AddBlacklistAsync(plate, "cũ", isActive: false);
        await _data.AddBlacklistAsync(GateTestData.Raw(plate), "đang cấm");

        var result = await CheckInAsync(sp, plate, vt);

        Assert.False(result.Success);
        Assert.Equal(CheckInRejectReason.Blacklisted, result.RejectReason);
        Assert.Equal("đang cấm", result.BlacklistReason);
    }
}
