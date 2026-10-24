using Microsoft.Extensions.DependencyInjection;
using SmartPS.Models.GateControl;
using SmartPS.Models.Parking;
using SmartPS.Services.Common;
using SmartPS.Services.GateControl;
using SmartPS.Services.ParkingZones;

namespace SmartPS.Tests.Integration;

/// <summary>AC-12 / R21 / E4: zone audience is changed with Parking.Configure, audited ZONE_UPDATE, and does not touch parked vehicles.</summary>
[Collection(DatabaseCollection.Name)]
[Trait("Category", "Integration")]
public class ParkingZoneServiceTests : IClassFixture<SqlServerDatabaseFixture>
{
    private readonly SqlServerDatabaseFixture _db;
    private readonly GateTestData _data;

    public ParkingZoneServiceTests(SqlServerDatabaseFixture db)
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

    private Task<long> AudienceAsync(int zoneId)
        => _data.CountAsync("SELECT \"Audience\" FROM \"ParkingZones\" WHERE \"ZoneId\" = @z", ("z", zoneId));

    [Fact]
    public async Task GetZonesAsync_lists_seed_zones_with_audience()
    {
        _db.RequireAvailable();
        var (sp, _) = await LoginAsync("Operator");
        using var spScope = sp;

        var zones = await sp.GetRequiredService<IParkingZoneService>().GetZonesAsync();

        Assert.Contains(zones, z => z.ZoneCode == "ZONE_A" && z.Audience == ZoneAudience.Mixed);
        Assert.Contains(zones, z => z.ZoneCode == "ZONE_R" && z.Audience == ZoneAudience.ResidentOnly && z.ZoneName == "Khu Cư dân (B2)");
    }

