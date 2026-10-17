namespace SmartPS.Tests.Unit;

/// <summary>
/// R2 (average and peak occupancy), N2, plan §1.3 / A7: start from the initial occupancy, sweep each UTC hour in
/// [start, min(end, now)) applying in − out, sample at the end of each hour, percent of capacity.
/// </summary>
public class OccupancyCalculatorTests
{
    private static readonly DateTime T = new(2026, 9, 30, 17, 0, 0, DateTimeKind.Utc); // 00:00 VN on 01/10

    private static HourBucket B(int hourOffset, int checkIns, int checkOuts)
        => new(T.AddHours(hourOffset), checkIns, checkOuts, 0, 0);

    [Fact]
    public void Starting_2_capacity_10_deltas_plus3_minus1_zero_gives_avg_43_3_and_peak_50()
    {
        var buckets = new[] { B(0, 3, 0), B(1, 0, 1), B(2, 0, 0) };

        var stats = OccupancyCalculator.Compute(2, buckets, T, T.AddHours(3), T.AddDays(10), 10);

        Assert.NotNull(stats.AveragePercent);
        Assert.NotNull(stats.PeakPercent);
        Assert.Equal(43.3, stats.AveragePercent!.Value, 1);
        Assert.Equal(50.0, stats.PeakPercent!.Value, 1);
        Assert.Equal(5, stats.PeakVehicles);
        Assert.Equal(10, stats.Capacity);
        Assert.NotNull(stats.PeakAtUtc);
        Assert.InRange(stats.PeakAtUtc!.Value, T, T.AddHours(1));
    }

    [Fact]
    public void Hours_without_a_bucket_keep_the_running_occupancy()
    {
        // only hour 0 has activity; hours 1 and 2 keep 5 vehicles
        var stats = OccupancyCalculator.Compute(2, new[] { B(0, 3, 0) }, T, T.AddHours(3), T.AddDays(10), 10);

        Assert.Equal(50.0, stats.AveragePercent!.Value, 1);
        Assert.Equal(50.0, stats.PeakPercent!.Value, 1);
    }

    [Fact]
    public void Buckets_at_or_after_now_are_ignored()
    {
        var buckets = new[] { B(0, 3, 0), B(1, 0, 1), B(2, 5, 0) };

        var stats = OccupancyCalculator.Compute(2, buckets, T, T.AddHours(3), T.AddHours(2), 10);

        // samples 5 and 4 only
        Assert.Equal(45.0, stats.AveragePercent!.Value, 1);
        Assert.Equal(50.0, stats.PeakPercent!.Value, 1);
        Assert.Equal(5, stats.PeakVehicles);
    }

    [Fact]
    public void Sweep_stops_at_the_end_of_the_range_when_now_is_later()
    {
        var buckets = new[] { B(0, 3, 0), B(1, 0, 1), B(5, 4, 0) };

        var stats = OccupancyCalculator.Compute(2, buckets, T, T.AddHours(2), T.AddDays(1), 10);

        Assert.Equal(45.0, stats.AveragePercent!.Value, 1);
        Assert.Equal(50.0, stats.PeakPercent!.Value, 1);
    }

    [Fact]
    public void Capacity_zero_gives_null_percentages()
    {
        var stats = OccupancyCalculator.Compute(2, new[] { B(0, 3, 0) }, T, T.AddHours(3), T.AddDays(1), 0);

        Assert.Null(stats.AveragePercent);
        Assert.Null(stats.PeakPercent);
        Assert.Equal(0, stats.Capacity);
    }

    [Fact]
    public void Start_at_or_after_now_gives_null_percentages()
    {
        var atNow = OccupancyCalculator.Compute(2, new[] { B(0, 3, 0) }, T, T.AddHours(3), T, 10);
        var future = OccupancyCalculator.Compute(2, new[] { B(0, 3, 0) }, T, T.AddHours(3), T.AddHours(-5), 10);

        Assert.Null(atNow.AveragePercent);
        Assert.Null(atNow.PeakPercent);
        Assert.Null(future.AveragePercent);
        Assert.Null(future.PeakPercent);
    }

    [Fact]
    public void Full_day_with_constant_occupancy_is_flat()
    {
        var stats = OccupancyCalculator.Compute(4, Array.Empty<HourBucket>(), T, T.AddDays(1), T.AddDays(2), 8);

        Assert.Equal(50.0, stats.AveragePercent!.Value, 1);
        Assert.Equal(50.0, stats.PeakPercent!.Value, 1);
        Assert.Equal(4, stats.PeakVehicles);
    }
}
