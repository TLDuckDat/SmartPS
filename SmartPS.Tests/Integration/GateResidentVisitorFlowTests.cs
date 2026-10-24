using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartPS.Models.GateControl;
using SmartPS.Models.Parking;
using SmartPS.Services.GateControl;

namespace SmartPS.Tests.Integration;

/// <summary>
/// Spec B1/B2/B3, D1, D4, AC-R1..AC-R4, AC-R6, addendum F4: check-in on the household/vehicle model.
/// Session.CustomerType = Resident iff the owner is an active customer of an active household (never from a ticket or
/// Customer.Type); IsMonthlyPass = a valid ticket of the requested vehicle type; zone audience preference by category.
/// Every test isolates slot allocation with its own vehicle type, except the ZONE_R test.
/// </summary>
[Collection(DatabaseCollection.Name)]
[Trait("Category", "Integration")]
public class GateResidentVisitorFlowTests : IClassFixture<SqlServerDatabaseFixture>
{
    private readonly SqlServerDatabaseFixture _db;
    private readonly GateTestData _data;

    public GateResidentVisitorFlowTests(SqlServerDatabaseFixture db)
    {
        _db = db;
        _data = new GateTestData(db);
    }

    private async Task<ServiceProvider> OperatorAsync()
    {
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(op.Username);
        return sp;
    }

    private static Task<GateCheckInResult> CheckInAsync(IServiceProvider sp, string plate, int vehicleTypeId, int? slotId = null)
        => sp.GetRequiredService<IGateControlService>().ProcessCheckInAsync(new GateCheckInRequest { LicensePlate = plate, VehicleTypeId = vehicleTypeId, SlotId = slotId });

    private async Task<ParkingSession> DbSessionAsync(int sessionId)
    {
        await using var ctx = _db.CreateContext();
        return await ctx.ParkingSessions.AsNoTracking().SingleAsync(s => s.SessionId == sessionId);
    }

    /// <summary>ResidentOnly, Mixed and VisitorOnly zones with one slot each for a fresh vehicle type.</summary>
    private async Task<(int Vt, TestZone Resident, TestZone Mixed, TestZone Visitor)> ThreeZonesAsync()
    {
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var resident = await _data.CreateZoneAsync(ZoneAudience.ResidentOnly, vt, 1);
        var mixed = await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
        var visitor = await _data.CreateZoneAsync(ZoneAudience.VisitorOnly, vt, 1);
        return (vt, resident, mixed, visitor);
    }

