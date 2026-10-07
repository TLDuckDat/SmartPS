using Microsoft.Extensions.DependencyInjection;
using SmartPS.Models.GateControl;
using SmartPS.Models.Parking;
using SmartPS.Services.GateControl;

namespace SmartPS.Tests.Integration;

/// <summary>AC-6 / R13: an operator-chosen SlotId is honoured but must be free, of the right vehicle type and of an allowed audience.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class GateRequestedSlotTests : IClassFixture<PostgresDatabaseFixture>
{
    private readonly PostgresDatabaseFixture _db;
    private readonly ResidentVisitorData _data;

    public GateRequestedSlotTests(PostgresDatabaseFixture db)
    {
        _db = db;
        _data = new ResidentVisitorData(db);
    }

    private async Task<ServiceProvider> OperatorAsync()
    {
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(op.Username);
        return sp;
    }

    private static Task<GateCheckInResult> CheckInAsync(IServiceProvider sp, string plate, int vehicleTypeId, int? slotId)
        => sp.GetRequiredService<IGateControlService>().ProcessCheckInAsync(new GateCheckInRequest
        {
            LicensePlate = plate,
            VehicleTypeId = vehicleTypeId,
            SlotId = slotId
        });

    private async Task AssertRejectedAsync(GateCheckInResult result, CheckInRejectReason reason, string plate, int slotId, SlotStatus expectedSlotStatus)
    {
        Assert.False(result.Success);
        Assert.Equal(reason, result.RejectReason);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
        Assert.Equal(0, await _data.SessionCountForPlateAsync(plate));
        Assert.Equal(expectedSlotStatus, await _data.SlotStatusAsync(slotId));
    }

    [Fact]
    public async Task AC6_valid_requested_slot_is_used_instead_of_the_automatic_pick()
    {
        _db.RequireAvailable();
        using var sp = await OperatorAsync();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var mixed = await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 3);
        var plate = ResidentVisitorData.UniqueNormalizedPlate();

        var result = await CheckInAsync(sp, plate, vt, mixed.SlotIds[2]);

        Assert.True(result.Success, result.Message);
        Assert.Equal(mixed.SlotIds[2], result.Session!.SlotId);
        Assert.Equal(mixed.SlotCodes[2], result.AssignedSlotCode);
        Assert.Equal(SlotStatus.Occupied, await _data.SlotStatusAsync(mixed.SlotIds[2]));
        Assert.Equal(SlotStatus.Available, await _data.SlotStatusAsync(mixed.SlotIds[0]));
    }

    [Fact]
    public async Task AC6_resident_only_slot_requested_for_a_visitor_is_rejected()
    {
        _db.RequireAvailable();
        using var sp = await OperatorAsync();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var residentZone = await _data.CreateZoneAsync(ZoneAudience.ResidentOnly, vt, 1);
        await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
        var plate = ResidentVisitorData.UniqueNormalizedPlate();

        var result = await CheckInAsync(sp, plate, vt, residentZone.SlotIds[0]);

        await AssertRejectedAsync(result, CheckInRejectReason.SlotAudienceNotAllowed, plate, residentZone.SlotIds[0], SlotStatus.Available);
    }

    [Fact]
    public async Task Resident_only_slot_requested_for_a_monthly_non_resident_is_rejected()
    {
        _db.RequireAvailable();
        using var sp = await OperatorAsync();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var residentZone = await _data.CreateZoneAsync(ZoneAudience.ResidentOnly, vt, 1);
        var monthly = await _data.CreateSubscriberAsync(vt, isResident: false);

        var result = await CheckInAsync(sp, monthly.Plate, vt, residentZone.SlotIds[0]);

        await AssertRejectedAsync(result, CheckInRejectReason.SlotAudienceNotAllowed, monthly.Plate, residentZone.SlotIds[0], SlotStatus.Available);
    }

    [Fact]
    public async Task Resident_may_request_resident_and_visitor_only_slots()
    {
        _db.RequireAvailable();
        using var sp = await OperatorAsync();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var residentZone = await _data.CreateZoneAsync(ZoneAudience.ResidentOnly, vt, 1);
        var visitorZone = await _data.CreateZoneAsync(ZoneAudience.VisitorOnly, vt, 1);
        var r1 = await _data.CreateSubscriberAsync(vt, isResident: true);
        var r2 = await _data.CreateSubscriberAsync(vt, isResident: true);

        var first = await CheckInAsync(sp, r1.Plate, vt, residentZone.SlotIds[0]);
        var second = await CheckInAsync(sp, r2.Plate, vt, visitorZone.SlotIds[0]);

        Assert.True(first.Success, first.Message);
        Assert.Equal(residentZone.SlotIds[0], first.Session!.SlotId);
        Assert.True(second.Success, second.Message);
        Assert.Equal(visitorZone.SlotIds[0], second.Session!.SlotId);
    }

    [Fact]
    public async Task Requested_slot_of_another_vehicle_type_is_rejected()
    {
        _db.RequireAvailable();
        using var sp = await OperatorAsync();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var otherVt = await _data.CreateIsolatedVehicleTypeAsync();
        await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
        var otherZone = await _data.CreateZoneAsync(ZoneAudience.Mixed, otherVt, 1);
        var plate = ResidentVisitorData.UniqueNormalizedPlate();

        var result = await CheckInAsync(sp, plate, vt, otherZone.SlotIds[0]);

        await AssertRejectedAsync(result, CheckInRejectReason.SlotVehicleTypeMismatch, plate, otherZone.SlotIds[0], SlotStatus.Available);
    }

    [Theory]
    [InlineData(SlotStatus.Occupied)]
    [InlineData(SlotStatus.Maintenance)]
    public async Task Requested_slot_that_is_not_free_is_rejected(SlotStatus status)
    {
        _db.RequireAvailable();
        using var sp = await OperatorAsync();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var busy = await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1, status);
        await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
        var plate = ResidentVisitorData.UniqueNormalizedPlate();

        var result = await CheckInAsync(sp, plate, vt, busy.SlotIds[0]);

        await AssertRejectedAsync(result, CheckInRejectReason.SlotNotAvailable, plate, busy.SlotIds[0], status);
    }

    [Fact]
    public async Task Requested_slot_already_used_by_an_active_session_is_rejected()
    {
        _db.RequireAvailable();
        using var sp = await OperatorAsync();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var zone = await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 2);
        var first = await CheckInAsync(sp, ResidentVisitorData.UniqueNormalizedPlate(), vt, zone.SlotIds[0]);
        Assert.True(first.Success, first.Message);
        var plate = ResidentVisitorData.UniqueNormalizedPlate();

        var second = await CheckInAsync(sp, plate, vt, zone.SlotIds[0]);

        await AssertRejectedAsync(second, CheckInRejectReason.SlotNotAvailable, plate, zone.SlotIds[0], SlotStatus.Occupied);
    }

    [Fact]
    public async Task Unknown_requested_slot_is_rejected()
    {
        _db.RequireAvailable();
        using var sp = await OperatorAsync();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
        var plate = ResidentVisitorData.UniqueNormalizedPlate();

        var result = await CheckInAsync(sp, plate, vt, int.MaxValue);

        Assert.False(result.Success);
        Assert.Equal(CheckInRejectReason.SlotNotFound, result.RejectReason);
        Assert.Equal(0, await _data.SessionCountForPlateAsync(plate));
    }

    [Fact]
    public async Task SlotId_zero_means_automatic_selection()
    {
        _db.RequireAvailable();
        using var sp = await OperatorAsync();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var zone = await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);

        var result = await CheckInAsync(sp, ResidentVisitorData.UniqueNormalizedPlate(), vt, 0);

        Assert.True(result.Success, result.Message);
        Assert.Equal(zone.SlotIds[0], result.Session!.SlotId);
    }
}
