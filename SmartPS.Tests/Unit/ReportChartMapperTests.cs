namespace SmartPS.Tests.Unit;

/// <summary>AC-11 / R3: the six chart data sets are built from the report rows (series data), and E1 empty state.</summary>
public class ReportChartMapperTests
{
    private static readonly ReportResult Result = ReportResults.Sample();

    private static ChartSeriesData Series(CartesianChartData data, string nameKey)
        => Assert.Single(data.Series, s => s.NameKey == nameKey);

    [Fact]
    public void Chart1_revenue_by_day_is_three_stacked_methods_plus_a_net_line()
    {
        var chart = ReportChartMapper.RevenueByDay(Result);

        Assert.Equal(new[] { "01/10", "02/10", "03/10" }, chart.Labels);
        Assert.True(chart.IsCurrency);
        Assert.Equal(4, chart.Series.Count);
        Assert.Equal(3, chart.Series.Count(s => s.Kind == ChartSeriesKind.StackedColumn));
        Assert.Equal(ChartSeriesKind.Line, Series(chart, "Str_Rpt_Series_NetRevenue").Kind);

        Assert.Equal(Result.Daily.Select(d => (double)d.CashRevenue), Series(chart, "Str_Rpt_Series_Cash").Values);
        Assert.Equal(Result.Daily.Select(d => (double)d.VietQrRevenue), Series(chart, "Str_Rpt_Series_VietQr").Values);
        Assert.Equal(Result.Daily.Select(d => (double)d.CardRevenue), Series(chart, "Str_Rpt_Series_Card").Values);
        Assert.Equal(Result.Daily.Select(d => (double)d.NetRevenue), Series(chart, "Str_Rpt_Series_NetRevenue").Values);
        Assert.All(new[] { "Str_Rpt_Series_Cash", "Str_Rpt_Series_VietQr", "Str_Rpt_Series_Card" },
            k => Assert.Equal(ChartSeriesKind.StackedColumn, Series(chart, k).Kind));
        Assert.True(chart.HasData);
    }

    [Fact]
    public void Chart2_hourly_traffic_has_24_labels_and_average_columns()
    {
        var chart = ReportChartMapper.HourlyTraffic(Result);

        Assert.Equal(Enumerable.Range(0, 24).Select(h => h.ToString(System.Globalization.CultureInfo.InvariantCulture)), chart.Labels);
        Assert.False(chart.IsCurrency);
        Assert.Equal(2, chart.Series.Count);
        Assert.All(chart.Series, s => Assert.Equal(ChartSeriesKind.Column, s.Kind));
        Assert.Equal(Result.Hourly.Select(h => h.AverageCheckInsPerDay), Series(chart, "Str_Rpt_Series_AvgCheckIns").Values);
        Assert.Equal(Result.Hourly.Select(h => h.AverageCheckOutsPerDay), Series(chart, "Str_Rpt_Series_AvgCheckOuts").Values);
        Assert.True(chart.HasData);
    }

    [Fact]
    public void Chart3_daily_traffic_has_two_count_lines()
    {
        var chart = ReportChartMapper.DailyTraffic(Result);

        Assert.Equal(new[] { "01/10", "02/10", "03/10" }, chart.Labels);
        Assert.False(chart.IsCurrency);
        Assert.Equal(2, chart.Series.Count);
        Assert.All(chart.Series, s => Assert.Equal(ChartSeriesKind.Line, s.Kind));
        Assert.Equal(new[] { 10.0, 5.0, 0.0 }, Series(chart, "Str_Rpt_Series_CheckIns").Values);
        Assert.Equal(new[] { 8.0, 6.0, 0.0 }, Series(chart, "Str_Rpt_Series_CheckOuts").Values);
    }

    [Fact]
    public void Chart4_customer_groups_are_three_slices_equal_to_the_breakdown()
    {
        var chart = ReportChartMapper.CustomerGroups(Result);

        Assert.Equal(3, chart.Slices.Count);
        Assert.Equal(4.0, Assert.Single(chart.Slices, s => s.NameKey == "Str_Rpt_Group_Resident").Value);
        Assert.Equal(3.0, Assert.Single(chart.Slices, s => s.NameKey == "Str_Rpt_Group_MonthlyPass").Value);
        Assert.Equal(8.0, Assert.Single(chart.Slices, s => s.NameKey == "Str_Rpt_Group_Visitor").Value);
        Assert.True(chart.HasData);
    }

