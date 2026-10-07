using SmartPS.Services.GateControl;

namespace SmartPS.Tests.Unit;

/// <summary>R15 / decision 1: every row of the plan §1.6 table.</summary>
public class MonthlyFeeSplitPolicyTests
{
    private static readonly DateTime In = new(2026, 10, 8, 1, 0, 0, DateTimeKind.Utc);   // VN 08:00
    private static readonly DateTime Out = new(2026, 10, 8, 5, 0, 0, DateTimeKind.Utc);  // VN 12:00

    [Fact]
    public void Not_monthly_charges_the_full_stay_whatever_the_coverage()
    {
        foreach (var coverage in new MonthlyCoverage?[] { null, new(null), new(Out.AddDays(1)), new(In.AddHours(2)) })
        {
            var w = MonthlyFeeSplitPolicy.Resolve(In, Out, isMonthlyPass: false, coverage);

            Assert.Equal(MonthlyChargeReason.NotMonthly, w.Reason);
            Assert.False(w.IsFree);
        }
    }

    [Fact]
    public void Unknown_coverage_is_free_legacy_offline_behaviour()
    {
        var w = MonthlyFeeSplitPolicy.Resolve(In, Out, isMonthlyPass: true, coverage: null);

        Assert.Equal(MonthlyChargeReason.CoverageUnknown, w.Reason);
        Assert.True(w.IsFree);
        Assert.Null(w.ChargeFromUtc);
    }

    [Fact]
    public void No_covering_period_charges_the_full_stay()
    {
        var w = MonthlyFeeSplitPolicy.Resolve(In, Out, isMonthlyPass: true, new MonthlyCoverage(null));

        Assert.Equal(MonthlyChargeReason.NotCovered, w.Reason);
        Assert.False(w.IsFree);
        Assert.True(w.ChargeFromUtc is null || w.ChargeFromUtc == In, $"ChargeFromUtc = {w.ChargeFromUtc:o}");
        Assert.Null(w.TicketValidUntilUtc);
    }

    [Fact]
    public void Covered_past_checkout_is_free()
    {
        var until = Out.AddDays(10);
        var w = MonthlyFeeSplitPolicy.Resolve(In, Out, isMonthlyPass: true, new MonthlyCoverage(until));

        Assert.Equal(MonthlyChargeReason.CoveredAtExit, w.Reason);
        Assert.True(w.IsFree);
        Assert.Null(w.ChargeFromUtc);
        Assert.Equal(until, w.TicketValidUntilUtc);
    }

    [Fact]
    public void AC5_expiry_during_stay_charges_from_expiry()
    {
        var until = new DateTime(2026, 10, 8, 3, 0, 0, DateTimeKind.Utc); // VN 10:00
        var w = MonthlyFeeSplitPolicy.Resolve(In, Out, isMonthlyPass: true, new MonthlyCoverage(until));

        Assert.Equal(MonthlyChargeReason.ExpiredDuringStay, w.Reason);
        Assert.False(w.IsFree);
        Assert.Equal(until, w.ChargeFromUtc);
        Assert.Equal(until, w.TicketValidUntilUtc);
    }

    [Fact]
    public void Expiry_exactly_at_checkout_is_expired_during_stay()
    {
        var w = MonthlyFeeSplitPolicy.Resolve(In, Out, isMonthlyPass: true, new MonthlyCoverage(Out));

        Assert.Equal(MonthlyChargeReason.ExpiredDuringStay, w.Reason);
        Assert.Equal(Out, w.ChargeFromUtc);
    }

    [Fact]
    public void Expiry_exactly_at_checkin_is_not_covered()
    {
        var w = MonthlyFeeSplitPolicy.Resolve(In, Out, isMonthlyPass: true, new MonthlyCoverage(In));

        Assert.Equal(MonthlyChargeReason.NotCovered, w.Reason);
        Assert.False(w.IsFree);
    }

    [Fact]
    public void Expiry_before_checkin_is_not_covered()
    {
        var w = MonthlyFeeSplitPolicy.Resolve(In, Out, isMonthlyPass: true, new MonthlyCoverage(In.AddDays(-1)));

        Assert.Equal(MonthlyChargeReason.NotCovered, w.Reason);
        Assert.False(w.IsFree);
    }
}
