using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartPS.Models.GateControl;
using SmartPS.Models.Parking;
using SmartPS.Services.Common;
using SmartPS.Services.GateControl;
using SmartPS.Services.ParkingZones;

namespace SmartPS.Tests.Integration;

/// <summary>
/// Sơ đồ bãi: tạo khu, thêm nhiều ô, chuyển bảo trì / mở lại, xoá ô. Cần Parking.Configure, ghi nhật ký
/// ZONE_CREATE / SLOT_CREATE / SLOT_STATUS_UPDATE / SLOT_DELETE; không đụng tới ô đang có xe hay ô đã có lịch sử.
/// </summary>
[Collection(DatabaseCollection.Name)]
[Trait("Category", "Integration")]
public class ParkingLayoutServiceTests : IClassFixture<SqlServerDatabaseFixture>
{
    private readonly SqlServerDatabaseFixture _db;
    private readonly GateTestData _data;

    public ParkingLayoutServiceTests(SqlServerDatabaseFixture db)
    {
        _db = db;
        _data = new GateTestData(db);
    }

    private async Task<(ServiceProvider Sp, int UserId)> LoginAsync(string roleName)
    {
        var user = await TestUsers.CreateAsync(_db.Factory, roleName);
        var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(user.Username);
        return (sp, user.UserId);
    }

    private static string UniqueZoneCode() => "ZT" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private static string UniquePrefix() => "P" + Guid.NewGuid().ToString("N")[..5].ToUpperInvariant() + "-";

