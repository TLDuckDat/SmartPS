using System.Globalization;
using SmartPS.Models.Reports;

namespace SmartPS.Services.Reports;

/// <summary>Turns report rows into chart data (series data only; LiveCharts objects are built in the view model layer).</summary>
public static class ReportChartMapper
{
    public static CartesianChartData RevenueByDay(ReportResult r) => new(
        DayLabels(r),
        new[]
        {
            new ChartSeriesData(ReportTextKeys.SeriesCash, ChartSeriesKind.StackedColumn, r.Daily.Select(d => (double)d.CashRevenue).ToList()),
            new ChartSeriesData(ReportTextKeys.SeriesVietQr, ChartSeriesKind.StackedColumn, r.Daily.Select(d => (double)d.VietQrRevenue).ToList()),
            new ChartSeriesData(ReportTextKeys.SeriesCard, ChartSeriesKind.StackedColumn, r.Daily.Select(d => (double)d.CardRevenue).ToList()),
            new ChartSeriesData(ReportTextKeys.SeriesNetRevenue, ChartSeriesKind.Line, r.Daily.Select(d => (double)d.NetRevenue).ToList())
        },
        IsCurrency: true);

    public static CartesianChartData HourlyTraffic(ReportResult r) => new(
        r.Hourly.Select(h => h.HourVn.ToString(CultureInfo.InvariantCulture)).ToList(),
        new[]
        {
            new ChartSeriesData(ReportTextKeys.SeriesAvgCheckIns, ChartSeriesKind.Column, r.Hourly.Select(h => h.AverageCheckInsPerDay).ToList()),
            new ChartSeriesData(ReportTextKeys.SeriesAvgCheckOuts, ChartSeriesKind.Column, r.Hourly.Select(h => h.AverageCheckOutsPerDay).ToList())
        },
        IsCurrency: false);

    public static CartesianChartData DailyTraffic(ReportResult r) => new(
        DayLabels(r),
        new[]
        {
            new ChartSeriesData(ReportTextKeys.SeriesCheckIns, ChartSeriesKind.Line, r.Daily.Select(d => (double)d.CheckIns).ToList()),
            new ChartSeriesData(ReportTextKeys.SeriesCheckOuts, ChartSeriesKind.Line, r.Daily.Select(d => (double)d.CheckOuts).ToList())
        },
        IsCurrency: false);

    public static PieChartData CustomerGroups(ReportResult r) => new(new[]
    {
        new PieSliceData(ReportTextKeys.GroupResident, null, r.CustomerGroups.Resident),
        new PieSliceData(ReportTextKeys.GroupMonthlyPass, null, r.CustomerGroups.MonthlyPass),
        new PieSliceData(ReportTextKeys.GroupVisitor, null, r.CustomerGroups.Visitor)
    });

    public static CartesianChartData ZoneOccupancy(ReportResult r) => new(
        r.Zones.Select(z => z.ZoneName).ToList(),
        new[]
        {
            new ChartSeriesData(ReportTextKeys.SeriesOccupied, ChartSeriesKind.StackedRow, r.Zones.Select(z => (double)z.OccupiedSlots).ToList()),
            new ChartSeriesData(ReportTextKeys.SeriesAvailable, ChartSeriesKind.StackedRow,
                r.Zones.Select(z => (double)Math.Max(z.TotalSlots - z.OccupiedSlots, 0)).ToList())
        },
        IsCurrency: false);

    public static PieChartData VehicleTypeMix(ReportResult r)
        => new(r.VehicleTypes.Where(v => v.CheckIns > 0).Select(v => new PieSliceData(null, v.VehicleTypeName, v.CheckIns)).ToList());

    private static IReadOnlyList<string> DayLabels(ReportResult r) => r.Daily.Select(d => ReportFormat.DayLabel(d.DayVn)).ToList();
}
