using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartPS.Models.GateControl;
using SmartPS.Models.Parking;
using SmartPS.Services.GateControl;

namespace SmartPS.Tests.Integration;

/// <summary>AC-1, AC-2, R10, R14, E1, E3, M2: classification at check-in and what the session records.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class GateCheckInClassificationTests : IClassFixture<PostgresDatabaseFixture>
{
    private readonly PostgresDatabaseFixture _db;
    private readonly ResidentVisitorData _data;

    public GateCheckInClassificationTests(PostgresDatabaseFixture db)
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

    private async Task<ParkingSession> DbSessionAsync(int sessionId)
    {
        await using var ctx = _db.CreateContext();
        return await ctx.ParkingSessions.AsNoTracking().SingleAsync(s => s.SessionId == sessionId);
    }

    [Fact]
    public async Task AC1_resident_with_valid_ticket_gets_resident_session_in_ZONE_R()
    {
        _db.RequireAvailable();
        using var sp = await OperatorAsync();
        var car = await _data.CarTypeIdAsync();
        var customerId = await _data.CustomerIdByPhoneAsync("0988123456");
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var result = await CheckInAsync(sp, "51F-123.45", car);

        Assert.True(result.Success, result.Message);
        Assert.Equal(VehicleCategory.Resident, result.Category);
        Assert.Equal(CheckInRejectReason.None, result.RejectReason);
        Assert.True(result.IsResident);
        Assert.Equal("A-1205", result.ApartmentCode);
        Assert.NotNull(result.TicketValidUntilUtc);
        Assert.True(result.TicketValidUntilUtc > DateTime.UtcNow);
        Assert.True(result.IsMonthlyTicket);
        Assert.Equal("ZONE_R", result.AssignedZoneCode);
        Assert.Equal(ZoneAudience.ResidentOnly, result.AssignedZoneAudience);
        Assert.False(result.CustomerLockedWarning);

        var session = await DbSessionAsync(result.Session!.SessionId);
        Assert.Equal(CustomerType.Resident, session.CustomerType);
        Assert.True(session.IsMonthlyPass);
        Assert.Equal(customerId, session.CustomerId);
        Assert.NotNull(session.SlotId);
        Assert.Equal("ZONE_R", await _data.SlotZoneCodeAsync(session.SlotId!.Value));
        Assert.Equal(ZoneAudience.ResidentOnly, await _data.SlotAudienceAsync(session.SlotId.Value));
        Assert.Equal(SlotStatus.Occupied, await _data.SlotStatusAsync(session.SlotId.Value));

        var row = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.ParkingCheckIn));
        AuditDb.HasKeys(row, "licensePlate", "ticketCode", "vehicleTypeId", "slotCode", "isMonthlyPass", "category", "customerId", "zoneCode");
        var details = AuditDb.Details(row);
        Assert.Equal("Resident", details.GetProperty("category").GetString());
        Assert.Equal(customerId, details.GetProperty("customerId").GetInt32());
        Assert.Equal("ZONE_R", details.GetProperty("zoneCode").GetString());
        Assert.True(details.GetProperty("isMonthlyPass").GetBoolean());
    }

    [Fact]
    public async Task AC2_unknown_plate_is_a_regular_visitor_outside_resident_zones()
    {
        _db.RequireAvailable();
        using var sp = await OperatorAsync();
        var moto = await _data.MotorbikeTypeIdAsync();
        var plate = ResidentVisitorData.UniqueNormalizedPlate();
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var result = await CheckInAsync(sp, plate, moto);

        Assert.True(result.Success, result.Message);
        Assert.Equal(VehicleCategory.Visitor, result.Category);
        Assert.False(result.IsResident);
        Assert.False(result.IsMonthlyTicket);
        Assert.NotEqual(ZoneAudience.ResidentOnly, result.AssignedZoneAudience);

        var session = await DbSessionAsync(result.Session!.SessionId);
        Assert.Equal(CustomerType.Regular, session.CustomerType);
        Assert.False(session.IsMonthlyPass);
        Assert.Null(session.CustomerId);
        Assert.NotNull(session.SlotId);
        Assert.NotEqual(ZoneAudience.ResidentOnly, await _data.SlotAudienceAsync(session.SlotId!.Value));

        var row = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.ParkingCheckIn));
        Assert.Equal("Visitor", AuditDb.Details(row).GetProperty("category").GetString());
    }

    [Fact]
    public async Task Monthly_non_resident_is_free_pass_but_never_in_resident_zone()
    {
        _db.RequireAvailable();
        using var sp = await OperatorAsync();
        var moto = await _data.MotorbikeTypeIdAsync();
        var customerId = await _data.CustomerIdByPhoneAsync("0911222333");

        var result = await CheckInAsync(sp, "59X3-123.45", moto);

        Assert.True(result.Success, result.Message);
        Assert.Equal(VehicleCategory.MonthlyPass, result.Category);
        Assert.False(result.IsResident);
        Assert.True(result.IsMonthlyTicket);
        var session = await DbSessionAsync(result.Session!.SessionId);
        Assert.True(session.IsMonthlyPass);
        Assert.Equal(CustomerType.Regular, session.CustomerType);
        Assert.Equal(customerId, session.CustomerId);
        Assert.NotEqual(ZoneAudience.ResidentOnly, await _data.SlotAudienceAsync(session.SlotId!.Value));
    }

    [Fact]
    public async Task Expired_seed_ticket_is_treated_as_visitor()
    {
        _db.RequireAvailable();
        using var sp = await OperatorAsync();
        var car = await _data.CarTypeIdAsync();

        var result = await CheckInAsync(sp, "30F-999.99", car); // MT-SEED-003 expired 5 days ago

        Assert.True(result.Success, result.Message);
        Assert.Equal(VehicleCategory.Visitor, result.Category);
        Assert.False(result.Session!.IsMonthlyPass);
    }

    [Fact]
    public async Task E1_decorated_lower_case_plate_matches_the_resident()
    {
        _db.RequireAvailable();
        using var sp = await OperatorAsync();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        await _data.CreateZoneAsync(ZoneAudience.ResidentOnly, vt, 1);
        await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
        var resident = await _data.CreateSubscriberAsync(vt, isResident: true, apartment: "D-0909");

        var result = await CheckInAsync(sp, ResidentVisitorData.Decorate(resident.Plate), vt);

        Assert.True(result.Success, result.Message);
        Assert.Equal(VehicleCategory.Resident, result.Category);
        Assert.Equal("D-0909", result.ApartmentCode);
        Assert.Equal(ZoneAudience.ResidentOnly, result.AssignedZoneAudience);
        var session = await DbSessionAsync(result.Session!.SessionId);
        Assert.Equal(resident.CustomerId, session.CustomerId);
        Assert.Equal(CustomerType.Resident, session.CustomerType);
    }

    [Fact]
    public async Task E3_locked_customer_with_valid_ticket_is_visitor_with_warning()
    {
        _db.RequireAvailable();
        using var sp = await OperatorAsync();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var residentZone = await _data.CreateZoneAsync(ZoneAudience.ResidentOnly, vt, 1);
        await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
        var locked = await _data.CreateSubscriberAsync(vt, isResident: true, customerActive: false);

        var result = await CheckInAsync(sp, locked.Plate, vt);

        Assert.True(result.Success, result.Message);
        Assert.Equal(VehicleCategory.Visitor, result.Category);
        Assert.True(result.CustomerLockedWarning);
        Assert.False(result.IsResident);
        Assert.False(result.IsMonthlyTicket);
        var session = await DbSessionAsync(result.Session!.SessionId);
        Assert.Equal(CustomerType.Regular, session.CustomerType);
        Assert.False(session.IsMonthlyPass);
        Assert.Null(session.CustomerId);
        Assert.NotEqual(residentZone.SlotIds[0], session.SlotId);
        Assert.Equal(SlotStatus.Available, await _data.SlotStatusAsync(residentZone.SlotIds[0]));
    }

    [Fact]
    public async Task M2_ticket_whose_plate_moved_to_another_customer_is_visitor()
    {
        _db.RequireAvailable();
        using var sp = await OperatorAsync();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        await _data.CreateZoneAsync(ZoneAudience.ResidentOnly, vt, 1);
        await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 2);
        var a = await _data.CreateSubscriberAsync(vt, isResident: true);
        await _data.DeactivateVehicleAsync(a.CustomerVehicleId);
        var b = await _data.CreateCustomerAsync(isResident: false);
        await _data.AddVehicleAsync(b, a.Plate, vt);

        var result = await CheckInAsync(sp, a.Plate, vt);

        Assert.True(result.Success, result.Message);
        Assert.Equal(VehicleCategory.Visitor, result.Category);
        Assert.False(result.IsMonthlyTicket);
        var session = await DbSessionAsync(result.Session!.SessionId);
        Assert.False(session.IsMonthlyPass);
        Assert.Null(session.CustomerId);
        Assert.NotEqual(ZoneAudience.ResidentOnly, await _data.SlotAudienceAsync(session.SlotId!.Value));
    }

    [Fact]
    public async Task M2_ticket_whose_vehicle_was_removed_is_visitor()
    {
        _db.RequireAvailable();
        using var sp = await OperatorAsync();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
        var a = await _data.CreateSubscriberAsync(vt, isResident: false);
        await _data.DeactivateVehicleAsync(a.CustomerVehicleId);

        var result = await CheckInAsync(sp, a.Plate, vt);

        Assert.True(result.Success, result.Message);
        Assert.Equal(VehicleCategory.Visitor, result.Category);
    }

    [Fact]
    public async Task ClassifyVehicleAsync_preview_matches_check_in_classification()
    {
        _db.RequireAvailable();
        using var sp = await OperatorAsync();
        var gate = sp.GetRequiredService<IGateControlService>();

        var resident = await gate.ClassifyVehicleAsync("51f 123 45");
        var monthly = await gate.ClassifyVehicleAsync("59X312345");
        var blacklisted = await gate.ClassifyVehicleAsync("29A-999.99");
        var visitor = await gate.ClassifyVehicleAsync(ResidentVisitorData.UniqueNormalizedPlate());

        Assert.Equal(VehicleCategory.Resident, resident.Category);
        Assert.Equal("51F12345", resident.NormalizedPlate);
        Assert.Equal("A-1205", resident.Ticket!.ApartmentCode);
        Assert.Equal(VehicleCategory.MonthlyPass, monthly.Category);
        Assert.Equal(VehicleCategory.Blacklisted, blacklisted.Category);
        Assert.NotNull(blacklisted.Blacklist);
        Assert.Equal(VehicleCategory.Visitor, visitor.Category);
    }

    [Fact]
    public async Task FindActiveMonthlyTicketAsync_matches_normalized_plates_in_the_database()
    {
        _db.RequireAvailable();
        using var sp = await OperatorAsync();
        var gate = sp.GetRequiredService<IGateControlService>();

        var ticket = await gate.FindActiveMonthlyTicketAsync("51f-123.45");
        var expired = await gate.FindActiveMonthlyTicketAsync("30F99999");

        Assert.NotNull(ticket);
        Assert.Equal("MT-SEED-001", ticket!.TicketCode);
        Assert.Null(expired);
    }
}
