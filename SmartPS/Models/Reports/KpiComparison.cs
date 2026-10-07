namespace SmartPS.Models.Reports;

public sealed record KpiComparison(decimal Current, decimal? Previous, decimal? ChangePercent, KpiTrend Trend);

public sealed record OccupancyStats(double? AveragePercent, double? PeakPercent, int PeakVehicles, DateTime? PeakAtUtc, int Capacity);

public sealed record CustomerGroupBreakdown(int Resident, int MonthlyPass, int Visitor)
{
    public int Total => Resident + MonthlyPass + Visitor;
}
