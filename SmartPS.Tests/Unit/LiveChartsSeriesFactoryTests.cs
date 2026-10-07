using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using SmartPS.ViewModels.Reports;

namespace SmartPS.Tests.Unit;

/// <summary>AC-11 / R3: chart DTOs become concrete LiveCharts2 series with the mapper's values, localized names and category labels.</summary>
public class LiveChartsSeriesFactoryTests
{
    private static readonly ReportResult Result = ReportResults.Sample();

    private static string Localize(string key) => "L(" + key + ")";

    [Fact]
    public void Revenue_chart_becomes_three_stacked_columns_and_one_line()
    {
        var data = ReportChartMapper.RevenueByDay(Result);

        var series = LiveChartsSeriesFactory.CreateCartesian(data, Localize);

        Assert.Equal(data.Series.Count, series.Length);
        for (var i = 0; i < data.Series.Count; i++)
        {
            var expected = data.Series[i];
            var actual = series[i];
            Assert.Equal(Localize(expected.NameKey), actual.Name);
            switch (expected.Kind)
            {
                case ChartSeriesKind.StackedColumn:
                    Assert.Equal(expected.Values, Assert.IsType<StackedColumnSeries<double>>(actual).Values!);
                    break;
                case ChartSeriesKind.Line:
                    Assert.Equal(expected.Values, Assert.IsType<LineSeries<double>>(actual).Values!);
                    break;
                default:
                    Assert.Fail($"unexpected kind {expected.Kind} in the revenue chart");
                    break;
            }
        }

        Assert.Equal(3, series.Count(s => s is StackedColumnSeries<double>));
        Assert.Single(series, s => s is LineSeries<double>);
    }

    [Fact]
    public void Hourly_chart_becomes_two_column_series()
    {
        var data = ReportChartMapper.HourlyTraffic(Result);

        var series = LiveChartsSeriesFactory.CreateCartesian(data, Localize);

        Assert.Equal(2, series.Length);
        for (var i = 0; i < series.Length; i++)
        {
            var column = Assert.IsType<ColumnSeries<double>>(series[i]);
            Assert.Equal(data.Series[i].Values, column.Values!);
            Assert.Equal(Localize(data.Series[i].NameKey), column.Name);
        }
    }

    [Fact]
    public void Daily_traffic_chart_becomes_two_line_series()
    {
        var data = ReportChartMapper.DailyTraffic(Result);

        var series = LiveChartsSeriesFactory.CreateCartesian(data, Localize);

        Assert.Equal(2, series.Length);
        Assert.All(series, s => Assert.IsType<LineSeries<double>>(s));
        Assert.Equal(data.Series[0].Values, ((LineSeries<double>)series[0]).Values!);
        Assert.Equal(data.Series[1].Values, ((LineSeries<double>)series[1]).Values!);
    }

    [Fact]
    public void Zone_chart_becomes_two_stacked_row_series()
    {
        var data = ReportChartMapper.ZoneOccupancy(Result);

        var series = LiveChartsSeriesFactory.CreateCartesian(data, Localize);

        Assert.Equal(2, series.Length);
        for (var i = 0; i < series.Length; i++)
        {
            var row = Assert.IsType<StackedRowSeries<double>>(series[i]);
            Assert.Equal(data.Series[i].Values, row.Values!);
        }
    }

    [Fact]
    public void Category_axes_carry_the_mapper_labels()
    {
        var revenue = ReportChartMapper.RevenueByDay(Result);
        var hourly = ReportChartMapper.HourlyTraffic(Result);
        var zones = ReportChartMapper.ZoneOccupancy(Result);

        Assert.Equal(revenue.Labels, Assert.Single(LiveChartsSeriesFactory.CreateCategoryAxes(revenue)).Labels!);
        Assert.Equal(hourly.Labels, Assert.Single(LiveChartsSeriesFactory.CreateCategoryAxes(hourly)).Labels!);
        Assert.Equal(zones.Labels, Assert.Single(LiveChartsSeriesFactory.CreateCategoryAxes(zones)).Labels!);
    }

    [Fact]
    public void Value_axes_exist_for_currency_and_counts()
    {
        Assert.NotEmpty(LiveChartsSeriesFactory.CreateValueAxes(true));
        Assert.NotEmpty(LiveChartsSeriesFactory.CreateValueAxes(false));
    }

    [Fact]
    public void Customer_group_pie_has_one_pie_series_per_slice_with_localized_names()
    {
        var data = ReportChartMapper.CustomerGroups(Result);

        var series = LiveChartsSeriesFactory.CreatePie(data, Localize);

        Assert.Equal(data.Slices.Count, series.Length);
        for (var i = 0; i < series.Length; i++)
        {
            var pie = Assert.IsType<PieSeries<double>>(series[i]);
            Assert.Equal(Localize(data.Slices[i].NameKey!), pie.Name);
            Assert.Equal(new[] { data.Slices[i].Value }, pie.Values!);
        }
    }

    [Fact]
    public void Vehicle_type_pie_uses_the_database_names_as_is()
    {
        var data = ReportChartMapper.VehicleTypeMix(Result);

        var series = LiveChartsSeriesFactory.CreatePie(data, Localize);

        Assert.Equal(data.Slices.Count, series.Length);
        for (var i = 0; i < series.Length; i++)
        {
            var pie = Assert.IsType<PieSeries<double>>(series[i]);
            var slice = data.Slices[i];
            Assert.Equal(slice.NameKey is null ? slice.Name : Localize(slice.NameKey), pie.Name);
            Assert.Equal(new[] { slice.Value }, pie.Values!);
        }

        Assert.Contains(series, s => s.Name == "Xe máy");
    }

    [Fact]
    public void Empty_chart_data_gives_series_without_failing()
    {
        var empty = ReportResults.Empty();

        Assert.NotNull(LiveChartsSeriesFactory.CreateCartesian(ReportChartMapper.RevenueByDay(empty), Localize));
        Assert.NotNull(LiveChartsSeriesFactory.CreatePie(ReportChartMapper.CustomerGroups(empty), Localize));
        Assert.NotNull(LiveChartsSeriesFactory.CreateCartesian(ReportChartMapper.ZoneOccupancy(empty), Localize));
    }
}
