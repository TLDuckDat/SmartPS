using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartPS.Models.GateControl;
using SmartPS.Models.Parking;
using SmartPS.Services.Audit;
using SmartPS.Services.Customers;
using SmartPS.Services.GateControl;

namespace SmartPS.Tests.Integration;

/// <summary>
/// Fix round 1 regression tests for the gate (G1, G2, G3, G4, G9, G10, G11), ported from the challenge repros
/// (Attacks.C2, A1, A2, D3, D6, G2; Attacks2.H1).
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class FixRound1GateRegressionTests : IClassFixture<PostgresDatabaseFixture>
{
    private readonly PostgresDatabaseFixture _db;
    private readonly ResidentVisitorData _data;

    public FixRound1GateRegressionTests(PostgresDatabaseFixture db)
    {
        _db = db;
        _data = new ResidentVisitorData(db);
    }

    private async Task<(ServiceProvider Sp, int UserId)> LoginAsync(string roleName)
    {
        var user = await TestUsers.CreateAsync(_db.Factory, roleName);
        var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(user.Username);
        return (sp, user.UserId);
    }

    private static GateCheckInRequest Req(string plate, int vehicleTypeId, int? slotId = null)
        => new() { LicensePlate = plate, VehicleTypeId = vehicleTypeId, SlotId = slotId };

    private static IGateControlService Gate(IServiceProvider sp) => sp.GetRequiredService<IGateControlService>();

    private async Task<PricingRule> RuleAsync(int vehicleTypeId)
    {
        await using var ctx = _db.CreateContext();
        return await ctx.PricingRules.AsNoTracking().SingleAsync(r => r.VehicleTypeId == vehicleTypeId);
    }

    private static decimal VisitorFee(PricingRule rule, int vehicleTypeId, DateTime fromUtc, DateTime toUtc)
        => new StandardParkingFeeCalculator().CalculateFee(
            new ParkingSession { CheckInTime = fromUtc, VehicleTypeId = vehicleTypeId, IsMonthlyPass = false }, rule, toUtc).TotalFee;

    private async Task<DateTime> SetCheckInAsync(int sessionId, DateTime checkInUtc)
    {
        await _data.SetSessionCheckInAsync(sessionId, checkInUtc);
        return await PostgresDatabaseFixture.ScalarAsync<DateTime>(_db.ConnectionString,
            "SELECT \"CheckInTime\" FROM \"ParkingSessions\" WHERE \"SessionId\" = @id", ("id", sessionId));
    }

    private static DateTime VnMidnight(DateOnly date) => AuditTime.VietnamDateStartUtc(date);

    // ---------------------------------------------------------------- G1 (CH1-01, Attacks.C2) ----------------------

    [Fact]
    public async Task G1_same_plate_checked_in_in_parallel_creates_exactly_one_session()
    {
        _db.RequireAvailable();
        var (sp1, _) = await LoginAsync("Operator");
        var (sp2, _) = await LoginAsync("Operator");
        using var scope1 = sp1;
        using var scope2 = sp2;

        for (var round = 0; round < 3; round++)
        {
            var vt = await _data.CreateIsolatedVehicleTypeAsync();
            var zone = await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 3);
            var plate = ResidentVisitorData.UniqueNormalizedPlate();
            using var start = new ManualResetEventSlim(false);

            Task<GateCheckInResult> Run(IServiceProvider sp, string typed) => Task.Run(async () =>
            {
                start.Wait();
                return await Gate(sp).ProcessCheckInAsync(Req(typed, vt));
            });

            var a = Run(sp1, plate);
            var b = Run(sp2, ResidentVisitorData.Decorate(plate)); // same plate, different formatting
            start.Set();
            var results = await Task.WhenAll(a, b);

            Assert.Equal(1, results.Count(r => r.Success));
            var loser = results.Single(r => !r.Success);
            Assert.Equal(CheckInRejectReason.AlreadyInside, loser.RejectReason);
            Assert.Equal(1, await _data.SessionCountForPlateAsync(plate, activeOnly: true));
            Assert.Equal(1, await _data.CountAsync("SELECT count(*) FROM \"ParkingSlots\" WHERE \"ZoneId\" = @z AND \"Status\" <> 0", ("z", zone.ZoneId)));
        }
    }

    // ---------------------------------------------------------------- G2 (review F1) -------------------------------

    [Fact]
    public async Task G2_legacy_ticket_renewed_while_active_still_covers_the_original_period()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 2);
        var today = TicketDates.TodayVn(DateTime.UtcNow);
        var customerId = await _data.CreateCustomerAsync(isResident: true);
        var plate = ResidentVisitorData.UniqueNormalizedPlate();
        await _data.AddVehicleAsync(customerId, plate, vt);
        // Legacy ticket: no Kind=Create purchase row
        var ticketId = await _data.CreateTicketAsync(customerId, plate, vt, VnMidnight(today.AddDays(-10)), VnMidnight(today.AddDays(20)), withCreatePurchase: false);
        var planId = await _data.CreateIsolatedPlanAsync(vt, durationMonths: 1, totalPrice: 150_000m);

        var (op, _) = await LoginAsync("Operator");
        using var opScope = op;
        var checkIn = await Gate(op).ProcessCheckInAsync(Req(plate, vt));
        Assert.True(checkIn.Success, checkIn.Message);
        Assert.True(checkIn.Session!.IsMonthlyPass);
        await SetCheckInAsync(checkIn.Session.SessionId, DateTime.UtcNow.AddHours(-4));

        // Renew while active: the only purchase row is Renew [oldEnd, newEnd)
        var (mgr, _) = await LoginAsync("Manager");
        using var mgrScope = mgr;
        var renew = await mgr.GetRequiredService<IMonthlyTicketService>().RenewTicketAsync(ticketId, planId);
        Assert.True(renew.Success, $"{renew.Error}: {renew.Message}");
        Assert.Equal(1, await _data.CountAsync("SELECT count(*) FROM \"MonthlyTicketPurchases\" WHERE \"TicketId\" = @t", ("t", ticketId)));

        // Checkout inside the original (legacy) period is still free
        var calc = await Gate(op).CalculateCheckOutAsync(plate);

        Assert.True(calc.Success, calc.Message);
        Assert.Equal(0m, calc.TotalFee);
        Assert.True(calc.IsMonthlyTicket);
        Assert.False(calc.TicketExpiredDuringStay);
        Assert.False(calc.TicketNoLongerValid);
        Assert.True(calc.TicketValidUntilUtc > DateTime.UtcNow.AddDays(20), $"coverage must chain into the renewal, got {calc.TicketValidUntilUtc:o}");
    }

    [Fact]
    public async Task G2_legacy_ticket_without_purchases_covers_its_interval()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
        var today = TicketDates.TodayVn(DateTime.UtcNow);
        var customerId = await _data.CreateCustomerAsync(isResident: false);
        var plate = ResidentVisitorData.UniqueNormalizedPlate();
        await _data.AddVehicleAsync(customerId, plate, vt);
        await _data.CreateTicketAsync(customerId, plate, vt, VnMidnight(today.AddDays(-10)), VnMidnight(today.AddDays(20)), withCreatePurchase: false);
        var (op, _) = await LoginAsync("Operator");
        using var opScope = op;
        var checkIn = await Gate(op).ProcessCheckInAsync(Req(plate, vt));
        Assert.True(checkIn.Success, checkIn.Message);
        await SetCheckInAsync(checkIn.Session!.SessionId, DateTime.UtcNow.AddHours(-4));

        var calc = await Gate(op).CalculateCheckOutAsync(plate);

        Assert.Equal(0m, calc.TotalFee);
        Assert.True(calc.IsMonthlyTicket);
        Assert.Equal(VnMidnight(today.AddDays(20)), calc.TicketValidUntilUtc);
    }

    // ---------------------------------------------------------------- G3 (CH1-02, Attacks.A1/A2, Attacks2.H1) ------

    [Theory]
    [InlineData("---")]
    [InlineData("...")]
    [InlineData("AB12")]                 // 4 after normalization
    [InlineData("ABCDEFG1234567")]       // 14 after normalization
    [InlineData("１２３４５")]            // full-width digits
    [InlineData("Ｂ８８")]
    [InlineData("51F-12З.45")]          // Cyrillic Ze looks like a 3
    public async Task G3_invalid_plates_are_rejected_without_a_session(string typed)
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var zone = await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 2);
        var (sp, _) = await LoginAsync("Operator");
        using var scope = sp;
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var result = await Gate(sp).ProcessCheckInAsync(Req(typed, vt));

        Assert.False(result.Success);
        Assert.Equal(CheckInRejectReason.PlateInvalid, result.RejectReason);
        Assert.False(result.IsPermissionDenied);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
        Assert.Null(result.Session);
        Assert.Equal(0, await _data.ActiveSessionCountForVehicleTypeAsync(vt));
        Assert.Equal(0, await _data.CountAsync("SELECT count(*) FROM \"ParkingSlots\" WHERE \"ZoneId\" = @z AND \"Status\" <> 0", ("z", zone.ZoneId)));
        Assert.Empty(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.ParkingCheckIn));
    }

    [Fact]
    public async Task G3_blank_plate_is_still_EmptyPlate()
    {
        _db.RequireAvailable();
        var (sp, _) = await LoginAsync("Operator");
        using var scope = sp;

        var result = await Gate(sp).ProcessCheckInAsync(Req("   ", 1));

        Assert.False(result.Success);
        Assert.Equal(CheckInRejectReason.EmptyPlate, result.RejectReason);
    }

    [Fact]
    public async Task G3_lookalike_variant_of_a_blacklisted_plate_is_never_admitted()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 3);
        var plate = ResidentVisitorData.UniqueNormalizedPlate(); // starts with "RV"
        await _data.AddBlacklistAsync(plate, "challenge");
        var (sp, _) = await LoginAsync("Operator");
        using var scope = sp;
        var gate = Gate(sp);

        var cyrillic = await gate.ProcessCheckInAsync(Req(plate.Replace('V', 'В'), vt));        // Cyrillic VE
        var fullWidth = await gate.ProcessCheckInAsync(Req(new string(plate.Select(ToFullWidth).ToArray()), vt));
        var zeroWidth = await gate.ProcessCheckInAsync(Req(plate[..3] + "​" + plate[3..], vt)); // zero-width space is a separator
        var nbspTab = await gate.ProcessCheckInAsync(Req(plate[..2] + " " + plate[2..5] + "\t" + plate[5..], vt));

        Assert.False(cyrillic.Success);
        Assert.Equal(CheckInRejectReason.PlateInvalid, cyrillic.RejectReason);
        Assert.False(fullWidth.Success);
        Assert.Equal(CheckInRejectReason.PlateInvalid, fullWidth.RejectReason);
        Assert.False(zeroWidth.Success);
        Assert.Equal(CheckInRejectReason.Blacklisted, zeroWidth.RejectReason);
        Assert.False(nbspTab.Success);
        Assert.Equal(CheckInRejectReason.Blacklisted, nbspTab.RejectReason);
        Assert.Equal(0, await _data.ActiveSessionCountForVehicleTypeAsync(vt));
    }

    [Fact]
    public async Task G3_checkout_calculation_for_a_junk_plate_matches_nothing()
    {
        _db.RequireAvailable();
        var (sp, _) = await LoginAsync("Operator");
        using var scope = sp;

        var calc = await Gate(sp).CalculateCheckOutAsync("###");

        Assert.False(calc.Success);
        Assert.Null(calc.ActiveSession);
    }

    private static char ToFullWidth(char c)
        => c is >= '0' and <= '9' ? (char)(c - '0' + 0xFF10) : c is >= 'A' and <= 'Z' ? (char)(c - 'A' + 0xFF21) : c;

    // ---------------------------------------------------------------- G4 (CH1-03, Attacks.D3) ----------------------

    [Fact]
    public async Task G4_removing_the_vehicle_after_a_legitimate_expiry_does_not_change_the_fee()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 2);
        var sub = await _data.CreateSubscriberAsync(vt, isResident: true);
        var (op, _) = await LoginAsync("Operator");
        using var opScope = op;
        var checkIn = await Gate(op).ProcessCheckInAsync(Req(sub.Plate, vt));
        Assert.True(checkIn.Success, checkIn.Message);
        await SetCheckInAsync(checkIn.Session!.SessionId, DateTime.UtcNow.AddHours(-4));
        var expiry = DateTime.UtcNow.AddMinutes(-90);
        await _data.SetTicketEndAsync(sub.TicketId!.Value, expiry);
        await _data.SetPurchaseEndAsync(sub.TicketId.Value, TicketPurchaseKind.Create, expiry);

        var before = await Gate(op).CalculateCheckOutAsync(sub.Plate);
        Assert.True(before.TicketExpiredDuringStay);

        // Staff removes the (now expired) vehicle through the service — allowed because the ticket is expired
        var (mgr, _) = await LoginAsync("Manager");
        using var mgrScope = mgr;
        var removed = await mgr.GetRequiredService<ICustomerService>().RemoveVehicleAsync(sub.CustomerVehicleId);
        Assert.True(removed.Success, $"{removed.Error}: {removed.Message}");

        var after = await Gate(op).CalculateCheckOutAsync(sub.Plate);

        Assert.True(after.TicketExpiredDuringStay);
        Assert.False(after.TicketNoLongerValid);
        Assert.Equal(before.ChargeFromUtc, after.ChargeFromUtc);
        Assert.Equal(before.TicketValidUntilUtc, after.TicketValidUntilUtc);
        var rule = await RuleAsync(vt);
        Assert.Equal(VisitorFee(rule, vt, after.ChargeFromUtc!.Value, after.CheckOutTime), after.TotalFee);
    }

    // ---------------------------------------------------------------- G9 (CH1-08, Attacks.D6) ----------------------

    [Fact]
    public async Task G9_checkout_ignores_a_client_fee_of_zero_and_uses_the_server_fee()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
        var (sp, userId) = await LoginAsync("Operator");
        using var scope = sp;
        await ParkingFlows.OpenShiftAsync(sp, userId);
        var plate = ResidentVisitorData.UniqueNormalizedPlate();
        var checkIn = await Gate(sp).ProcessCheckInAsync(Req(plate, vt));
        Assert.True(checkIn.Success, checkIn.Message);
        var checkInUtc = await SetCheckInAsync(checkIn.Session!.SessionId, DateTime.UtcNow.AddHours(-3));
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var done = await Gate(sp).CompleteCheckOutAsync(new GateCheckOutRequest
        {
            SessionId = checkIn.Session.SessionId,
            ActorUserId = userId,
            PaymentMethod = PaymentMethod.Cash,
            TotalFee = 0m
        });

        Assert.True(done.Success, done.Message);
        await using var ctx = _db.CreateContext();
        var session = await ctx.ParkingSessions.AsNoTracking().SingleAsync(s => s.SessionId == checkIn.Session.SessionId);
        Assert.Equal(SessionStatus.Completed, session.Status);
        var expected = VisitorFee(await RuleAsync(vt), vt, checkInUtc, session.CheckOutTime!.Value);
        Assert.True(expected > 0);
        Assert.Equal(expected, session.TotalFee);
        Assert.Equal(expected, done.CompletedSession!.TotalFee);
        var row = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.ParkingCheckOut));
        Assert.Equal(expected, AuditDb.Details(row).GetProperty("fee").GetDecimal());
    }

    [Fact]
    public async Task G9_free_payment_method_is_rejected_when_the_server_fee_is_positive()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
        var (sp, userId) = await LoginAsync("Operator");
        using var scope = sp;
        await ParkingFlows.OpenShiftAsync(sp, userId);
        var plate = ResidentVisitorData.UniqueNormalizedPlate();
        var checkIn = await Gate(sp).ProcessCheckInAsync(Req(plate, vt));
        Assert.True(checkIn.Success, checkIn.Message);
        await SetCheckInAsync(checkIn.Session!.SessionId, DateTime.UtcNow.AddHours(-3));
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var done = await Gate(sp).CompleteCheckOutAsync(new GateCheckOutRequest
        {
            SessionId = checkIn.Session.SessionId,
            ActorUserId = userId,
            PaymentMethod = PaymentMethod.Free,
            TotalFee = 0m
        });

        Assert.False(done.Success);
        Assert.False(done.IsPermissionDenied);
        Assert.Equal(1, await _data.SessionCountForPlateAsync(plate, activeOnly: true));
        Assert.Empty(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.ParkingCheckOut));
    }

    [Fact]
    public async Task G9_free_exit_of_a_covered_monthly_vehicle_still_works()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
        var sub = await _data.CreateSubscriberAsync(vt, isResident: true);
        var (sp, userId) = await LoginAsync("Operator");
        using var scope = sp;
        await ParkingFlows.OpenShiftAsync(sp, userId);
        var checkIn = await Gate(sp).ProcessCheckInAsync(Req(sub.Plate, vt));
        Assert.True(checkIn.Success, checkIn.Message);

        var done = await Gate(sp).CompleteCheckOutAsync(new GateCheckOutRequest
        {
            SessionId = checkIn.Session!.SessionId,
            ActorUserId = userId,
            PaymentMethod = PaymentMethod.Free,
            TotalFee = 0m
        });

        Assert.True(done.Success, done.Message);
        Assert.Equal(0m, done.CompletedSession!.TotalFee);
    }

    [Fact]
    public async Task G9_partial_monthly_charge_ignores_a_client_fee_of_zero()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
        var sub = await _data.CreateSubscriberAsync(vt, isResident: true);
        var (sp, userId) = await LoginAsync("Operator");
        using var scope = sp;
        await ParkingFlows.OpenShiftAsync(sp, userId);
        var checkIn = await Gate(sp).ProcessCheckInAsync(Req(sub.Plate, vt));
        Assert.True(checkIn.Success, checkIn.Message);
        await SetCheckInAsync(checkIn.Session!.SessionId, DateTime.UtcNow.AddHours(-4));
        var expiry = DateTime.UtcNow.AddMinutes(-90);
        await _data.SetTicketEndAsync(sub.TicketId!.Value, expiry);
        await _data.SetPurchaseEndAsync(sub.TicketId.Value, TicketPurchaseKind.Create, expiry);

        var done = await Gate(sp).CompleteCheckOutAsync(new GateCheckOutRequest
        {
            SessionId = checkIn.Session.SessionId,
            ActorUserId = userId,
            PaymentMethod = PaymentMethod.Cash,
            TotalFee = 0m
        });

        Assert.True(done.Success, done.Message);
        await using var ctx = _db.CreateContext();
        var session = await ctx.ParkingSessions.AsNoTracking().SingleAsync(s => s.SessionId == checkIn.Session.SessionId);
        var storedExpiry = await PostgresDatabaseFixture.ScalarAsync<DateTime>(_db.ConnectionString,
            "SELECT \"PeriodEndUtc\" FROM \"MonthlyTicketPurchases\" WHERE \"TicketId\" = @t AND \"Kind\" = 0", ("t", sub.TicketId.Value));
        Assert.Equal(VisitorFee(await RuleAsync(vt), vt, storedExpiry, session.CheckOutTime!.Value), session.TotalFee);
        Assert.True(session.TotalFee > 0);
    }

    // ---------------------------------------------------------------- G10 (CH1-09, Attacks.G2) ---------------------

    [Fact]
    public async Task G10_ticket_for_another_vehicle_type_is_not_honoured()
    {
        _db.RequireAvailable();
        var moto = await _data.CreateIsolatedVehicleTypeAsync();
        var car = await _data.CreateIsolatedVehicleTypeAsync();
        var carResident = await _data.CreateZoneAsync(ZoneAudience.ResidentOnly, car, 1);
        await _data.CreateZoneAsync(ZoneAudience.Mixed, car, 1);
        var sub = await _data.CreateSubscriberAsync(moto, isResident: true); // ticket + vehicle are motorbike
        var (sp, userId) = await LoginAsync("Operator");
        using var scope = sp;
        await ParkingFlows.OpenShiftAsync(sp, userId);

        var result = await Gate(sp).ProcessCheckInAsync(Req(sub.Plate, car));

        Assert.True(result.Success, result.Message);
        Assert.Equal(VehicleCategory.Visitor, result.Category);
        Assert.True(result.TicketVehicleTypeMismatchWarning);
        Assert.False(result.IsMonthlyTicket);
        Assert.False(result.IsResident);
        Assert.NotEqual(ZoneAudience.ResidentOnly, result.AssignedZoneAudience);
        Assert.Equal(SlotStatus.Available, await _data.SlotStatusAsync(carResident.SlotIds[0]));
        await using (var ctx = _db.CreateContext())
        {
            var session = await ctx.ParkingSessions.AsNoTracking().SingleAsync(s => s.SessionId == result.Session!.SessionId);
            Assert.False(session.IsMonthlyPass);
            Assert.Equal(CustomerType.Regular, session.CustomerType);
            Assert.Null(session.CustomerId);
        }

        await SetCheckInAsync(result.Session!.SessionId, DateTime.UtcNow.AddHours(-3));
        var calc = await Gate(sp).CalculateCheckOutAsync(sub.Plate);
        Assert.False(calc.IsMonthlyTicket);
        Assert.True(calc.TotalFee > 0);
    }

    [Fact]
    public async Task G10_ticket_of_the_matching_vehicle_type_still_applies()
    {
        _db.RequireAvailable();
        var moto = await _data.CreateIsolatedVehicleTypeAsync();
        await _data.CreateZoneAsync(ZoneAudience.ResidentOnly, moto, 1);
        var sub = await _data.CreateSubscriberAsync(moto, isResident: true);
        var (sp, _) = await LoginAsync("Operator");
        using var scope = sp;

        var result = await Gate(sp).ProcessCheckInAsync(Req(sub.Plate, moto));

        Assert.True(result.Success, result.Message);
        Assert.Equal(VehicleCategory.Resident, result.Category);
        Assert.False(result.TicketVehicleTypeMismatchWarning);
    }

    // ---------------------------------------------------------------- G11 (review F4) ------------------------------

    [Fact]
    public async Task G11_non_connection_classification_failure_fails_closed()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var zone = await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 2);
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        var hook = new CommandHookInterceptor("BlacklistEntries", occurrence: 1, throwInstead: true,
            exceptionFactory: () => new InvalidOperationException("Simulated query bug"));
        using var sp = IntegrationServices.Create(_db, CommandHookInterceptor.Install(_db, hook));
        await sp.LoginAsync(op.Username);
        var plate = ResidentVisitorData.UniqueNormalizedPlate();
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var result = await Gate(sp).ProcessCheckInAsync(Req(plate, vt));

        Assert.Equal(1, hook.FiredCount);
        Assert.False(result.Success);
        Assert.Equal(CheckInRejectReason.ClassificationFailed, result.RejectReason);
        Assert.False(result.IsPermissionDenied);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
        Assert.Null(result.Session);
        Assert.Equal(0, await _data.SessionCountForPlateAsync(plate));
        Assert.Equal(0, await _data.CountAsync("SELECT count(*) FROM \"ParkingSlots\" WHERE \"ZoneId\" = @z AND \"Status\" <> 0", ("z", zone.ZoneId)));
        Assert.Empty(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.ParkingCheckIn));

        // The plate is not considered inside afterwards (no memory session either)
        using var sp2 = IntegrationServices.Create(_db);
        await sp2.LoginAsync(op.Username);
        var retry = await Gate(sp2).ProcessCheckInAsync(Req(plate, vt));
        Assert.True(retry.Success, retry.Message);
    }
}