    [Fact]
    public async Task Manager_creates_a_zone_and_adds_slots_with_sequential_codes_and_audit()
    {
        _db.RequireAvailable();
        var (sp, managerId) = await LoginAsync("Manager");
        using var scope = sp;
        var zones = sp.GetRequiredService<IParkingZoneService>();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var code = UniqueZoneCode();
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var created = await zones.CreateZoneAsync(new CreateZoneRequest(code.ToLowerInvariant(), "Khu thử nghiệm", vt, ZoneAudience.VisitorOnly));
        Assert.True(created.Success, created.Message);

        var zone = Assert.Single((await zones.GetZonesAsync()).Where(z => z.ZoneCode == code));
        Assert.Equal(ZoneAudience.VisitorOnly, zone.Audience);
        Assert.Equal(vt, zone.VehicleTypeId);
        Assert.Equal(0, zone.TotalCapacity);

        var prefix = UniquePrefix();
        var first = await zones.AddSlotsAsync(new AddSlotsRequest(zone.ZoneId, vt, prefix.ToLowerInvariant(), 3));
        Assert.True(first.Success, first.Result.Message);
        Assert.Equal(new[] { prefix + "01", prefix + "02", prefix + "03" }, first.SlotCodes);

        var second = await zones.AddSlotsAsync(new AddSlotsRequest(zone.ZoneId, vt, prefix, 2));
        Assert.True(second.Success, second.Result.Message);
        Assert.Equal(new[] { prefix + "04", prefix + "05" }, second.SlotCodes);

        await using var ctx = _db.CreateContext();
        var slots = await ctx.ParkingSlots.AsNoTracking().Where(s => s.ZoneId == zone.ZoneId).ToListAsync();
        Assert.Equal(5, slots.Count);
        Assert.All(slots, s =>
        {
            Assert.Equal(vt, s.VehicleTypeId);
            Assert.Equal(SlotStatus.Available, s.Status);
            Assert.Equal("Khu thử nghiệm", s.ZoneName);
        });
        Assert.Equal(5, (await ctx.ParkingZones.AsNoTracking().SingleAsync(z => z.ZoneId == zone.ZoneId)).TotalCapacity);

        var zoneRow = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.ZoneCreate));
        Assert.Equal("ParkingZone", zoneRow.EntityType);
        Assert.Equal(managerId, zoneRow.UserId);
        AuditDb.HasKeys(zoneRow, "zoneCode", "zoneName", "vehicleTypeId", "audience");

        var slotRows = await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.SlotCreate);
        Assert.Equal(2, slotRows.Count);
        AuditDb.HasKeys(slotRows[0], "zoneCode", "vehicleTypeId", "count", "firstCode", "lastCode");
        Assert.Equal(prefix + "01", AuditDb.Details(slotRows[0]).GetProperty("firstCode").GetString());
    }

    [Fact]
    public async Task Invalid_zone_and_slot_requests_are_rejected_without_audit()
    {
        _db.RequireAvailable();
        var (sp, _) = await LoginAsync("Manager");
        using var scope = sp;
        var zones = sp.GetRequiredService<IParkingZoneService>();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var otherVt = await _data.CreateIsolatedVehicleTypeAsync();
        var zone = await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var duplicate = await zones.CreateZoneAsync(new CreateZoneRequest(zone.ZoneCode, "Trùng mã", vt, ZoneAudience.Mixed));
        var noName = await zones.CreateZoneAsync(new CreateZoneRequest(UniqueZoneCode(), "  ", vt, ZoneAudience.Mixed));
        var badCode = await zones.CreateZoneAsync(new CreateZoneRequest("KHU A?", "Mã sai", vt, ZoneAudience.Mixed));
        var wrongType = await zones.AddSlotsAsync(new AddSlotsRequest(zone.ZoneId, otherVt, UniquePrefix(), 2));
        var tooMany = await zones.AddSlotsAsync(new AddSlotsRequest(zone.ZoneId, vt, UniquePrefix(), SlotCodeGenerator.MaxSlotsPerBatch + 1));
        var badPrefix = await zones.AddSlotsAsync(new AddSlotsRequest(zone.ZoneId, vt, "A1", 2));
        var noZone = await zones.AddSlotsAsync(new AddSlotsRequest(int.MaxValue, vt, UniquePrefix(), 1));

        Assert.All(new[] { duplicate, noName, badCode, wrongType.Result, tooMany.Result, badPrefix.Result },
            r => Assert.Equal(OperationError.Validation, r.Error));
        Assert.Equal(OperationError.NotFound, noZone.Result.Error);
        Assert.Empty(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.ZoneCreate));
        Assert.Empty(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.SlotCreate));
    }

    [Fact]
    public async Task Operator_without_Parking_Configure_is_denied()
    {
        _db.RequireAvailable();
        var (sp, _) = await LoginAsync("Operator");
        using var scope = sp;
        var zones = sp.GetRequiredService<IParkingZoneService>();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var zone = await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);

        Assert.True((await zones.CreateZoneAsync(new CreateZoneRequest(UniqueZoneCode(), "X", vt, ZoneAudience.Mixed))).IsPermissionDenied);
        Assert.True((await zones.AddSlotsAsync(new AddSlotsRequest(zone.ZoneId, vt, UniquePrefix(), 1))).Result.IsPermissionDenied);
        Assert.True((await zones.SetSlotMaintenanceAsync(zone.SlotIds[0], true, null)).IsPermissionDenied);
        Assert.True((await zones.DeleteSlotAsync(zone.SlotIds[0])).IsPermissionDenied);
    }

    [Fact]
    public async Task Maintenance_slot_is_not_allocated_and_can_be_reopened()
    {
        _db.RequireAvailable();
        var (sp, _) = await LoginAsync("Manager");
        using var scope = sp;
        var zones = sp.GetRequiredService<IParkingZoneService>();
        var gate = sp.GetRequiredService<IGateControlService>();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var zone = await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var off = await zones.SetSlotMaintenanceAsync(zone.SlotIds[0], true, "Sơn lại vạch");
        Assert.True(off.Success, off.Message);
        var blocked = await gate.ProcessCheckInAsync(new GateCheckInRequest { LicensePlate = ParkingFlows.UniquePlate(), VehicleTypeId = vt });
        Assert.False(blocked.Success);
        Assert.Equal(CheckInRejectReason.NoSlotAvailable, blocked.RejectReason);

        var on = await zones.SetSlotMaintenanceAsync(zone.SlotIds[0], false, null);
        Assert.True(on.Success, on.Message);
        var allowed = await gate.ProcessCheckInAsync(new GateCheckInRequest { LicensePlate = ParkingFlows.UniquePlate(), VehicleTypeId = vt });
        Assert.True(allowed.Success, allowed.Message);
        Assert.Equal(zone.SlotIds[0], allowed.Session!.SlotId);

        var rows = await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.SlotStatusUpdate);
        Assert.Equal(2, rows.Count);
        var details = AuditDb.Details(rows[0]);
        Assert.Equal("Available", details.GetProperty("before").GetString());
        Assert.Equal("Maintenance", details.GetProperty("after").GetString());
        Assert.Equal("Sơn lại vạch", details.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Occupied_slot_cannot_be_put_in_maintenance_or_deleted()
    {
        _db.RequireAvailable();
        var (sp, _) = await LoginAsync("Manager");
        using var scope = sp;
        var zones = sp.GetRequiredService<IParkingZoneService>();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var zone = await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
        var checkIn = await sp.GetRequiredService<IGateControlService>()
            .ProcessCheckInAsync(new GateCheckInRequest { LicensePlate = ParkingFlows.UniquePlate(), VehicleTypeId = vt });
        Assert.True(checkIn.Success, checkIn.Message);

        Assert.Equal(OperationError.Validation, (await zones.SetSlotMaintenanceAsync(zone.SlotIds[0], true, null)).Error);
        Assert.Equal(OperationError.Validation, (await zones.DeleteSlotAsync(zone.SlotIds[0])).Error);
    }

    [Fact]
    public async Task Slot_with_history_is_kept_and_an_unused_slot_is_deleted()
    {
        _db.RequireAvailable();
        var (sp, managerId) = await LoginAsync("Manager");
        using var scope = sp;
        var zones = sp.GetRequiredService<IParkingZoneService>();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var zone = await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 2);

        // Lượt gửi đã hoàn thành ở ô 1 (ô 1 có lịch sử)
        await ParkingFlows.OpenShiftAsync(sp, managerId);
        var plate = ParkingFlows.UniquePlate();
        var checkIn = await sp.GetRequiredService<IGateControlService>().ProcessCheckInAsync(
            new GateCheckInRequest { LicensePlate = plate, VehicleTypeId = vt, SlotId = zone.SlotIds[0] });
        Assert.True(checkIn.Success, checkIn.Message);
        var (_, checkOut) = await ParkingFlows.CheckOutCashAsync(sp, plate, managerId);
        Assert.True(checkOut.Success, checkOut.Message);
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var keep = await zones.DeleteSlotAsync(zone.SlotIds[0]);
        Assert.Equal(OperationError.Validation, keep.Error);

        var delete = await zones.DeleteSlotAsync(zone.SlotIds[1]);
        Assert.True(delete.Success, delete.Message);

        await using var ctx = _db.CreateContext();
        Assert.True(await ctx.ParkingSlots.AnyAsync(s => s.SlotId == zone.SlotIds[0]));
        Assert.False(await ctx.ParkingSlots.AnyAsync(s => s.SlotId == zone.SlotIds[1]));
        Assert.Equal(1, (await ctx.ParkingZones.AsNoTracking().SingleAsync(z => z.ZoneId == zone.ZoneId)).TotalCapacity);

        var row = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.SlotDelete));
        Assert.Equal("ParkingSlot", row.EntityType);
        Assert.Equal(zone.SlotIds[1].ToString(), row.EntityId);
        Assert.Equal(zone.SlotCodes[1], AuditDb.Details(row).GetProperty("slotCode").GetString());
    }
}
