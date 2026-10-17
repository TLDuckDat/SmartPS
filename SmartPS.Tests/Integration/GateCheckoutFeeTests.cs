using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartPS.Models.GateControl;
using SmartPS.Models.Parking;
using SmartPS.Services.GateControl;

namespace SmartPS.Tests.Integration;

/// <summary>
/// AC-R1..AC-R3 at checkout, B6 (server-side fee), D2/A2: the fee is always recomputed on the server with the injected
/// <see cref="IParkingFeeCalculator"/> and the PricingRule of the session's vehicle type; the client's TotalFee is never
/// trusted. Ticket holders (IsMonthlyPass at check-in) pay 0 even if the ticket expires during the stay.
/// Each test uses an isolated vehicle type with its own PricingRule (Block4h 7,000 / Daily 31,000).
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class GateCheckoutFeeTests : IClassFixture<PostgresDatabaseFixture>
{
    private const decimal Block4h = 7_000m;
    private const decimal Daily = 31_000m;

    private readonly PostgresDatabaseFixture _db;
    private readonly GateTestData _data;

    public GateCheckoutFeeTests(PostgresDatabaseFixture db)
    {
        _db = db;
        _data = new GateTestData(db);
    }

    private async Task<(ServiceProvider Sp, int UserId)> OperatorWithShiftAsync(Action<IServiceCollection>? overrides = null)
    {
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        var sp = IntegrationServices.Create(_db, overrides);
        await sp.LoginAsync(op.Username);
        await ParkingFlows.OpenShiftAsync(sp, op.UserId);
        return (sp, op.UserId);
    }

    private static IGateControlService Gate(IServiceProvider sp) => sp.GetRequiredService<IGateControlService>();

    private async Task<int> VehicleTypeWithZoneAsync(ZoneAudience audience = ZoneAudience.Mixed)
    {
        var vt = await _data.CreateIsolatedVehicleTypeAsync(block4hPrice: Block4h, dailyPrice: Daily);
        await _data.CreateZoneAsync(audience, vt, 2);
        return vt;
    }

    private async Task<int> CheckInBackdatedAsync(IServiceProvider sp, string plate, int vt, TimeSpan ago)
    {
        var result = await Gate(sp).ProcessCheckInAsync(new GateCheckInRequest { LicensePlate = plate, VehicleTypeId = vt });
        Assert.True(result.Success, result.Message);
        await _data.SetSessionCheckInAsync(result.Session!.SessionId, DateTime.UtcNow - ago);
        return result.Session.SessionId;
    }

    private Task<GateCheckOutResult> CompleteAsync(IServiceProvider sp, int sessionId, int userId, PaymentMethod method, decimal clientFee)
        => Gate(sp).CompleteCheckOutAsync(new GateCheckOutRequest { SessionId = sessionId, ActorUserId = userId, PaymentMethod = method, TotalFee = clientFee });

    private async Task<ParkingSession> DbSessionAsync(int sessionId)
    {
        await using var ctx = _db.CreateContext();
        return await ctx.ParkingSessions.AsNoTracking().SingleAsync(s => s.SessionId == sessionId);
    }

    private async Task<decimal> ExpectedVisitorFeeAsync(ParkingSession stored)
    {
        await using var ctx = _db.CreateContext();
        var rule = await ctx.PricingRules.AsNoTracking().SingleAsync(r => r.VehicleTypeId == stored.VehicleTypeId);
        return new StandardParkingFeeCalculator().CalculateFee(
            new ParkingSession { CheckInTime = stored.CheckInTime, VehicleTypeId = stored.VehicleTypeId, IsMonthlyPass = false },
            rule, stored.CheckOutTime!.Value).TotalFee;
    }

    // ---- AC-R1: resident without ticket pays ---------------------------------------------------------------------------

    [Fact]
    public async Task AC_R1_resident_without_ticket_pays_the_4h_block_price_at_checkout()
    {
        _db.RequireAvailable();
        var (sp, userId) = await OperatorWithShiftAsync();
        using var scope = sp;
        var vt = await VehicleTypeWithZoneAsync(ZoneAudience.ResidentOnly);
        var resident = await _data.CreateResidentAsync(vt);
        var sessionId = await CheckInBackdatedAsync(sp, resident.NormalizedPlate, vt, TimeSpan.FromHours(2));

        var calc = await Gate(sp).CalculateCheckOutAsync(resident.NormalizedPlate);

        Assert.True(calc.Success, calc.Message);
        Assert.Equal(sessionId, calc.ActiveSession!.SessionId);
        Assert.False(calc.IsMonthlyTicket);
        Assert.Equal(Block4h, calc.TotalFee);
        Assert.False(string.IsNullOrWhiteSpace(calc.FeeDetails));

        var done = await CompleteAsync(sp, sessionId, userId, PaymentMethod.Cash, calc.TotalFee);

        Assert.True(done.Success, done.Message);
        var stored = await DbSessionAsync(sessionId);
        Assert.Equal(SessionStatus.Completed, stored.Status);
        Assert.Equal(Block4h, stored.TotalFee);
        Assert.Equal(PaymentMethod.Cash, stored.PaymentMethod);
        Assert.Equal(CustomerType.Resident, stored.CustomerType);
    }

    [Fact]
    public async Task Resident_without_ticket_staying_over_4_hours_pays_the_daily_price()
    {
        _db.RequireAvailable();
        var (sp, userId) = await OperatorWithShiftAsync();
        using var scope = sp;
        var vt = await VehicleTypeWithZoneAsync();
        var resident = await _data.CreateResidentAsync(vt);
        var sessionId = await CheckInBackdatedAsync(sp, resident.NormalizedPlate, vt, TimeSpan.FromHours(5));

        var calc = await Gate(sp).CalculateCheckOutAsync(resident.NormalizedPlate);
        var done = await CompleteAsync(sp, sessionId, userId, PaymentMethod.Cash, calc.TotalFee);

        Assert.Equal(Daily, calc.TotalFee);
        Assert.True(done.Success, done.Message);
        Assert.Equal(Daily, (await DbSessionAsync(sessionId)).TotalFee);
    }

    // ---- AC-R2 / AC-R3: ticket holders pay nothing ------------------------------------------------------------------

    [Fact]
    public async Task AC_R2_external_ticket_holder_pays_zero_and_payment_method_becomes_Free()
    {
        _db.RequireAvailable();
        var (sp, userId) = await OperatorWithShiftAsync();
        using var scope = sp;
        var vt = await VehicleTypeWithZoneAsync();
        var external = await _data.CreateExternalAsync(vt, withTicket: true);
        var sessionId = await CheckInBackdatedAsync(sp, external.NormalizedPlate, vt, TimeSpan.FromHours(30));

        var calc = await Gate(sp).CalculateCheckOutAsync(external.RawPlate);
        Assert.True(calc.Success, calc.Message);
        Assert.True(calc.IsMonthlyTicket);
        Assert.Equal(0m, calc.TotalFee);

        var done = await CompleteAsync(sp, sessionId, userId, PaymentMethod.Free, 0m);

        Assert.True(done.Success, done.Message);
        var stored = await DbSessionAsync(sessionId);
        Assert.Equal(SessionStatus.Completed, stored.Status);
        Assert.Equal(0m, stored.TotalFee);
        Assert.Equal(PaymentMethod.Free, stored.PaymentMethod);
        Assert.Equal(CustomerType.External, stored.CustomerType);
        Assert.True(stored.IsMonthlyPass);
    }

    [Fact]
    public async Task AC_R3_resident_ticket_holder_pays_zero_even_when_the_client_chose_cash()
    {
        _db.RequireAvailable();
        var (sp, userId) = await OperatorWithShiftAsync();
        using var scope = sp;
        var vt = await VehicleTypeWithZoneAsync(ZoneAudience.ResidentOnly);
        var resident = await _data.CreateResidentAsync(vt, withTicket: true);
        var sessionId = await CheckInBackdatedAsync(sp, resident.NormalizedPlate, vt, TimeSpan.FromHours(3));

        var calc = await Gate(sp).CalculateCheckOutAsync(resident.NormalizedPlate);
        var done = await CompleteAsync(sp, sessionId, userId, PaymentMethod.Cash, 0m);

        Assert.True(calc.IsMonthlyTicket);
        Assert.Equal(0m, calc.TotalFee);
        Assert.True(done.Success, done.Message);
        var stored = await DbSessionAsync(sessionId);
        Assert.Equal(0m, stored.TotalFee);
        Assert.Equal(PaymentMethod.Free, stored.PaymentMethod);
        Assert.Equal(CustomerType.Resident, stored.CustomerType);
    }

    [Fact]
    public async Task A2_ticket_expiring_during_the_stay_is_still_free_because_check_in_decides()
    {
        _db.RequireAvailable();
        var (sp, userId) = await OperatorWithShiftAsync();
        using var scope = sp;
        var vt = await VehicleTypeWithZoneAsync();
        var external = await _data.CreateExternalAsync(vt, withTicket: true);
        var sessionId = await CheckInBackdatedAsync(sp, external.NormalizedPlate, vt, TimeSpan.FromHours(6));
        await _data.SetTicketEndAsync(external.TicketId!.Value, DateTime.UtcNow.AddHours(-1));

        var calc = await Gate(sp).CalculateCheckOutAsync(external.NormalizedPlate);
        var done = await CompleteAsync(sp, sessionId, userId, PaymentMethod.Free, 0m);

        Assert.Equal(0m, calc.TotalFee);
        Assert.True(done.Success, done.Message);
        Assert.Equal(0m, (await DbSessionAsync(sessionId)).TotalFee);
    }

    // ---- B6: the client fee is never trusted ----------------------------------------------------------------------------

    [Fact]
    public async Task Client_fee_of_zero_is_ignored_and_the_server_fee_is_stored_and_audited()
    {
        _db.RequireAvailable();
        var (sp, userId) = await OperatorWithShiftAsync();
        using var scope = sp;
        var vt = await VehicleTypeWithZoneAsync();
        var resident = await _data.CreateResidentAsync(vt);
        var sessionId = await CheckInBackdatedAsync(sp, resident.NormalizedPlate, vt, TimeSpan.FromHours(3));
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var done = await CompleteAsync(sp, sessionId, userId, PaymentMethod.Cash, 0m);

        Assert.True(done.Success, done.Message);
        var stored = await DbSessionAsync(sessionId);
        var expected = await ExpectedVisitorFeeAsync(stored);
        Assert.Equal(Block4h, expected);
        Assert.Equal(expected, stored.TotalFee);
        Assert.Equal(PaymentMethod.Cash, stored.PaymentMethod);
        Assert.Equal(expected, done.CompletedSession!.TotalFee);
        var row = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.ParkingCheckOut));
        Assert.Equal(expected, AuditDb.Details(row).GetProperty("fee").GetDecimal());
    }

    [Fact]
    public async Task Client_fee_higher_than_the_server_fee_is_ignored()
    {
        _db.RequireAvailable();
        var (sp, userId) = await OperatorWithShiftAsync();
        using var scope = sp;
        var vt = await VehicleTypeWithZoneAsync();
        var plate = GateTestData.UniqueNormalizedPlate();
        var sessionId = await CheckInBackdatedAsync(sp, plate, vt, TimeSpan.FromHours(1));

        var done = await CompleteAsync(sp, sessionId, userId, PaymentMethod.Cash, 999_999m);

        Assert.True(done.Success, done.Message);
        Assert.Equal(Block4h, (await DbSessionAsync(sessionId)).TotalFee);
    }

    [Fact]
    public async Task Free_method_is_rejected_when_the_server_fee_is_positive()
    {
        _db.RequireAvailable();
        var (sp, userId) = await OperatorWithShiftAsync();
        using var scope = sp;
        var vt = await VehicleTypeWithZoneAsync();
        var resident = await _data.CreateResidentAsync(vt);
        var sessionId = await CheckInBackdatedAsync(sp, resident.NormalizedPlate, vt, TimeSpan.FromHours(2));
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var done = await CompleteAsync(sp, sessionId, userId, PaymentMethod.Free, 0m);

        Assert.False(done.Success);
        Assert.False(done.IsPermissionDenied);
        Assert.Equal(SessionStatus.Active, (await DbSessionAsync(sessionId)).Status);
        Assert.Equal(1, await _data.SessionCountForPlateAsync(resident.NormalizedPlate, activeOnly: true));
        Assert.Empty(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.ParkingCheckOut));
    }

    [Fact]
    public async Task Negative_client_fee_does_not_change_the_server_fee()
    {
        _db.RequireAvailable();
        var (sp, userId) = await OperatorWithShiftAsync();
        using var scope = sp;
        var vt = await VehicleTypeWithZoneAsync();
        var plate = GateTestData.UniqueNormalizedPlate();
        var sessionId = await CheckInBackdatedAsync(sp, plate, vt, TimeSpan.FromHours(1));

        var done = await CompleteAsync(sp, sessionId, userId, PaymentMethod.Cash, -5_000m);

        // Either the request is rejected or the server fee is used; a negative or client-chosen amount is never stored.
        var stored = await DbSessionAsync(sessionId);
        if (done.Success)
        {
            Assert.Equal(Block4h, stored.TotalFee);
        }
        else
        {
            Assert.Equal(SessionStatus.Active, stored.Status);
        }
    }

    [Fact]
    public async Task Checkout_uses_the_injected_fee_calculator_on_both_preview_and_completion()
    {
        _db.RequireAvailable();
        var calculator = new FixedFeeCalculator(12_345m);
        var (sp, userId) = await OperatorWithShiftAsync(services => services.AddSingleton<IParkingFeeCalculator>(calculator));
        using var scope = sp;
        var vt = await VehicleTypeWithZoneAsync();
        var plate = GateTestData.UniqueNormalizedPlate();
        var sessionId = await CheckInBackdatedAsync(sp, plate, vt, TimeSpan.FromHours(1));

        var calc = await Gate(sp).CalculateCheckOutAsync(plate);
        var done = await CompleteAsync(sp, sessionId, userId, PaymentMethod.Cash, 1m);

        Assert.Equal(12_345m, calc.TotalFee);
        Assert.True(done.Success, done.Message);
        Assert.Equal(12_345m, (await DbSessionAsync(sessionId)).TotalFee);
        Assert.True(calculator.Calls >= 2, $"calculator was called {calculator.Calls} time(s); completion must recompute");
        Assert.All(calculator.Rules, r => Assert.Equal(vt, r?.VehicleTypeId));
        Assert.All(calculator.Rules, r => Assert.Equal(Block4h, r?.Block4hPrice));
    }

    private sealed class FixedFeeCalculator : IParkingFeeCalculator
    {
        private readonly decimal _fee;
        private int _calls;

        public FixedFeeCalculator(decimal fee) => _fee = fee;

        public int Calls => Volatile.Read(ref _calls);

        public System.Collections.Concurrent.ConcurrentBag<PricingRule?> Rules { get; } = new();

        public ParkingFeeCalculationResult CalculateFee(ParkingSession session, PricingRule? pricingRule, DateTime checkOutTime)
        {
            Interlocked.Increment(ref _calls);
            Rules.Add(pricingRule);
            return new ParkingFeeCalculationResult
            {
                Duration = checkOutTime - session.CheckInTime,
                RawFee = _fee,
                TotalFee = session.IsMonthlyPass ? 0m : _fee,
                IsMonthlyTicket = session.IsMonthlyPass,
                Message = "fixed",
                FeeDetails = "fixed"
            };
        }
    }
}
