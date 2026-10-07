using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartPS.Models.GateControl;
using SmartPS.Models.Parking;
using SmartPS.Services.GateControl;
using SmartPS.Services.Payment;

namespace SmartPS.Tests.Integration;

/// <summary>
/// AC-5 / R15 / decision 1 / m8 / M2: a monthly vehicle whose coverage ends during the stay pays the visitor rate from the
/// end of coverage; coverage is the contiguous chain of paid periods; suspension / reassignment charges the whole stay.
/// Expected amounts are computed through <see cref="StandardParkingFeeCalculator"/> (overnight rule uses UTC hours, pre-existing).
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class GateMonthlyExpiryCheckoutTests : IClassFixture<PostgresDatabaseFixture>
{
    private readonly PostgresDatabaseFixture _db;
    private readonly ResidentVisitorData _data;

    public GateMonthlyExpiryCheckoutTests(PostgresDatabaseFixture db)
    {
        _db = db;
        _data = new ResidentVisitorData(db);
    }

    private sealed record Setup(ServiceProvider Sp, int UserId, int VehicleTypeId, TestSubscriber Subscriber, int SessionId, DateTime CheckInUtc, PricingRule Rule);

    /// <summary>Isolated vehicle type with the motorbike rule, a resident with a valid ticket, checked in, then check-in moved 4 h back.</summary>
    private async Task<Setup> CheckedInSubscriberAsync(bool isResident = true)
    {
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 2);
        var subscriber = await _data.CreateSubscriberAsync(vt, isResident);
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(op.Username);

        var checkIn = await sp.GetRequiredService<IGateControlService>().ProcessCheckInAsync(new GateCheckInRequest { LicensePlate = subscriber.Plate, VehicleTypeId = vt });
        Assert.True(checkIn.Success, checkIn.Message);
        Assert.True(checkIn.Session!.IsMonthlyPass);

        var checkInUtc = DateTime.UtcNow.AddHours(-4);
        await _data.SetSessionCheckInAsync(checkIn.Session.SessionId, checkInUtc);
        checkInUtc = await PostgresDatabaseFixture.ScalarAsync<DateTime>(_db.ConnectionString,
            "SELECT \"CheckInTime\" FROM \"ParkingSessions\" WHERE \"SessionId\" = @id", ("id", checkIn.Session.SessionId));

        await using var ctx = _db.CreateContext();
        var rule = await ctx.PricingRules.AsNoTracking().SingleAsync(r => r.VehicleTypeId == vt);
        return new Setup(sp, op.UserId, vt, subscriber, checkIn.Session.SessionId, checkInUtc, rule);
    }

    private static decimal VisitorFee(PricingRule rule, int vehicleTypeId, DateTime fromUtc, DateTime toUtc)
        => new StandardParkingFeeCalculator().CalculateFee(
            new ParkingSession { CheckInTime = fromUtc, VehicleTypeId = vehicleTypeId, IsMonthlyPass = false },
            rule,
            toUtc).TotalFee;

    /// <summary>Moves the end of the ticket's Create purchase (and the ticket end) to <paramref name="endUtc"/>; returns the stored value.</summary>
    private async Task<DateTime> ExpireCreatePurchaseAtAsync(int ticketId, DateTime endUtc, bool alsoTicket = true)
    {
        await _data.SetPurchaseEndAsync(ticketId, TicketPurchaseKind.Create, endUtc);
        if (alsoTicket)
        {
            await _data.SetTicketEndAsync(ticketId, endUtc);
        }

        return await PostgresDatabaseFixture.ScalarAsync<DateTime>(_db.ConnectionString,
            "SELECT \"PeriodEndUtc\" FROM \"MonthlyTicketPurchases\" WHERE \"TicketId\" = @t AND \"Kind\" = 0", ("t", ticketId));
    }

    [Fact]
    public async Task AC5_ticket_expired_during_the_stay_charges_visitor_rate_from_expiry_and_payment_uses_the_same_amount()
    {
        _db.RequireAvailable();
        var s = await CheckedInSubscriberAsync();
        using var spScope = s.Sp;
        var expiry = await ExpireCreatePurchaseAtAsync(s.Subscriber.TicketId!.Value, DateTime.UtcNow.AddMinutes(-90));

        var calc = await s.Sp.GetRequiredService<IGateControlService>().CalculateCheckOutAsync(s.Subscriber.Plate);

        Assert.True(calc.Success, calc.Message);
        Assert.True(calc.TicketExpiredDuringStay);
        Assert.False(calc.TicketNoLongerValid);
        Assert.False(calc.IsMonthlyTicket);
        Assert.Equal(expiry, calc.TicketValidUntilUtc);
        Assert.Equal(expiry, calc.ChargeFromUtc);
        var expected = VisitorFee(s.Rule, s.VehicleTypeId, expiry, calc.CheckOutTime);
        Assert.True(expected > 0);
        Assert.Equal(expected, calc.TotalFee);
        Assert.Equal(calc.CheckOutTime - s.CheckInUtc, calc.Duration); // the displayed duration is still the whole stay

        // PaymentService computes the same split
        await ParkingFlows.OpenShiftAsync(s.Sp, s.UserId);
        var payment = await ParkingFlows.CreateVietQrAsync(s.Sp, s.SessionId, s.UserId);
        Assert.True(payment.Success, payment.Message);
        Assert.Equal(VisitorFee(s.Rule, s.VehicleTypeId, expiry, DateTime.UtcNow), payment.Amount);
    }

    [Fact]
    public async Task Valid_ticket_at_exit_is_free()
    {
        _db.RequireAvailable();
        var s = await CheckedInSubscriberAsync();
        using var spScope = s.Sp;

        var calc = await s.Sp.GetRequiredService<IGateControlService>().CalculateCheckOutAsync(s.Subscriber.Plate);

        Assert.True(calc.Success, calc.Message);
        Assert.Equal(0m, calc.TotalFee);
        Assert.True(calc.IsMonthlyTicket);
        Assert.False(calc.TicketExpiredDuringStay);
        Assert.False(calc.TicketNoLongerValid);
        Assert.NotNull(calc.TicketValidUntilUtc);
        Assert.True(calc.TicketValidUntilUtc > calc.CheckOutTime);
    }

    [Fact]
    public async Task Monthly_non_resident_follows_the_same_split()
    {
        _db.RequireAvailable();
        var s = await CheckedInSubscriberAsync(isResident: false);
        using var spScope = s.Sp;
        var expiry = await ExpireCreatePurchaseAtAsync(s.Subscriber.TicketId!.Value, DateTime.UtcNow.AddMinutes(-60));

        var calc = await s.Sp.GetRequiredService<IGateControlService>().CalculateCheckOutAsync(s.Subscriber.Plate);

        Assert.True(calc.TicketExpiredDuringStay);
        Assert.Equal(VisitorFee(s.Rule, s.VehicleTypeId, expiry, calc.CheckOutTime), calc.TotalFee);
    }

    [Fact]
    public async Task M8_contiguous_renewal_during_the_stay_keeps_the_exit_free()
    {
        _db.RequireAvailable();
        var s = await CheckedInSubscriberAsync();
        using var spScope = s.Sp;
        var ticketId = s.Subscriber.TicketId!.Value;
        var e1 = await ExpireCreatePurchaseAtAsync(ticketId, DateTime.UtcNow.AddHours(-2), alsoTicket: false);
        var e2 = DateTime.UtcNow.AddDays(28);
        await _data.AddPurchaseAsync(ticketId, TicketPurchaseKind.Renew, e1, e2);
        await _data.SetTicketEndAsync(ticketId, e2);

        var calc = await s.Sp.GetRequiredService<IGateControlService>().CalculateCheckOutAsync(s.Subscriber.Plate);

        Assert.Equal(0m, calc.TotalFee);
        Assert.True(calc.IsMonthlyTicket);
        Assert.False(calc.TicketExpiredDuringStay);
    }

    [Fact]
    public async Task M8_renewal_after_a_gap_charges_from_the_end_of_the_first_period()
    {
        _db.RequireAvailable();
        var s = await CheckedInSubscriberAsync();
        using var spScope = s.Sp;
        var ticketId = s.Subscriber.TicketId!.Value;
        var e1 = await ExpireCreatePurchaseAtAsync(ticketId, DateTime.UtcNow.AddHours(-2), alsoTicket: false);
        var renewStart = DateTime.UtcNow.AddHours(-1);
        var renewEnd = DateTime.UtcNow.AddDays(29);
        await _data.AddPurchaseAsync(ticketId, TicketPurchaseKind.Renew, renewStart, renewEnd);
        await _db.ExecuteAsync("UPDATE \"MonthlyTickets\" SET \"StartDate\" = @s, \"EndDate\" = @e WHERE \"TicketId\" = @id",
            ("s", renewStart), ("e", renewEnd), ("id", ticketId));

        var calc = await s.Sp.GetRequiredService<IGateControlService>().CalculateCheckOutAsync(s.Subscriber.Plate);

        Assert.True(calc.TicketExpiredDuringStay);
        Assert.False(calc.IsMonthlyTicket);
        Assert.Equal(e1, calc.ChargeFromUtc);
        Assert.Equal(VisitorFee(s.Rule, s.VehicleTypeId, e1, calc.CheckOutTime), calc.TotalFee);
    }

    [Fact]
    public async Task Legacy_ticket_without_purchases_uses_the_ticket_interval()
    {
        _db.RequireAvailable();
        var s = await CheckedInSubscriberAsync();
        using var spScope = s.Sp;
        var ticketId = s.Subscriber.TicketId!.Value;
        await _db.ExecuteAsync("DELETE FROM \"MonthlyTicketPurchases\" WHERE \"TicketId\" = @t", ("t", ticketId));
        await _data.SetTicketEndAsync(ticketId, DateTime.UtcNow.AddMinutes(-75));
        var end = await _data.TicketEndAsync(ticketId);

        var calc = await s.Sp.GetRequiredService<IGateControlService>().CalculateCheckOutAsync(s.Subscriber.Plate);

        Assert.True(calc.TicketExpiredDuringStay);
        Assert.Equal(end, calc.ChargeFromUtc);
        Assert.Equal(VisitorFee(s.Rule, s.VehicleTypeId, end, calc.CheckOutTime), calc.TotalFee);
    }

    [Fact]
    public async Task Ticket_suspended_during_the_stay_charges_the_whole_stay()
    {
        _db.RequireAvailable();
        var s = await CheckedInSubscriberAsync();
        using var spScope = s.Sp;
        await _data.SetTicketStatusAsync(s.Subscriber.TicketId!.Value, MonthlyTicketStatus.Suspended);

        var calc = await s.Sp.GetRequiredService<IGateControlService>().CalculateCheckOutAsync(s.Subscriber.Plate);

        Assert.True(calc.TicketNoLongerValid);
        Assert.False(calc.TicketExpiredDuringStay);
        Assert.False(calc.IsMonthlyTicket);
        Assert.Equal(VisitorFee(s.Rule, s.VehicleTypeId, s.CheckInUtc, calc.CheckOutTime), calc.TotalFee);
    }

    [Fact]
    public async Task G4_vehicle_moved_to_another_customer_after_check_in_keeps_the_coverage_of_the_owner_at_check_in()
    {
        // Fix round 1, G4: coverage comes from tickets whose customer owned the plate (active CustomerVehicle) AT CHECK-IN
        // time, so a reassignment after check-in (only possible through data drift: A8 blocks removing a vehicle with an
        // active ticket) does not change the fee. Suspension / customer lock during the stay still charge the whole stay (A7).
        _db.RequireAvailable();
        var s = await CheckedInSubscriberAsync();
        using var spScope = s.Sp;
        await _data.DeactivateVehicleAsync(s.Subscriber.CustomerVehicleId);
        var other = await _data.CreateCustomerAsync(isResident: false);
        await _data.AddVehicleAsync(other, s.Subscriber.Plate, s.VehicleTypeId, createdAtUtc: DateTime.UtcNow);

        var calc = await s.Sp.GetRequiredService<IGateControlService>().CalculateCheckOutAsync(s.Subscriber.Plate);

        Assert.True(calc.Success, calc.Message);
        Assert.Equal(0m, calc.TotalFee);
        Assert.True(calc.IsMonthlyTicket);
        Assert.False(calc.TicketNoLongerValid);
    }

    [Fact]
    public async Task G4_vehicle_added_to_the_ticket_owner_only_after_check_in_gives_no_coverage()
    {
        // The owner-at-check-in rule also works the other way round: a vehicle row created after check-in does not
        // retroactively cover the stay (the session was a monthly pass at check-in through the earlier vehicle row).
        _db.RequireAvailable();
        var s = await CheckedInSubscriberAsync();
        using var spScope = s.Sp;
        await _db.ExecuteAsync("UPDATE \"CustomerVehicles\" SET \"CreatedAt\" = now() WHERE \"CustomerVehicleId\" = @id",
            ("id", s.Subscriber.CustomerVehicleId));

        var calc = await s.Sp.GetRequiredService<IGateControlService>().CalculateCheckOutAsync(s.Subscriber.Plate);

        Assert.True(calc.TicketNoLongerValid);
        Assert.False(calc.IsMonthlyTicket);
        Assert.Equal(VisitorFee(s.Rule, s.VehicleTypeId, s.CheckInUtc, calc.CheckOutTime), calc.TotalFee);
    }

    [Fact]
    public async Task Customer_locked_during_the_stay_charges_the_whole_stay()
    {
        _db.RequireAvailable();
        var s = await CheckedInSubscriberAsync();
        using var spScope = s.Sp;
        await _db.ExecuteAsync("UPDATE \"Customers\" SET \"IsActive\" = false WHERE \"CustomerId\" = @c", ("c", s.Subscriber.CustomerId));

        var calc = await s.Sp.GetRequiredService<IGateControlService>().CalculateCheckOutAsync(s.Subscriber.Plate);

        Assert.True(calc.TicketNoLongerValid);
        Assert.Equal(VisitorFee(s.Rule, s.VehicleTypeId, s.CheckInUtc, calc.CheckOutTime), calc.TotalFee);
    }

    [Fact]
    public async Task Cash_checkout_of_a_partially_charged_stay_records_the_fee()
    {
        _db.RequireAvailable();
        var s = await CheckedInSubscriberAsync();
        using var spScope = s.Sp;
        await ExpireCreatePurchaseAtAsync(s.Subscriber.TicketId!.Value, DateTime.UtcNow.AddMinutes(-90));
        await ParkingFlows.OpenShiftAsync(s.Sp, s.UserId);
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var (request, result) = await ParkingFlows.CheckOutCashAsync(s.Sp, s.Subscriber.Plate, s.UserId);

        Assert.True(result.Success, result.Message);
        Assert.True(request.TotalFee > 0);
        var row = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.ParkingCheckOut));
        Assert.Equal(request.TotalFee, AuditDb.Details(row).GetProperty("fee").GetDecimal());
    }
}
