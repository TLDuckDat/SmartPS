using SmartPS.Models.Parking;
using SmartPS.Services.GateControl;

namespace SmartPS.Tests.Unit;

/// <summary>AC-5 exact numbers and the 4-argument calculator; the 3-argument overload keeps the legacy result.</summary>
public class FeeCalculatorExpirySplitTests
{
    // Motorbike seed rule: 2 h first block 5,000; 2,000 per extra hour; 10,000 overnight.
    private static readonly PricingRule MotoRule = new()
    {
        VehicleTypeId = 1, FirstBlockMinutes = 120, FirstBlockPrice = 5000m, AdditionalPricePerHour = 2000m, OvernightPrice = 10000m
    };

    private static readonly DateTime In = new(2026, 10, 8, 1, 0, 0, DateTimeKind.Utc);     // VN 08:00
    private static readonly DateTime Out = new(2026, 10, 8, 5, 0, 0, DateTimeKind.Utc);    // VN 12:00
    private static readonly DateTime Expiry = new(2026, 10, 8, 3, 0, 0, DateTimeKind.Utc); // VN 10:00

    private static ParkingSession Session(bool monthly, CustomerType? customerType = null) => new()
    {
        SessionId = 1,
        LicensePlate = "51F12345",
        VehicleTypeId = 1,
        CheckInTime = In,
        IsMonthlyPass = monthly,
        CustomerType = monthly ? CustomerType.Resident : CustomerType.Regular,
        Customer = customerType is null ? null : new Customer { CustomerId = 9, FullName = "X", Type = customerType.Value }
    };

    [Fact]
    public void AC5_ticket_expiring_at_10_00_charges_visitor_rate_for_10_00_to_12_00()
    {
        var calc = new StandardParkingFeeCalculator();

        var r = calc.CalculateFee(Session(monthly: true), MotoRule, Out, new MonthlyCoverage(Expiry));

        Assert.Equal(5000m, r.TotalFee);
        Assert.Equal(5000m, r.RawFee);
        Assert.Equal(0, r.DiscountPercentage);
        Assert.False(r.IsMonthlyTicket);
        Assert.Equal(MonthlyChargeReason.ExpiredDuringStay, r.ChargeReason);
        Assert.Equal(Expiry, r.ChargeFromUtc);
        Assert.Equal(Expiry, r.TicketValidUntilUtc);
        Assert.Equal(TimeSpan.FromHours(4), r.Duration);
    }

    [Fact]
    public void Expired_window_gets_no_tier_discount_even_for_a_VIP_customer()
    {
        var r = new StandardParkingFeeCalculator().CalculateFee(Session(monthly: true, CustomerType.VIP), MotoRule, Out, new MonthlyCoverage(Expiry));

        Assert.Equal(5000m, r.TotalFee);
        Assert.Equal(0, r.DiscountPercentage);
    }

    [Fact]
    public void Expiry_after_three_hours_charges_the_extra_hour_block()
    {
        // Window 01:00 → 05:00 with expiry at 01:30: 3.5 h ⇒ 5,000 + 2 × 2,000.
        var r = new StandardParkingFeeCalculator().CalculateFee(Session(monthly: true), MotoRule, Out, new MonthlyCoverage(In.AddMinutes(30)));

        Assert.Equal(9000m, r.TotalFee);
        Assert.Equal(MonthlyChargeReason.ExpiredDuringStay, r.ChargeReason);
    }

    [Fact]
    public void Covered_at_exit_is_free_and_flagged_monthly()
    {
        var r = new StandardParkingFeeCalculator().CalculateFee(Session(monthly: true), MotoRule, Out, new MonthlyCoverage(Out.AddDays(5)));

        Assert.Equal(0m, r.TotalFee);
        Assert.True(r.IsMonthlyTicket);
        Assert.Equal(MonthlyChargeReason.CoveredAtExit, r.ChargeReason);
        Assert.Null(r.ChargeFromUtc);
        Assert.Equal(TimeSpan.FromHours(4), r.Duration);
    }

    [Fact]
    public void Not_covered_charges_the_full_stay_at_visitor_rate_without_discount()
    {
        var r = new StandardParkingFeeCalculator().CalculateFee(Session(monthly: true, CustomerType.VIP), MotoRule, Out, new MonthlyCoverage(null));

        Assert.Equal(9000m, r.TotalFee); // 4 h ⇒ 5,000 + 2 × 2,000
        Assert.Equal(0, r.DiscountPercentage);
        Assert.False(r.IsMonthlyTicket);
        Assert.Equal(MonthlyChargeReason.NotCovered, r.ChargeReason);
    }

    [Fact]
    public void Unknown_coverage_is_free_like_before()
    {
        var r = new StandardParkingFeeCalculator().CalculateFee(Session(monthly: true), MotoRule, Out, null);

        Assert.Equal(0m, r.TotalFee);
        Assert.True(r.IsMonthlyTicket);
        Assert.Equal(MonthlyChargeReason.CoverageUnknown, r.ChargeReason);
    }

    [Fact]
    public void Three_argument_overload_matches_the_legacy_monthly_result()
    {
        var r = new StandardParkingFeeCalculator().CalculateFee(Session(monthly: true), MotoRule, Out);

        Assert.Equal(0m, r.TotalFee);
        Assert.Equal(0m, r.RawFee);
        Assert.Equal(100, r.DiscountPercentage);
        Assert.True(r.IsMonthlyTicket);
        Assert.Equal(TimeSpan.FromHours(4), r.Duration);
    }

    [Fact]
    public void Three_argument_overload_matches_the_legacy_visitor_result_with_tier_discount()
    {
        var calc = new StandardParkingFeeCalculator();

        var r = calc.CalculateFee(Session(monthly: false, CustomerType.VIP), MotoRule, Out);
        var r4 = calc.CalculateFee(Session(monthly: false, CustomerType.VIP), MotoRule, Out, new MonthlyCoverage(Expiry));

        Assert.Equal(9000m, r.RawFee);
        Assert.Equal(20, r.DiscountPercentage);
        Assert.Equal(7200m, r.TotalFee);
        Assert.False(r.IsMonthlyTicket);
        Assert.Equal(MonthlyChargeReason.NotMonthly, r.ChargeReason);
        Assert.Equal(r.TotalFee, r4.TotalFee);
        Assert.Equal(MonthlyChargeReason.NotMonthly, r4.ChargeReason);
    }

    [Fact]
    public void Calculator_does_not_mutate_the_session()
    {
        var session = Session(monthly: true, CustomerType.VIP);

        new StandardParkingFeeCalculator().CalculateFee(session, MotoRule, Out, new MonthlyCoverage(Expiry));

        Assert.Equal(In, session.CheckInTime);
        Assert.True(session.IsMonthlyPass);
        Assert.NotNull(session.Customer);
    }
}
