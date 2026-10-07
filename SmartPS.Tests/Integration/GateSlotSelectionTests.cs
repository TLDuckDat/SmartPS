using Microsoft.Extensions.DependencyInjection;
using SmartPS.Models.GateControl;
using SmartPS.Models.Parking;
using SmartPS.Services.GateControl;

namespace SmartPS.Tests.Integration;

/// <summary>AC-3, R12, decision 2, A5: automatic slot allocation by zone audience (each test uses its own vehicle type).</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class GateSlotSelectionTests : IClassFixture<PostgresDatabaseFixture>
{
    private readonly PostgresDatabaseFixture _db;
    private readonly ResidentVisitorData _data;

    public GateSlotSelectionTests(PostgresDatabaseFixture db)
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

    private static Task<GateCheckInResult> CheckInAsync(IServiceProvider sp, string plate, int vehicleTypeId)
        => sp.GetRequiredService<IGateControlService>().ProcessCheckInAsync(new GateCheckInRequest { LicensePlate = plate, VehicleTypeId = vehicleTypeId });

    private static async Task<int> CheckedInSlotAsync(IServiceProvider sp, string plate, int vehicleTypeId)
    {
        var result = await CheckInAsync(sp, plate, vehicleTypeId);
        Assert.True(result.Success, $"{plate}: {result.Message}");
        Assert.NotNull(result.Session!.SlotId);
        return result.Session.SlotId!.Value;
    }

    [Fact]
    public async Task AC3_visitor_is_rejected_when_only_resident_slots_are_free_and_a_resident_then_gets_it()
    {
        _db.RequireAvailable();
        using var sp = await OperatorAsync();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var residentZone = await _data.CreateZoneAsync(ZoneAudience.ResidentOnly, vt, 1);
        var mixed = await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
        var visitorZone = await _data.CreateZoneAsync(ZoneAudience.VisitorOnly, vt, 1);

        // Fill VisitorOnly and Mixed with visitors
        Assert.Equal(visitorZone.SlotIds[0], await CheckedInSlotAsync(sp, ResidentVisitorData.UniqueNormalizedPlate(), vt));
        Assert.Equal(mixed.SlotIds[0], await CheckedInSlotAsync(sp, ResidentVisitorData.UniqueNormalizedPlate(), vt));
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        // When another visitor arrives
        var plate = ResidentVisitorData.UniqueNormalizedPlate();
        var rejected = await CheckInAsync(sp, plate, vt);

        // Then no session, barrier stays closed (Success=false), resident slot untouched
        Assert.False(rejected.Success);
        Assert.Equal(CheckInRejectReason.NoSlotAvailable, rejected.RejectReason);
        Assert.Equal(VehicleCategory.Visitor, rejected.Category);
        Assert.False(rejected.IsPermissionDenied);
        Assert.Contains("Hết chỗ", rejected.Message, StringComparison.Ordinal);
        Assert.Equal(0, await _data.SessionCountForPlateAsync(plate));
        Assert.Equal(SlotStatus.Available, await _data.SlotStatusAsync(residentZone.SlotIds[0]));
        Assert.Null(await _data.SlotPlateAsync(residentZone.SlotIds[0]));
        Assert.Empty(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.ParkingCheckIn));

        // A resident still gets the ResidentOnly slot
        var resident = await _data.CreateSubscriberAsync(vt, isResident: true);
        Assert.Equal(residentZone.SlotIds[0], await CheckedInSlotAsync(sp, resident.Plate, vt));
    }

    [Fact]
    public async Task Decision2_resident_order_is_ResidentOnly_then_Mixed_then_VisitorOnly_then_full()
    {
        _db.RequireAvailable();
        using var sp = await OperatorAsync();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var visitorZone = await _data.CreateZoneAsync(ZoneAudience.VisitorOnly, vt, 1);
        var mixed = await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
        var residentZone = await _data.CreateZoneAsync(ZoneAudience.ResidentOnly, vt, 1);

        var r1 = await _data.CreateSubscriberAsync(vt, isResident: true);
        var r2 = await _data.CreateSubscriberAsync(vt, isResident: true);
        var r3 = await _data.CreateSubscriberAsync(vt, isResident: true);
        var r4 = await _data.CreateSubscriberAsync(vt, isResident: true);

        Assert.Equal(residentZone.SlotIds[0], await CheckedInSlotAsync(sp, r1.Plate, vt));
        Assert.Equal(mixed.SlotIds[0], await CheckedInSlotAsync(sp, r2.Plate, vt));
        var third = await CheckInAsync(sp, r3.Plate, vt);
        Assert.True(third.Success, third.Message);
        Assert.Equal(visitorZone.SlotIds[0], third.Session!.SlotId);
        Assert.Equal(ZoneAudience.VisitorOnly, third.AssignedZoneAudience);
        Assert.Equal(visitorZone.ZoneCode, third.AssignedZoneCode);

        var fourth = await CheckInAsync(sp, r4.Plate, vt);
        Assert.False(fourth.Success);
        Assert.Equal(CheckInRejectReason.NoSlotAvailable, fourth.RejectReason);
        Assert.Equal(VehicleCategory.Resident, fourth.Category);
        Assert.Equal(0, await _data.SessionCountForPlateAsync(r4.Plate));
    }

    [Fact]
    public async Task Visitor_order_is_VisitorOnly_then_Mixed()
    {
        _db.RequireAvailable();
        using var sp = await OperatorAsync();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var mixed = await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
        var visitorZone = await _data.CreateZoneAsync(ZoneAudience.VisitorOnly, vt, 1);

        Assert.Equal(visitorZone.SlotIds[0], await CheckedInSlotAsync(sp, ResidentVisitorData.UniqueNormalizedPlate(), vt));
        Assert.Equal(mixed.SlotIds[0], await CheckedInSlotAsync(sp, ResidentVisitorData.UniqueNormalizedPlate(), vt));
    }

    [Fact]
    public async Task Monthly_non_resident_never_gets_a_resident_slot()
    {
        _db.RequireAvailable();
        using var sp = await OperatorAsync();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var residentZone = await _data.CreateZoneAsync(ZoneAudience.ResidentOnly, vt, 1);
        var mixed = await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
        var m1 = await _data.CreateSubscriberAsync(vt, isResident: false);
        var m2 = await _data.CreateSubscriberAsync(vt, isResident: false);

        var first = await CheckInAsync(sp, m1.Plate, vt);
        Assert.True(first.Success, first.Message);
        Assert.Equal(VehicleCategory.MonthlyPass, first.Category);
        Assert.Equal(mixed.SlotIds[0], first.Session!.SlotId);

        var second = await CheckInAsync(sp, m2.Plate, vt);
        Assert.False(second.Success);
        Assert.Equal(CheckInRejectReason.NoSlotAvailable, second.RejectReason);
        Assert.Equal(SlotStatus.Available, await _data.SlotStatusAsync(residentZone.SlotIds[0]));
    }

    [Fact]
    public async Task Maintenance_slots_are_never_allocated()
    {
        _db.RequireAvailable();
        using var sp = await OperatorAsync();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var broken = await _data.CreateZoneAsync(ZoneAudience.VisitorOnly, vt, 1, SlotStatus.Maintenance);
        var mixed = await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);

        Assert.Equal(mixed.SlotIds[0], await CheckedInSlotAsync(sp, ResidentVisitorData.UniqueNormalizedPlate(), vt));
        var rejected = await CheckInAsync(sp, ResidentVisitorData.UniqueNormalizedPlate(), vt);
        Assert.Equal(CheckInRejectReason.NoSlotAvailable, rejected.RejectReason);
        Assert.Equal(SlotStatus.Maintenance, await _data.SlotStatusAsync(broken.SlotIds[0]));
    }

    [Fact]
    public async Task Within_an_audience_slots_are_taken_in_slot_code_order()
    {
        _db.RequireAvailable();
        using var sp = await OperatorAsync();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var mixed = await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 3);

        Assert.Equal(mixed.SlotIds[0], await CheckedInSlotAsync(sp, ResidentVisitorData.UniqueNormalizedPlate(), vt));
        Assert.Equal(mixed.SlotIds[1], await CheckedInSlotAsync(sp, ResidentVisitorData.UniqueNormalizedPlate(), vt));
        Assert.Equal(mixed.SlotIds[2], await CheckedInSlotAsync(sp, ResidentVisitorData.UniqueNormalizedPlate(), vt));
    }

    [Fact]
    public async Task A5_vehicle_type_without_any_slot_checks_in_without_slot()
    {
        _db.RequireAvailable();
        using var sp = await OperatorAsync();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();

        var result = await CheckInAsync(sp, ResidentVisitorData.UniqueNormalizedPlate(), vt);

        Assert.True(result.Success, result.Message);
        Assert.Null(result.Session!.SlotId);
        Assert.Equal(CheckInRejectReason.None, result.RejectReason);
    }

    [Fact]
    public async Task Assigned_slot_is_marked_occupied_with_the_plate()
    {
        _db.RequireAvailable();
        using var sp = await OperatorAsync();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var mixed = await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
        var plate = ResidentVisitorData.UniqueNormalizedPlate();

        var result = await CheckInAsync(sp, ResidentVisitorData.Decorate(plate), vt);

        Assert.True(result.Success, result.Message);
        Assert.Equal(mixed.SlotIds[0], result.Session!.SlotId);
        Assert.Equal(mixed.SlotCodes[0], result.AssignedSlotCode);
        Assert.Equal(mixed.ZoneCode, result.AssignedZoneCode);
        Assert.Equal(ZoneAudience.Mixed, result.AssignedZoneAudience);
        Assert.Equal(SlotStatus.Occupied, await _data.SlotStatusAsync(mixed.SlotIds[0]));
        Assert.Equal(plate, LicensePlateNormalizer.Normalize(await _data.SlotPlateAsync(mixed.SlotIds[0])));
        Assert.Equal(1, await _data.SessionCountForPlateAsync(plate, activeOnly: true));
    }

    [Fact]
    public async Task SuggestAvailableSlotAsync_preview_follows_the_same_audience_rules_without_locking()
    {
        _db.RequireAvailable();
        using var sp = await OperatorAsync();
        var gate = sp.GetRequiredService<IGateControlService>();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var residentZone = await _data.CreateZoneAsync(ZoneAudience.ResidentOnly, vt, 1);
        var mixed = await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
        var visitorZone = await _data.CreateZoneAsync(ZoneAudience.VisitorOnly, vt, 1);

        Assert.Equal(residentZone.SlotIds[0], (await gate.SuggestAvailableSlotAsync(vt, VehicleCategory.Resident))!.SlotId);
        Assert.Equal(visitorZone.SlotIds[0], (await gate.SuggestAvailableSlotAsync(vt, VehicleCategory.Visitor))!.SlotId);
        Assert.Equal(visitorZone.SlotIds[0], (await gate.SuggestAvailableSlotAsync(vt))!.SlotId); // legacy overload = Visitor
        Assert.Equal(visitorZone.SlotIds[0], (await gate.SuggestAvailableSlotAsync(vt, VehicleCategory.MonthlyPass))!.SlotId);
        Assert.Null(await gate.SuggestAvailableSlotAsync(vt, VehicleCategory.Blacklisted));

        // Preview leaves every slot free
        Assert.Equal(SlotStatus.Available, await _data.SlotStatusAsync(residentZone.SlotIds[0]));
        Assert.Equal(SlotStatus.Available, await _data.SlotStatusAsync(mixed.SlotIds[0]));
        Assert.Equal(SlotStatus.Available, await _data.SlotStatusAsync(visitorZone.SlotIds[0]));
    }
}
