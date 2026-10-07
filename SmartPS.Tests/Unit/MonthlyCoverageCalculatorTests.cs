using SmartPS.Services.GateControl;

namespace SmartPS.Tests.Unit;

/// <summary>m8: coverage is the contiguous chain of paid periods starting at the one that contains check-in; a gap ends it.</summary>
public class MonthlyCoverageCalculatorTests
{
    private static readonly DateTime T0 = new(2026, 9, 30, 17, 0, 0, DateTimeKind.Utc); // VN 2026-10-01

    private static CoveragePeriod P(int startDay, int endDay) => new(T0.AddDays(startDay), T0.AddDays(endDay));

    [Fact]
    public void Single_period_containing_checkin_returns_its_end()
    {
        Assert.Equal(T0.AddDays(30), MonthlyCoverageCalculator.ContiguousValidUntil(new[] { P(0, 30) }, T0.AddDays(5)));
    }

    [Fact]
    public void Contiguous_create_and_renew_extend_the_chain()
    {
        var periods = new[] { P(0, 30), P(30, 61), P(61, 92) };

        Assert.Equal(T0.AddDays(92), MonthlyCoverageCalculator.ContiguousValidUntil(periods, T0.AddDays(29)));
    }

    [Fact]
    public void A_gap_stops_the_chain()
    {
        var periods = new[] { P(0, 30), P(31, 61) };

        Assert.Equal(T0.AddDays(30), MonthlyCoverageCalculator.ContiguousValidUntil(periods, T0.AddDays(10)));
    }

    [Fact]
    public void Gap_of_one_tick_still_stops_the_chain()
    {
        var periods = new[] { new CoveragePeriod(T0, T0.AddDays(30)), new CoveragePeriod(T0.AddDays(30).AddTicks(1), T0.AddDays(60)) };

        Assert.Equal(T0.AddDays(30), MonthlyCoverageCalculator.ContiguousValidUntil(periods, T0.AddDays(1)));
    }

    [Fact]
    public void Overlapping_periods_continue_the_chain()
    {
        var periods = new[] { P(0, 30), P(20, 50), P(45, 80) };

        Assert.Equal(T0.AddDays(80), MonthlyCoverageCalculator.ContiguousValidUntil(periods, T0.AddDays(1)));
    }

    [Fact]
    public void A_period_nested_inside_another_does_not_shorten_coverage()
    {
        var periods = new[] { P(0, 60), P(10, 20) };

        Assert.Equal(T0.AddDays(60), MonthlyCoverageCalculator.ContiguousValidUntil(periods, T0.AddDays(15)));
    }

    [Fact]
    public void Unsorted_input_is_handled()
    {
        var periods = new[] { P(61, 92), P(0, 30), P(30, 61) };

        Assert.Equal(T0.AddDays(92), MonthlyCoverageCalculator.ContiguousValidUntil(periods, T0.AddDays(2)));
    }

    [Fact]
    public void Checkin_in_a_later_period_ignores_earlier_ones()
    {
        var periods = new[] { P(0, 30), P(40, 70) };

        Assert.Equal(T0.AddDays(70), MonthlyCoverageCalculator.ContiguousValidUntil(periods, T0.AddDays(45)));
    }

    [Fact]
    public void Checkin_outside_every_period_returns_null()
    {
        var periods = new[] { P(0, 30), P(40, 70) };

        Assert.Null(MonthlyCoverageCalculator.ContiguousValidUntil(periods, T0.AddDays(35)));
        Assert.Null(MonthlyCoverageCalculator.ContiguousValidUntil(periods, T0.AddDays(-1)));
        Assert.Null(MonthlyCoverageCalculator.ContiguousValidUntil(Array.Empty<CoveragePeriod>(), T0));
    }

    [Fact]
    public void End_is_exclusive_and_start_inclusive()
    {
        var periods = new[] { P(0, 30) };

        Assert.Null(MonthlyCoverageCalculator.ContiguousValidUntil(periods, T0.AddDays(30)));
        Assert.Equal(T0.AddDays(30), MonthlyCoverageCalculator.ContiguousValidUntil(periods, T0));
    }
}
