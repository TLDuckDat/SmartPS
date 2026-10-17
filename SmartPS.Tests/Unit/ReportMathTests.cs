namespace SmartPS.Tests.Unit;

/// <summary>AC-5, E5, R2 (comparison with the previous period of the same length), N2.</summary>
public class ReportMathTests
{
    [Fact]
    public void AC5_120_vs_100_is_up_20_percent()
    {
        Assert.Equal(20m, ReportMath.ChangePercent(120m, 100m));

        var cmp = ReportMath.Compare(120m, 100m);

        Assert.Equal(120m, cmp.Current);
        Assert.Equal(100m, cmp.Previous);
        Assert.Equal(20m, cmp.ChangePercent);
        Assert.Equal(KpiTrend.Up, cmp.Trend);
        Assert.Equal("▲20%", ReportFormat.Change(cmp));
    }

    [Fact]
    public void Decrease_is_down_with_a_positive_magnitude_in_text()
    {
        var cmp = ReportMath.Compare(80m, 100m);

        Assert.Equal(-20m, cmp.ChangePercent);
        Assert.Equal(KpiTrend.Down, cmp.Trend);
        Assert.Equal("▼20%", ReportFormat.Change(cmp));
    }

    [Fact]
    public void Fractional_change_uses_a_Vietnamese_decimal_comma()
    {
        var cmp = ReportMath.Compare(87.5m, 100m);

        Assert.Equal(-12.5m, cmp.ChangePercent);
        Assert.Equal("▼12,5%", ReportFormat.Change(cmp));
    }

    [Fact]
    public void Equal_values_are_flat_zero_percent()
    {
        var cmp = ReportMath.Compare(100m, 100m);

        Assert.Equal(0m, cmp.ChangePercent);
        Assert.Equal(KpiTrend.Flat, cmp.Trend);
        Assert.Equal("0%", ReportFormat.Change(cmp));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(0)]
    public void E5_previous_zero_gives_no_percentage_and_a_dash(int current)
    {
        Assert.Null(ReportMath.ChangePercent(current, 0m));

        var cmp = ReportMath.Compare(current, 0m);

        Assert.Equal(current, cmp.Current);
        Assert.Null(cmp.ChangePercent);
        Assert.Equal(KpiTrend.None, cmp.Trend);
        Assert.Equal("—", ReportFormat.Change(cmp));
        Assert.Equal(ReportFormat.Dash, ReportFormat.Change(cmp));
    }

    [Fact]
    public void Missing_comparison_is_a_dash()
    {
        Assert.Equal("—", ReportFormat.Change(null));
    }

    [Theory]
    [InlineData(1, 3, -66.7)]
    [InlineData(2, 3, -33.3)]
    [InlineData(4, 3, 33.3)]
    [InlineData(1001, 800, 25.1)]
    public void Change_is_rounded_to_one_decimal(int current, int previous, double expected)
    {
        Assert.Equal((decimal)expected, ReportMath.ChangePercent(current, previous));
    }

    [Fact]
    public void Midpoints_round_away_from_zero()
    {
        // (200.1 - 200) / 200 * 100 = 0.05 exactly -> 0.1 (banker's rounding would give 0.0)
        Assert.Equal(0.1m, ReportMath.ChangePercent(200.1m, 200m));
        Assert.Equal(-0.1m, ReportMath.ChangePercent(199.9m, 200m));
    }

    [Fact]
    public void Nullable_compare_with_missing_current_is_null()
    {
        Assert.Null(ReportMath.Compare((decimal?)null, 10m));
        Assert.Null(ReportMath.Compare((decimal?)null, null));
    }

    [Fact]
    public void Nullable_compare_with_missing_previous_has_no_change()
    {
        var cmp = ReportMath.Compare((decimal?)40m, null);

        Assert.NotNull(cmp);
        Assert.Equal(40m, cmp!.Current);
        Assert.Null(cmp.ChangePercent);
        Assert.Equal(KpiTrend.None, cmp.Trend);
        Assert.Equal("—", ReportFormat.Change(cmp));
    }

    [Fact]
    public void Nullable_compare_with_both_values_behaves_like_the_plain_overload()
    {
        var cmp = ReportMath.Compare((decimal?)120m, 100m);

        Assert.NotNull(cmp);
        Assert.Equal(20m, cmp!.ChangePercent);
        Assert.Equal(KpiTrend.Up, cmp.Trend);
    }

    [Fact]
    public void SharePercent_of_a_whole()
    {
        Assert.Equal(25m, ReportMath.SharePercent(1m, 4m));
        Assert.Equal(100m, ReportMath.SharePercent(9m, 9m));
        Assert.Equal(0m, ReportMath.SharePercent(0m, 9m));
    }

    [Fact]
    public void SharePercent_of_zero_whole_is_null()
    {
        Assert.Null(ReportMath.SharePercent(0m, 0m));
    }
}