    [Fact]
    public void Chart5_zone_occupancy_occupied_plus_available_equals_total()
    {
        var chart = ReportChartMapper.ZoneOccupancy(Result);

        Assert.Equal(new[] { "Khu A", "Khu B" }, chart.Labels);
        Assert.Equal(2, chart.Series.Count);
        Assert.All(chart.Series, s => Assert.Equal(ChartSeriesKind.StackedRow, s.Kind));
        var occupied = Series(chart, "Str_Rpt_Series_Occupied").Values;
        var available = Series(chart, "Str_Rpt_Series_Available").Values;
        Assert.Equal(new[] { 3.0, 0.0 }, occupied);
        Assert.Equal(new[] { 7.0, 5.0 }, available);
        for (var i = 0; i < Result.Zones.Count; i++)
        {
            Assert.Equal(Result.Zones[i].TotalSlots, occupied[i] + available[i]);
        }
    }

    [Fact]
    public void Chart6_vehicle_type_mix_has_one_slice_per_type_with_check_ins()
    {
        var chart = ReportChartMapper.VehicleTypeMix(Result);

        foreach (var type in Result.VehicleTypes.Where(v => v.CheckIns > 0))
        {
            Assert.Equal(type.CheckIns, Assert.Single(chart.Slices, s => s.Name == type.VehicleTypeName).Value);
        }

        Assert.Equal(Result.VehicleTypes.Sum(v => v.CheckIns), chart.Slices.Sum(s => s.Value));
        Assert.True(chart.HasData);
    }

    [Fact]
    public void E1_empty_result_has_no_data_in_any_chart()
    {
        var empty = ReportResults.Empty();

        Assert.False(ReportChartMapper.RevenueByDay(empty).HasData);
        Assert.False(ReportChartMapper.HourlyTraffic(empty).HasData);
        Assert.False(ReportChartMapper.DailyTraffic(empty).HasData);
        Assert.False(ReportChartMapper.CustomerGroups(empty).HasData);
        Assert.False(ReportChartMapper.ZoneOccupancy(empty).HasData);
        Assert.False(ReportChartMapper.VehicleTypeMix(empty).HasData);
        Assert.False(empty.HasActivity);
        Assert.True(Result.HasActivity);
    }

    [Fact]
    public void E1_empty_result_still_labels_every_day_of_the_range()
    {
        var empty = ReportResults.Empty(new ReportDateRange(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 7)));

        Assert.Equal(7, ReportChartMapper.RevenueByDay(empty).Labels.Count);
        Assert.Equal(7, ReportChartMapper.DailyTraffic(empty).Labels.Count);
        Assert.Equal(24, ReportChartMapper.HourlyTraffic(empty).Labels.Count);
    }

    [Fact]
    public void Series_name_keys_are_report_text_keys()
    {
        var keys = new[]
            {
                ReportChartMapper.RevenueByDay(Result), ReportChartMapper.HourlyTraffic(Result), ReportChartMapper.DailyTraffic(Result),
                ReportChartMapper.ZoneOccupancy(Result)
            }
            .SelectMany(c => c.Series.Select(s => s.NameKey))
            .Concat(ReportChartMapper.CustomerGroups(Result).Slices.Select(s => s.NameKey!));

        Assert.All(keys, k => Assert.Contains(k, ReportTextKeys.All));
    }

    [Fact]
    public void HasData_flags_follow_the_contract()
    {
        Assert.False(new CartesianChartData(new[] { "a" }, new[] { new ChartSeriesData("k", ChartSeriesKind.Line, new[] { 0.0 }) }, false).HasData);
        Assert.True(new CartesianChartData(new[] { "a" }, new[] { new ChartSeriesData("k", ChartSeriesKind.Line, new[] { -1.0 }) }, true).HasData);
        Assert.False(new PieChartData(new[] { new PieSliceData("k", null, 0) }).HasData);
        Assert.True(new PieChartData(new[] { new PieSliceData(null, "n", 2) }).HasData);
    }
}