    // ---- AC-R1 ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AC_R1_household_member_without_ticket_is_resident_not_monthly_and_gets_ZONE_R()
    {
        _db.RequireAvailable();
        using var sp = await OperatorAsync();
        var moto = await _data.MotorbikeTypeIdAsync();
        var resident = await _data.CreateResidentAsync(moto, withTicket: false, apartment: "R1-1205");
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var result = await CheckInAsync(sp, resident.NormalizedPlate, moto);

        Assert.True(result.Success, result.Message);
        Assert.Equal(VehicleCategory.Resident, result.Category);
        Assert.Equal(CheckInRejectReason.None, result.RejectReason);
        Assert.True(result.IsResident);
        Assert.Equal("R1-1205", result.ApartmentCode);
        Assert.False(result.IsMonthlyTicket);
        Assert.Null(result.TicketValidUntilUtc);
        Assert.False(result.CustomerLockedWarning);
        Assert.False(result.TicketVehicleTypeMismatchWarning);
        Assert.Equal("ZONE_R", result.AssignedZoneCode);
        Assert.Equal(ZoneAudience.ResidentOnly, result.AssignedZoneAudience);

        var session = await DbSessionAsync(result.Session!.SessionId);
        Assert.Equal(CustomerType.Resident, session.CustomerType);
        Assert.False(session.IsMonthlyPass);
        Assert.Equal(resident.CustomerId, session.CustomerId);
        Assert.Equal("ZONE_R", await _data.SlotZoneCodeAsync(session.SlotId!.Value));
        Assert.Equal(SlotStatus.Occupied, await _data.SlotStatusAsync(session.SlotId.Value));
        Assert.StartsWith("TK-", session.TicketCode, StringComparison.Ordinal);

        var row = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.ParkingCheckIn));
        var details = AuditDb.Details(row);
        Assert.Equal("Resident", details.GetProperty("category").GetString());
        Assert.False(details.GetProperty("isMonthlyPass").GetBoolean());
    }

    [Fact]
    public async Task AC_R1_resident_prefers_ResidentOnly_then_Mixed_then_VisitorOnly()
    {
        _db.RequireAvailable();
        using var sp = await OperatorAsync();
        var (vt, residentZone, mixed, visitorZone) = await ThreeZonesAsync();
        var r1 = await _data.CreateResidentAsync(vt);
        var r2 = await _data.CreateResidentAsync(vt);
        var r3 = await _data.CreateResidentAsync(vt);
        var r4 = await _data.CreateResidentAsync(vt);

        var first = await CheckInAsync(sp, r1.NormalizedPlate, vt);
        var second = await CheckInAsync(sp, r2.NormalizedPlate, vt);
        var third = await CheckInAsync(sp, r3.NormalizedPlate, vt);
        var fourth = await CheckInAsync(sp, r4.NormalizedPlate, vt);

        Assert.Equal(residentZone.SlotIds[0], first.Session!.SlotId);
        Assert.Equal(mixed.SlotIds[0], second.Session!.SlotId);
        Assert.Equal(visitorZone.SlotIds[0], third.Session!.SlotId);
        Assert.False(fourth.Success);
        Assert.Equal(CheckInRejectReason.NoSlotAvailable, fourth.RejectReason);
        Assert.Equal(VehicleCategory.Resident, fourth.Category);
        Assert.Equal(0, await _data.SessionCountForPlateAsync(r4.NormalizedPlate));
    }

    [Fact]
    public async Task D1_customer_typed_External_but_in_an_active_household_is_resident()
    {
        _db.RequireAvailable();
        using var sp = await OperatorAsync();
        var (vt, residentZone, _, _) = await ThreeZonesAsync();
        var resident = await _data.CreateResidentAsync(vt, type: CustomerType.External);

        var result = await CheckInAsync(sp, resident.NormalizedPlate, vt);

        Assert.True(result.Success, result.Message);
        Assert.Equal(VehicleCategory.Resident, result.Category);
        Assert.Equal(residentZone.SlotIds[0], result.Session!.SlotId);
        Assert.Equal(CustomerType.Resident, (await DbSessionAsync(result.Session.SessionId)).CustomerType);
    }

    // ---- D1 negatives -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task D1_customer_typed_Resident_without_household_is_visitor_and_never_gets_ResidentOnly()
    {
        _db.RequireAvailable();
        using var sp = await OperatorAsync();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var residentZone = await _data.CreateZoneAsync(ZoneAudience.ResidentOnly, vt, 1);
        var mixed = await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
        var typedResident = await _data.CreateExternalAsync(vt, withTicket: false, type: CustomerType.Resident);

        var result = await CheckInAsync(sp, typedResident.NormalizedPlate, vt);

        Assert.True(result.Success, result.Message);
        Assert.Equal(VehicleCategory.Visitor, result.Category);
        Assert.False(result.IsResident);
        Assert.Equal(mixed.SlotIds[0], result.Session!.SlotId);
        Assert.Equal(SlotStatus.Available, await _data.SlotStatusAsync(residentZone.SlotIds[0]));
        var session = await DbSessionAsync(result.Session.SessionId);
        Assert.Equal(CustomerType.External, session.CustomerType);
        Assert.False(session.IsMonthlyPass);
        Assert.Equal(typedResident.CustomerId, session.CustomerId);
    }

    [Fact]
    public async Task D1_member_of_an_inactive_household_is_visitor()
    {
        _db.RequireAvailable();
        using var sp = await OperatorAsync();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var residentZone = await _data.CreateZoneAsync(ZoneAudience.ResidentOnly, vt, 1);
        await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
        var member = await _data.CreateResidentAsync(vt, householdActive: false);

        var result = await CheckInAsync(sp, member.NormalizedPlate, vt);

        Assert.True(result.Success, result.Message);
        Assert.Equal(VehicleCategory.Visitor, result.Category);
        Assert.False(result.IsResident);
        Assert.Equal(SlotStatus.Available, await _data.SlotStatusAsync(residentZone.SlotIds[0]));
        var session = await DbSessionAsync(result.Session!.SessionId);
        Assert.Equal(CustomerType.External, session.CustomerType);
        Assert.Equal(member.CustomerId, session.CustomerId);
    }

    [Fact]
    public async Task D1_household_deactivated_after_registration_takes_effect_at_the_next_check_in()
    {
        _db.RequireAvailable();
        using var sp = await OperatorAsync();
        var (vt, residentZone, _, _) = await ThreeZonesAsync();
        var member = await _data.CreateResidentAsync(vt);
        await _data.SetHouseholdActiveAsync(member.HouseholdId!.Value, false);

        var result = await CheckInAsync(sp, member.NormalizedPlate, vt);

        Assert.True(result.Success, result.Message);
        Assert.Equal(VehicleCategory.Visitor, result.Category);
        Assert.NotEqual(residentZone.SlotIds[0], result.Session!.SlotId);
    }

    [Fact]
    public async Task Locked_customer_in_an_active_household_with_a_ticket_is_visitor_with_warning_and_no_customer()
    {
        _db.RequireAvailable();
        using var sp = await OperatorAsync();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var residentZone = await _data.CreateZoneAsync(ZoneAudience.ResidentOnly, vt, 1);
        await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
        var locked = await _data.CreateResidentAsync(vt, withTicket: true, customerActive: false);

        var result = await CheckInAsync(sp, locked.NormalizedPlate, vt);

        Assert.True(result.Success, result.Message);
        Assert.Equal(VehicleCategory.Visitor, result.Category);
        Assert.True(result.CustomerLockedWarning);
        Assert.False(result.IsResident);
        Assert.False(result.IsMonthlyTicket);
        var session = await DbSessionAsync(result.Session!.SessionId);
        Assert.Equal(CustomerType.External, session.CustomerType);
        Assert.False(session.IsMonthlyPass);
        Assert.Null(session.CustomerId);
        Assert.Equal(SlotStatus.Available, await _data.SlotStatusAsync(residentZone.SlotIds[0]));
    }

    [Fact]
    public async Task Unknown_plate_is_an_external_visitor_without_customer()
    {
        _db.RequireAvailable();
        using var sp = await OperatorAsync();
        var (vt, residentZone, _, visitorZone) = await ThreeZonesAsync();
        var plate = GateTestData.UniqueNormalizedPlate();

        var result = await CheckInAsync(sp, plate, vt);

        Assert.True(result.Success, result.Message);
        Assert.Equal(VehicleCategory.Visitor, result.Category);
        Assert.False(result.IsResident);
        Assert.False(result.IsMonthlyTicket);
        Assert.Equal(visitorZone.SlotIds[0], result.Session!.SlotId);
        Assert.Equal(SlotStatus.Available, await _data.SlotStatusAsync(residentZone.SlotIds[0]));
        var session = await DbSessionAsync(result.Session.SessionId);
        Assert.Equal(CustomerType.External, session.CustomerType);
        Assert.False(session.IsMonthlyPass);
        Assert.Null(session.CustomerId);
    }

    // ---- AC-R2 / AC-R3 ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AC_R2_non_resident_with_valid_ticket_is_external_monthly_pass_outside_ResidentOnly()
    {
        _db.RequireAvailable();
        using var sp = await OperatorAsync();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var residentZone = await _data.CreateZoneAsync(ZoneAudience.ResidentOnly, vt, 1);
        var mixed = await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
        var external = await _data.CreateExternalAsync(vt, withTicket: true);
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var result = await CheckInAsync(sp, external.NormalizedPlate, vt);

        Assert.True(result.Success, result.Message);
        Assert.Equal(VehicleCategory.MonthlyPass, result.Category);
        Assert.False(result.IsResident);
        Assert.True(result.IsMonthlyTicket);
        Assert.NotNull(result.TicketValidUntilUtc);
        Assert.True(result.TicketValidUntilUtc > DateTime.UtcNow);
        Assert.Equal(mixed.SlotIds[0], result.Session!.SlotId);
        Assert.NotEqual(ZoneAudience.ResidentOnly, result.AssignedZoneAudience);
        Assert.Equal(SlotStatus.Available, await _data.SlotStatusAsync(residentZone.SlotIds[0]));

        var session = await DbSessionAsync(result.Session.SessionId);
        Assert.Equal(CustomerType.External, session.CustomerType);
        Assert.True(session.IsMonthlyPass);
        Assert.Equal(external.CustomerId, session.CustomerId);
        Assert.Equal(external.TicketCode, session.TicketCode);

        var details = AuditDb.Details(Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.ParkingCheckIn)));
        Assert.Equal("MonthlyPass", details.GetProperty("category").GetString());
        Assert.True(details.GetProperty("isMonthlyPass").GetBoolean());
    }

    [Fact]
    public async Task AC_R2_monthly_pass_is_rejected_when_only_ResidentOnly_slots_are_free()
    {
        _db.RequireAvailable();
        using var sp = await OperatorAsync();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var residentZone = await _data.CreateZoneAsync(ZoneAudience.ResidentOnly, vt, 2);
        var external = await _data.CreateExternalAsync(vt, withTicket: true);

        var result = await CheckInAsync(sp, external.NormalizedPlate, vt);

        Assert.False(result.Success);
        Assert.Equal(CheckInRejectReason.NoSlotAvailable, result.RejectReason);
        Assert.Equal(VehicleCategory.MonthlyPass, result.Category);
        Assert.Equal(0, await _data.SessionCountForPlateAsync(external.NormalizedPlate));
        Assert.Equal(0, await _data.OccupiedSlotCountInZoneAsync(residentZone.ZoneId));
    }

    [Fact]
    public async Task AC_R3_resident_with_valid_ticket_is_resident_monthly_pass_in_ResidentOnly()
    {
        _db.RequireAvailable();
        using var sp = await OperatorAsync();
        var (vt, residentZone, _, _) = await ThreeZonesAsync();
        var resident = await _data.CreateResidentAsync(vt, withTicket: true);

        var result = await CheckInAsync(sp, resident.NormalizedPlate, vt);

        Assert.True(result.Success, result.Message);
        Assert.Equal(VehicleCategory.Resident, result.Category);
        Assert.True(result.IsResident);
        Assert.True(result.IsMonthlyTicket);
        Assert.NotNull(result.TicketValidUntilUtc);
        Assert.Equal(residentZone.SlotIds[0], result.Session!.SlotId);
        var session = await DbSessionAsync(result.Session.SessionId);
        Assert.Equal(CustomerType.Resident, session.CustomerType);
        Assert.True(session.IsMonthlyPass);
        Assert.Equal(resident.CustomerId, session.CustomerId);
        Assert.Equal(resident.TicketCode, session.TicketCode);
    }

    [Theory]
    [InlineData(MonthlyTicketStatus.Expired)]
    [InlineData(MonthlyTicketStatus.Suspended)]
    [InlineData(MonthlyTicketStatus.Cancelled)]
    public async Task Ticket_that_is_not_Active_is_not_honoured(MonthlyTicketStatus status)
    {
        _db.RequireAvailable();
        using var sp = await OperatorAsync();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
        var external = await _data.CreateExternalAsync(vt, withTicket: true);
        await _data.SetTicketStatusAsync(external.TicketId!.Value, status);

        var result = await CheckInAsync(sp, external.NormalizedPlate, vt);

        Assert.True(result.Success, result.Message);
        Assert.Equal(VehicleCategory.Visitor, result.Category);
        Assert.False(result.IsMonthlyTicket);
        Assert.False((await DbSessionAsync(result.Session!.SessionId)).IsMonthlyPass);
    }

    [Fact]
    public async Task Expired_ticket_by_date_is_not_honoured_and_resident_stays_resident()
    {
        _db.RequireAvailable();
        using var sp = await OperatorAsync();
        var (vt, residentZone, _, _) = await ThreeZonesAsync();
        var resident = await _data.CreateResidentAsync(vt, withTicket: true);
        await _data.SetTicketEndAsync(resident.TicketId!.Value, DateTime.UtcNow.AddDays(-1));

        var result = await CheckInAsync(sp, resident.NormalizedPlate, vt);

        Assert.True(result.Success, result.Message);
        Assert.Equal(VehicleCategory.Resident, result.Category);
        Assert.False(result.IsMonthlyTicket);
        Assert.Equal(residentZone.SlotIds[0], result.Session!.SlotId);
        var session = await DbSessionAsync(result.Session.SessionId);
        Assert.Equal(CustomerType.Resident, session.CustomerType);
        Assert.False(session.IsMonthlyPass);
    }

    // ---- ticket vehicle type ------------------------------------------------------------------------------------------

    [Fact]
    public async Task Ticket_of_another_vehicle_type_is_ignored_for_a_non_resident_with_warning()
    {
        _db.RequireAvailable();
        using var sp = await OperatorAsync();
        var moto = await _data.CreateIsolatedVehicleTypeAsync();
        var car = await _data.CreateIsolatedVehicleTypeAsync();
        await _data.CreateZoneAsync(ZoneAudience.Mixed, car, 1);
        var external = await _data.CreateExternalAsync(moto, withTicket: true); // vehicle + ticket are moto

        var result = await CheckInAsync(sp, external.NormalizedPlate, car);

        Assert.True(result.Success, result.Message);
        Assert.Equal(VehicleCategory.Visitor, result.Category);
        Assert.True(result.TicketVehicleTypeMismatchWarning);
        Assert.False(result.IsMonthlyTicket);
        var session = await DbSessionAsync(result.Session!.SessionId);
        Assert.False(session.IsMonthlyPass);
        Assert.Equal(CustomerType.External, session.CustomerType);
        Assert.Equal(car, session.VehicleTypeId);
    }

    [Fact]
    public async Task Ticket_of_another_vehicle_type_keeps_a_household_member_resident_but_not_free()
    {
        _db.RequireAvailable();
        using var sp = await OperatorAsync();
        var moto = await _data.CreateIsolatedVehicleTypeAsync();
        var car = await _data.CreateIsolatedVehicleTypeAsync();
        var carResident = await _data.CreateZoneAsync(ZoneAudience.ResidentOnly, car, 1);
        await _data.CreateZoneAsync(ZoneAudience.Mixed, car, 1);
        var resident = await _data.CreateResidentAsync(moto, withTicket: true);

        var result = await CheckInAsync(sp, resident.NormalizedPlate, car);

        Assert.True(result.Success, result.Message);
        Assert.Equal(VehicleCategory.Resident, result.Category);
        Assert.True(result.TicketVehicleTypeMismatchWarning);
        Assert.False(result.IsMonthlyTicket);
        Assert.Equal(carResident.SlotIds[0], result.Session!.SlotId);
        var session = await DbSessionAsync(result.Session.SessionId);
        Assert.Equal(CustomerType.Resident, session.CustomerType);
        Assert.False(session.IsMonthlyPass);
    }

    // ---- plate matching (AC-R4, F4) -----------------------------------------------------------------------------------

    [Fact]
    public async Task AC_R4_vehicle_stored_raw_matches_lower_case_gate_input()
    {
        _db.RequireAvailable();
        using var sp = await OperatorAsync();
        var (vt, residentZone, _, _) = await ThreeZonesAsync();
        var resident = await _data.CreateResidentAsync(vt); // stored as "RVx-xxx.xxx"
        Assert.Contains("-", resident.RawPlate, StringComparison.Ordinal);

        var result = await CheckInAsync(sp, resident.NormalizedPlate.ToLowerInvariant(), vt);

        Assert.True(result.Success, result.Message);
        Assert.Equal(VehicleCategory.Resident, result.Category);
        Assert.Equal(residentZone.SlotIds[0], result.Session!.SlotId);
        Assert.Equal(resident.CustomerId, (await DbSessionAsync(result.Session.SessionId)).CustomerId);
    }

    [Fact]
    public async Task Decorated_gate_input_matches_a_raw_stored_vehicle()
    {
        _db.RequireAvailable();
        using var sp = await OperatorAsync();
        var (vt, _, _, _) = await ThreeZonesAsync();
        var external = await _data.CreateExternalAsync(vt, withTicket: true);

        var result = await CheckInAsync(sp, GateTestData.Decorate(external.NormalizedPlate), vt);

        Assert.True(result.Success, result.Message);
        Assert.Equal(VehicleCategory.MonthlyPass, result.Category);
    }

    [Fact]
    public async Task Ticket_matched_by_VehicleId_with_a_different_registered_plate_is_honoured()
    {
        _db.RequireAvailable();
        using var sp = await OperatorAsync();
        var (vt, _, _, _) = await ThreeZonesAsync();
        var external = await _data.CreateExternalAsync(vt, withTicket: false);
        var ticketId = await _data.CreateValidTicketAsync(external.CustomerId, external.VehicleId, "OLD-000.01", vt);

        var result = await CheckInAsync(sp, external.NormalizedPlate, vt);

        Assert.True(result.Success, result.Message);
        Assert.Equal(VehicleCategory.MonthlyPass, result.Category);
        Assert.True((await DbSessionAsync(result.Session!.SessionId)).IsMonthlyPass);
        Assert.Equal(await _data.TicketCodeAsync(ticketId), result.Session.TicketCode);
    }

    [Fact]
    public async Task F4_two_active_variants_where_the_ticket_belongs_to_the_older_variants_owner_is_still_a_monthly_pass()
    {
        _db.RequireAvailable();
        using var sp = await OperatorAsync();
        var (vt, residentZone, _, _) = await ThreeZonesAsync();
        var norm = GateTestData.UniqueNormalizedPlate();
        var ticketHolder = await _data.CreateCustomerAsync(null);
        var latestOwner = await _data.CreateCustomerAsync(null);
        var olderVehicle = await _data.AddVehicleAsync(ticketHolder, GateTestData.Raw(norm), vt, createdAtUtc: DateTime.UtcNow.AddDays(-90));
        await _data.AddVehicleAsync(latestOwner, norm, vt, createdAtUtc: DateTime.UtcNow.AddDays(-1));
        await _data.CreateValidTicketAsync(ticketHolder, olderVehicle, GateTestData.Raw(norm), vt);

        var result = await CheckInAsync(sp, norm, vt);

        Assert.True(result.Success, result.Message);
        Assert.Equal(VehicleCategory.MonthlyPass, result.Category);
        Assert.True(result.IsMonthlyTicket);
        Assert.Equal(SlotStatus.Available, await _data.SlotStatusAsync(residentZone.SlotIds[0]));
        var session = await DbSessionAsync(result.Session!.SessionId);
        Assert.True(session.IsMonthlyPass);
        Assert.Equal(CustomerType.External, session.CustomerType);
        Assert.Equal(latestOwner, session.CustomerId);
    }

    [Fact]
    public async Task F4_residency_comes_from_the_latest_active_variants_owner()
    {
        _db.RequireAvailable();
        using var sp = await OperatorAsync();
        var (vt, residentZone, _, _) = await ThreeZonesAsync();
        var norm = GateTestData.UniqueNormalizedPlate();
        var olderResident = await _data.CreateCustomerAsync(await _data.CreateHouseholdAsync());
        var latestExternal = await _data.CreateCustomerAsync(null);
        await _data.AddVehicleAsync(olderResident, GateTestData.Raw(norm), vt, createdAtUtc: DateTime.UtcNow.AddDays(-90));
        await _data.AddVehicleAsync(latestExternal, norm.ToLowerInvariant(), vt, createdAtUtc: DateTime.UtcNow.AddDays(-1));

        var result = await CheckInAsync(sp, norm, vt);

        Assert.True(result.Success, result.Message);
        Assert.Equal(VehicleCategory.Visitor, result.Category);
        Assert.Equal(SlotStatus.Available, await _data.SlotStatusAsync(residentZone.SlotIds[0]));
        Assert.Equal(latestExternal, (await DbSessionAsync(result.Session!.SessionId)).CustomerId);
    }

    // ---- AC-R6: audience with an operator-chosen slot, duplicate plate -------------------------------------------------

    [Fact]
    public async Task AC_R6_visitor_and_monthly_pass_cannot_request_a_ResidentOnly_slot_but_a_resident_can()
    {
        _db.RequireAvailable();
        using var sp = await OperatorAsync();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var residentZone = await _data.CreateZoneAsync(ZoneAudience.ResidentOnly, vt, 1);
        await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
        var visitorPlate = GateTestData.UniqueNormalizedPlate();
        var monthly = await _data.CreateExternalAsync(vt, withTicket: true);
        var resident = await _data.CreateResidentAsync(vt);

        var visitor = await CheckInAsync(sp, visitorPlate, vt, residentZone.SlotIds[0]);
        var pass = await CheckInAsync(sp, monthly.NormalizedPlate, vt, residentZone.SlotIds[0]);
        var res = await CheckInAsync(sp, resident.NormalizedPlate, vt, residentZone.SlotIds[0]);

        Assert.False(visitor.Success);
        Assert.Equal(CheckInRejectReason.SlotAudienceNotAllowed, visitor.RejectReason);
        Assert.False(pass.Success);
        Assert.Equal(CheckInRejectReason.SlotAudienceNotAllowed, pass.RejectReason);
        Assert.Equal(0, await _data.SessionCountForPlateAsync(visitorPlate));
        Assert.Equal(0, await _data.SessionCountForPlateAsync(monthly.NormalizedPlate));
        Assert.True(res.Success, res.Message);
        Assert.Equal(residentZone.SlotIds[0], res.Session!.SlotId);
    }

    [Fact]
    public async Task AC_R6_plate_already_inside_in_another_spelling_is_rejected_without_a_second_session()
    {
        _db.RequireAvailable();
        using var sp = await OperatorAsync();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var zone = await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 2);
        var resident = await _data.CreateResidentAsync(vt);
        var first = await CheckInAsync(sp, resident.NormalizedPlate, vt);
        Assert.True(first.Success, first.Message);
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var second = await CheckInAsync(sp, resident.RawPlate.ToLowerInvariant(), vt);

        Assert.False(second.Success);
        Assert.Equal(CheckInRejectReason.AlreadyInside, second.RejectReason);
        Assert.Null(second.Session);
        Assert.Equal(1, await _data.SessionCountForPlateAsync(resident.NormalizedPlate, activeOnly: true));
        Assert.Equal(1, await _data.OccupiedSlotCountInZoneAsync(zone.ZoneId));
        Assert.Empty(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.ParkingCheckIn));
    }

    // ---- preview / lookup API ------------------------------------------------------------------------------------------

    [Fact]
    public async Task ClassifyVehicleAsync_preview_matches_check_in_classification()
    {
        _db.RequireAvailable();
        using var sp = await OperatorAsync();
        var gate = sp.GetRequiredService<IGateControlService>();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var resident = await _data.CreateResidentAsync(vt, apartment: "PV-0101");
        var monthly = await _data.CreateExternalAsync(vt, withTicket: true);
        var blockedNorm = GateTestData.UniqueNormalizedPlate();
        await _data.AddBlacklistAsync(GateTestData.Raw(blockedNorm), "preview reason");

        var r = await gate.ClassifyVehicleAsync(GateTestData.Decorate(resident.NormalizedPlate));
        var m = await gate.ClassifyVehicleAsync(monthly.RawPlate);
        var b = await gate.ClassifyVehicleAsync(blockedNorm.ToLowerInvariant());
        var v = await gate.ClassifyVehicleAsync(GateTestData.UniqueNormalizedPlate());

        Assert.Equal(VehicleCategory.Resident, r.Category);
        Assert.Equal(resident.NormalizedPlate, r.NormalizedPlate);
        Assert.Equal("PV-0101", r.Owner!.ApartmentCode);
        Assert.False(r.IsMonthlyPass);
        Assert.Equal(VehicleCategory.MonthlyPass, m.Category);
        Assert.Equal(monthly.TicketId, m.Ticket!.TicketId);
        Assert.Equal(VehicleCategory.Blacklisted, b.Category);
        Assert.Equal("preview reason", b.Blacklist!.Reason);
        Assert.Equal(blockedNorm, b.Blacklist.NormalizedPlate);
        Assert.Equal(VehicleCategory.Visitor, v.Category);
    }

    [Fact]
    public async Task FindActiveMonthlyTicketAsync_matches_raw_stored_plates_and_ignores_residents_without_ticket()
    {
        _db.RequireAvailable();
        using var sp = await OperatorAsync();
        var gate = sp.GetRequiredService<IGateControlService>();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var monthly = await _data.CreateExternalAsync(vt, withTicket: true);
        var expired = await _data.CreateExternalAsync(vt, withTicket: true);
        await _data.SetTicketEndAsync(expired.TicketId!.Value, DateTime.UtcNow.AddDays(-1));
        var resident = await _data.CreateResidentAsync(vt);

        var ticket = await gate.FindActiveMonthlyTicketAsync(GateTestData.Decorate(monthly.NormalizedPlate));

        Assert.NotNull(ticket);
        Assert.Equal(monthly.TicketId, ticket!.TicketId);
        Assert.Equal(monthly.TicketCode, ticket.TicketCode);
        Assert.NotNull(ticket.Customer);
        Assert.Equal(monthly.CustomerId, ticket.Customer!.CustomerId);
        Assert.Null(await gate.FindActiveMonthlyTicketAsync(expired.NormalizedPlate));
        Assert.Null(await gate.FindActiveMonthlyTicketAsync(resident.NormalizedPlate));
    }
}
