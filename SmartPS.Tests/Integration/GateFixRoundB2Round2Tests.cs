using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartPS.Models.GateControl;
using SmartPS.Models.Parking;
using SmartPS.Services.GateControl;

namespace SmartPS.Tests.Integration;

/// <summary>
/// Branch 2 fix round 2.
/// <list type="bullet">
/// <item>H5 (user rule "resident by household, never by ticket"): (a) a ticket bound to an INACTIVE vehicle is not
/// honoured — the plate pays (Visitor, or Resident by household without pass); (b) a ticket-only match (no active Vehicle
/// row with the plate) is MonthlyPass/External and free, never Resident and never a ResidentOnly slot, even when the holder
/// is in an active household. The transferred-plate case of addendum F4 (C1b) stays honoured (see
/// GateResidentVisitorFlowTests.F4_*).</item>
/// <item>H7: a non-connection error inside the check-in transaction (in-transaction blacklist / already-inside re-check,
/// FK violation for an unknown vehicle type) returns Success=false with the dedicated reject reason
/// <c>CheckInRejectReason.PersistFailed</c> (appended after ClassificationFailed) and a neutral message; nothing is stored,
/// audited, kept in memory or written to the JSONL log.</item>
/// </list>
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class GateFixRoundB2Round2Tests : IClassFixture<PostgresDatabaseFixture>
{
    private static readonly string JsonlPath = Path.Combine(AppContext.BaseDirectory, "Storage", "gate_audit_log.jsonl");
    private static readonly string OfflinePath = Path.Combine(AppContext.BaseDirectory, "Storage", "offline_active_sessions.json");

    private readonly PostgresDatabaseFixture _db;
    private readonly GateTestData _data;

    public GateFixRoundB2Round2Tests(PostgresDatabaseFixture db)
    {
        _db = db;
        _data = new GateTestData(db);
    }

    /// <summary>The new reject reason, resolved by name so the suite compiles before it exists.</summary>
    private static CheckInRejectReason PersistFailed => Enum.Parse<CheckInRejectReason>("PersistFailed");

    private async Task<(ServiceProvider Sp, int UserId)> OperatorAsync(Action<IServiceCollection>? overrides = null, bool openShift = false)
    {
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        var sp = IntegrationServices.Create(_db, overrides);
        await sp.LoginAsync(op.Username);
        if (openShift)
        {
            await ParkingFlows.OpenShiftAsync(sp, op.UserId);
        }

        return (sp, op.UserId);
    }

    private static IGateControlService Gate(IServiceProvider sp) => sp.GetRequiredService<IGateControlService>();

    private static Task<GateCheckInResult> CheckInAsync(IServiceProvider sp, string plate, int vehicleTypeId)
        => Gate(sp).ProcessCheckInAsync(new GateCheckInRequest { LicensePlate = plate, VehicleTypeId = vehicleTypeId });

    private async Task<ParkingSession> DbSessionAsync(int sessionId)
    {
        await using var ctx = _db.CreateContext();
        return await ctx.ParkingSessions.AsNoTracking().SingleAsync(s => s.SessionId == sessionId);
    }

    /// <summary>Back-dates the check-in by 2 hours, then completes a cash checkout sending a client fee of 0.</summary>
    private async Task<ParkingSession> CheckOutAfterTwoHoursAsync(IServiceProvider sp, int userId, int sessionId)
    {
        await _data.SetSessionCheckInAsync(sessionId, DateTime.UtcNow.AddHours(-2));
        var done = await Gate(sp).CompleteCheckOutAsync(new GateCheckOutRequest
        {
            SessionId = sessionId,
            ActorUserId = userId,
            PaymentMethod = PaymentMethod.Cash,
            TotalFee = 0m
        });
        Assert.True(done.Success, done.Message);
        return await DbSessionAsync(sessionId);
    }

    // ---- H5a ------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task H5a_ticket_of_an_inactive_vehicle_is_not_honoured_and_the_visitor_pays()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync(block4hPrice: 7_000m);
        await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 2);
        var norm = GateTestData.UniqueNormalizedPlate();
        var holder = await _data.CreateCustomerAsync(null);
        var removedVehicle = await _data.AddVehicleAsync(holder, GateTestData.Raw(norm), vt, isActive: false);
        await _data.CreateValidTicketAsync(holder, removedVehicle, GateTestData.Raw(norm), vt);
        var (sp, userId) = await OperatorAsync(openShift: true);
        using var scope = sp;

        var result = await CheckInAsync(sp, norm, vt);

        Assert.True(result.Success, result.Message);
        Assert.Equal(VehicleCategory.Visitor, result.Category);
        Assert.False(result.IsMonthlyTicket);
        var session = await DbSessionAsync(result.Session!.SessionId);
        Assert.False(session.IsMonthlyPass);
        Assert.Equal(CustomerType.External, session.CustomerType);

        var paid = await CheckOutAfterTwoHoursAsync(sp, userId, session.SessionId);
        Assert.Equal(7_000m, paid.TotalFee);
        Assert.Equal(PaymentMethod.Cash, paid.PaymentMethod);
    }

    [Fact]
    public async Task H5a_household_member_whose_ticket_is_on_the_removed_vehicle_is_resident_without_pass_and_pays()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync(block4hPrice: 7_000m);
        var residentZone = await _data.CreateZoneAsync(ZoneAudience.ResidentOnly, vt, 1);
        await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
        var norm = GateTestData.UniqueNormalizedPlate();
        var member = await _data.CreateCustomerAsync(await _data.CreateHouseholdAsync());
        var removedVehicle = await _data.AddVehicleAsync(member, GateTestData.Raw(norm), vt, isActive: false, createdAtUtc: DateTime.UtcNow.AddDays(-90));
        await _data.AddVehicleAsync(member, norm, vt, createdAtUtc: DateTime.UtcNow.AddDays(-1));
        await _data.CreateValidTicketAsync(member, removedVehicle, GateTestData.Raw(norm), vt);
        var (sp, userId) = await OperatorAsync(openShift: true);
        using var scope = sp;

        var result = await CheckInAsync(sp, norm, vt);

        Assert.True(result.Success, result.Message);
        Assert.Equal(VehicleCategory.Resident, result.Category);
        Assert.False(result.IsMonthlyTicket);
        Assert.Equal(residentZone.SlotIds[0], result.Session!.SlotId);
        var session = await DbSessionAsync(result.Session.SessionId);
        Assert.Equal(CustomerType.Resident, session.CustomerType);
        Assert.False(session.IsMonthlyPass);

        var paid = await CheckOutAfterTwoHoursAsync(sp, userId, session.SessionId);
        Assert.Equal(7_000m, paid.TotalFee);
    }

    // ---- H5b ------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task H5b_ticket_only_match_of_a_household_member_is_an_external_monthly_pass_outside_ResidentOnly_and_free()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync(block4hPrice: 7_000m);
        var residentZone = await _data.CreateZoneAsync(ZoneAudience.ResidentOnly, vt, 1);
        var mixed = await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
        var norm = GateTestData.UniqueNormalizedPlate();
        var holder = await _data.CreateCustomerAsync(await _data.CreateHouseholdAsync("H5B-0202"), CustomerType.Resident);
        // The holder's active vehicle carries ANOTHER plate; no active Vehicle row has the gate plate.
        var holderVehicle = await _data.AddVehicleAsync(holder, GateTestData.Raw(GateTestData.UniqueNormalizedPlate()), vt);
        var ticketId = await _data.CreateValidTicketAsync(holder, holderVehicle, GateTestData.Raw(norm), vt);
        var (sp, userId) = await OperatorAsync(openShift: true);
        using var scope = sp;

        var result = await CheckInAsync(sp, norm, vt);

        Assert.True(result.Success, result.Message);
        Assert.Equal(VehicleCategory.MonthlyPass, result.Category);
        Assert.False(result.IsResident);
        Assert.True(result.IsMonthlyTicket);
        Assert.Null(result.ApartmentCode);
        Assert.Equal(mixed.SlotIds[0], result.Session!.SlotId);
        Assert.NotEqual(ZoneAudience.ResidentOnly, result.AssignedZoneAudience);
        Assert.Equal(SlotStatus.Available, await _data.SlotStatusAsync(residentZone.SlotIds[0]));
        var session = await DbSessionAsync(result.Session.SessionId);
        Assert.Equal(CustomerType.External, session.CustomerType);
        Assert.True(session.IsMonthlyPass);
        Assert.Equal(holder, session.CustomerId);
        Assert.Equal(await _data.TicketCodeAsync(ticketId), session.TicketCode);

        var paid = await CheckOutAfterTwoHoursAsync(sp, userId, session.SessionId);
        Assert.Equal(0m, paid.TotalFee);
        Assert.Equal(PaymentMethod.Free, paid.PaymentMethod);
    }

    [Fact]
    public async Task H5b_ticket_only_match_is_rejected_when_only_ResidentOnly_slots_are_free()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var residentZone = await _data.CreateZoneAsync(ZoneAudience.ResidentOnly, vt, 2);
        var norm = GateTestData.UniqueNormalizedPlate();
        var holder = await _data.CreateCustomerAsync(await _data.CreateHouseholdAsync());
        var holderVehicle = await _data.AddVehicleAsync(holder, GateTestData.Raw(GateTestData.UniqueNormalizedPlate()), vt);
        await _data.CreateValidTicketAsync(holder, holderVehicle, GateTestData.Raw(norm), vt);
        var (sp, _) = await OperatorAsync();
        using var scope = sp;

        var result = await CheckInAsync(sp, norm, vt);

        Assert.False(result.Success);
        Assert.Equal(CheckInRejectReason.NoSlotAvailable, result.RejectReason);
        Assert.Equal(VehicleCategory.MonthlyPass, result.Category);
        Assert.Equal(0, await _data.OccupiedSlotCountInZoneAsync(residentZone.ZoneId));
        Assert.Equal(0, await _data.SessionCountForPlateAsync(norm));
    }

    [Fact]
    public async Task H5b_preview_of_a_ticket_only_household_member_is_monthly_pass_without_apartment()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var norm = GateTestData.UniqueNormalizedPlate();
        var holder = await _data.CreateCustomerAsync(await _data.CreateHouseholdAsync("H5B-0303"));
        var holderVehicle = await _data.AddVehicleAsync(holder, GateTestData.Raw(GateTestData.UniqueNormalizedPlate()), vt);
        await _data.CreateValidTicketAsync(holder, holderVehicle, GateTestData.Raw(norm), vt);
        var (sp, _) = await OperatorAsync();
        using var scope = sp;

        var c = await Gate(sp).ClassifyVehicleAsync(norm.ToLowerInvariant());

        Assert.Equal(VehicleCategory.MonthlyPass, c.Category);
        Assert.False(c.IsResident);
        Assert.True(c.IsMonthlyPass);
        Assert.Null(c.Owner!.ApartmentCode);
    }

    // ---- H7 -------------------------------------------------------------------------------------------------------------

    private static bool InJsonl(string text)
        => File.Exists(JsonlPath) && File.ReadAllLines(JsonlPath).Any(l => l.Contains(text, StringComparison.Ordinal));

    private static bool InOfflineSessions(string text)
        => File.Exists(OfflinePath) && File.ReadAllText(OfflinePath).Contains(text, StringComparison.Ordinal);

    private async Task AssertPersistFailedAsync(GateCheckInResult result, string normalizedPlate, TestZone? zone, long idBefore, long occupiedBefore)
    {
        Assert.False(result.Success, $"in-transaction failure was admitted: {result.Message}");
        Assert.Equal(PersistFailed, result.RejectReason);
        Assert.False(result.IsPermissionDenied);
        Assert.False(result.IsBlacklisted);
        Assert.Null(result.Session);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
        Assert.DoesNotContain("nhật ký kiểm toán", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("audit", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await _data.SessionCountForPlateAsync(normalizedPlate));
        Assert.Equal(occupiedBefore, await _data.OccupiedSlotCountAsync());
        if (zone is not null)
        {
            Assert.Equal(0, await _data.OccupiedSlotCountInZoneAsync(zone.ZoneId));
        }

        Assert.Empty(await AuditDb.RowsAfterAsync(_db.Factory, idBefore));
        Assert.False(InJsonl(normalizedPlate), "no JSONL line may be written");
        Assert.False(InOfflineSessions(normalizedPlate), "no memory/offline session may be created");
    }

    [Fact]
    public async Task H7_in_transaction_blacklist_recheck_failure_is_PersistFailed_and_nothing_is_stored()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var zone = await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
        var fault = new InTransactionFaultInterceptor("\"BlacklistedVehicles\"", () => new InvalidOperationException("Simulated in-transaction query bug"));
        var (sp, _) = await OperatorAsync(InterceptorInstall.With(_db, fault));
        using var scope = sp;
        var plate = GateTestData.UniqueNormalizedPlate();
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);
        var occupiedBefore = await _data.OccupiedSlotCountAsync();

        var result = await CheckInAsync(sp, plate, vt);

        Assert.Equal(1, fault.FiredCount);
        await AssertPersistFailedAsync(result, plate, zone, idBefore, occupiedBefore);
    }

    [Fact]
    public async Task H7_in_transaction_already_inside_recheck_failure_is_PersistFailed_and_nothing_is_stored()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var zone = await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
        // The first command inside the transaction that reads ParkingSessions is the already-inside re-check.
        var fault = new InTransactionFaultInterceptor("\"ParkingSessions\"", () => new InvalidOperationException("Simulated in-transaction query bug"));
        var (sp, _) = await OperatorAsync(InterceptorInstall.With(_db, fault));
        using var scope = sp;
        var plate = GateTestData.UniqueNormalizedPlate();
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);
        var occupiedBefore = await _data.OccupiedSlotCountAsync();

        var result = await CheckInAsync(sp, plate, vt);

        Assert.Equal(1, fault.FiredCount);
        await AssertPersistFailedAsync(result, plate, zone, idBefore, occupiedBefore);
    }

    [Fact]
    public async Task H7_unknown_vehicle_type_foreign_key_violation_is_PersistFailed_and_nothing_is_stored()
    {
        _db.RequireAvailable();
        var (sp, _) = await OperatorAsync();
        using var scope = sp;
        var plate = GateTestData.UniqueNormalizedPlate();
        var unknownVehicleType = await _db.ScalarAsync<int>("SELECT COALESCE(max(\"VehicleTypeId\"), 0) + 1000 FROM \"VehicleTypes\"");
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);
        var occupiedBefore = await _data.OccupiedSlotCountAsync();

        var result = await CheckInAsync(sp, plate, unknownVehicleType);

        await AssertPersistFailedAsync(result, plate, null, idBefore, occupiedBefore);
    }

    [Fact]
    public void H7_PersistFailed_is_appended_after_the_existing_reject_reasons()
    {
        Assert.True(Enum.TryParse<CheckInRejectReason>("PersistFailed", out var value), "CheckInRejectReason.PersistFailed is missing");
        Assert.Equal((int)CheckInRejectReason.ClassificationFailed + 1, (int)value);
        Assert.Equal(value, Enum.GetValues<CheckInRejectReason>().Max());

        // Existing values keep their numbers.
        Assert.Equal(0, (int)CheckInRejectReason.None);
        Assert.Equal(1, (int)CheckInRejectReason.EmptyPlate);
        Assert.Equal(2, (int)CheckInRejectReason.AlreadyInside);
        Assert.Equal(10, (int)CheckInRejectReason.PlateInvalid);
        Assert.Equal(11, (int)CheckInRejectReason.ClassificationFailed);
    }
}