    [Fact]
    public async Task AC12_manager_changes_audience_with_before_after_audit()
    {
        _db.RequireAvailable();
        var (sp, managerId) = await LoginAsync("Manager");
        using var spScope = sp;
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var zone = await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var result = await sp.GetRequiredService<IParkingZoneService>().UpdateAudienceAsync(zone.ZoneId, ZoneAudience.VisitorOnly);

        Assert.True(result.Success, result.Message);
        Assert.Equal((long)ZoneAudience.VisitorOnly, await AudienceAsync(zone.ZoneId));
        var row = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.ZoneUpdate));
        Assert.Equal(AuditOutcome.Success, row.Outcome);
        Assert.Equal("ParkingZone", row.EntityType);
        Assert.Equal(zone.ZoneId.ToString(), row.EntityId);
        Assert.Equal(managerId, row.UserId);
        AuditDb.HasKeys(row, "zoneCode", "before", "after");
        var details = AuditDb.Details(row);
        Assert.Equal(zone.ZoneCode, details.GetProperty("zoneCode").GetString());
        Assert.Equal("Mixed", details.GetProperty("before").GetProperty("audience").GetString());
        Assert.Equal("VisitorOnly", details.GetProperty("after").GetProperty("audience").GetString());
    }

    [Fact]
    public async Task Saving_the_same_audience_is_ok_without_audit()
    {
        _db.RequireAvailable();
        var (sp, _) = await LoginAsync("Manager");
        using var spScope = sp;
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var zone = await _data.CreateZoneAsync(ZoneAudience.ResidentOnly, vt, 1);
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var result = await sp.GetRequiredService<IParkingZoneService>().UpdateAudienceAsync(zone.ZoneId, ZoneAudience.ResidentOnly);

        Assert.True(result.Success, result.Message);
        Assert.Equal(idBefore, await AuditDb.MaxIdAsync(_db.Factory));
    }

    [Fact]
    public async Task Unknown_zone_is_not_found()
    {
        _db.RequireAvailable();
        var (sp, _) = await LoginAsync("Manager");
        using var spScope = sp;

        var result = await sp.GetRequiredService<IParkingZoneService>().UpdateAudienceAsync(int.MaxValue, ZoneAudience.Mixed);

        Assert.False(result.Success);
        Assert.Equal(OperationError.NotFound, result.Error);
    }

    /// <summary>Fix round 1, G12 (review F2), moved here from the removed service regression tests.</summary>
    [Fact]
    public async Task G12_undefined_audience_value_is_a_validation_error()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var zone = await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
        var (sp, _) = await LoginAsync("Manager");
        using var scope = sp;
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var result = await sp.GetRequiredService<IParkingZoneService>().UpdateAudienceAsync(zone.ZoneId, (ZoneAudience)99);

        Assert.False(result.Success);
        Assert.Equal(OperationError.Validation, result.Error);
        Assert.Equal(0, await AudienceAsync(zone.ZoneId));
        Assert.Equal(idBefore, await AuditDb.MaxIdAsync(_db.Factory));
    }

    [Fact]
    public async Task Operator_is_denied_with_Parking_Configure()
    {
        _db.RequireAvailable();
        var (sp, opId) = await LoginAsync("Operator");
        using var spScope = sp;
        var zoneA = await _data.ZoneIdAsync("ZONE_A");
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var result = await sp.GetRequiredService<IParkingZoneService>().UpdateAudienceAsync(zoneA, ZoneAudience.ResidentOnly);

        Assert.False(result.Success);
        Assert.True(result.IsPermissionDenied);
        Assert.Equal((long)ZoneAudience.Mixed, await AudienceAsync(zoneA));
        var row = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore));
        Assert.Equal(AuditActions.AccessDenied, row.Action);
        Assert.Equal(opId, row.UserId);
        Assert.Equal(new[] { Permissions.ParkingConfigure }, AuditDb.StringArray(AuditDb.Details(row), "requiredPermissions"));
    }

    [Fact]
    public async Task E4_changing_audience_keeps_parked_vehicles_but_new_visitors_are_refused()
    {
        _db.RequireAvailable();
        var (managerSp, _) = await LoginAsync("Manager");
        using var spScope = managerSp;
        var (opSp, _) = await LoginAsync("Operator");
        using var opScope = opSp;
        var gate = opSp.GetRequiredService<IGateControlService>();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var zone = await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 2);
        var parked = await gate.ProcessCheckInAsync(new GateCheckInRequest { LicensePlate = GateTestData.UniqueNormalizedPlate(), VehicleTypeId = vt });
        Assert.True(parked.Success, parked.Message);

        var update = await managerSp.GetRequiredService<IParkingZoneService>().UpdateAudienceAsync(zone.ZoneId, ZoneAudience.ResidentOnly);
        Assert.True(update.Success, update.Message);

        // The parked visitor is untouched
        Assert.Equal(1, await _data.CountAsync("SELECT count(*) FROM \"ParkingSessions\" WHERE \"SessionId\" = @id AND \"Status\" = 0 AND \"SlotId\" = @s",
            ("id", parked.Session!.SessionId), ("s", zone.SlotIds[0])));
        Assert.Equal(SlotStatus.Occupied, await _data.SlotStatusAsync(zone.SlotIds[0]));

        // A new visitor can no longer use the zone; a resident can
        var visitor = await gate.ProcessCheckInAsync(new GateCheckInRequest { LicensePlate = GateTestData.UniqueNormalizedPlate(), VehicleTypeId = vt });
        Assert.False(visitor.Success);
        Assert.Equal(CheckInRejectReason.NoSlotAvailable, visitor.RejectReason);
        var resident = await _data.CreateResidentAsync(vt);
        var residentCheckIn = await gate.ProcessCheckInAsync(new GateCheckInRequest { LicensePlate = resident.NormalizedPlate, VehicleTypeId = vt });
        Assert.True(residentCheckIn.Success, residentCheckIn.Message);
        Assert.Equal(zone.SlotIds[1], residentCheckIn.Session!.SlotId);
    }
}
