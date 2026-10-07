namespace SmartPS.Models.Reports;

public sealed record ReportResult(
    ReportFilter Filter,
    ReportDateRange PreviousRange,
    DateTime GeneratedAtUtc,
    ReportKpis Kpis,
    IReadOnlyList<DailyReportRow> Daily,
    IReadOnlyList<HourlyReportRow> Hourly,
    IReadOnlyList<ShiftReportRow> Shifts,
    IReadOnlyList<VehicleTypeReportRow> VehicleTypes,
    IReadOnlyList<TopPlateRow> TopPlates,
    IReadOnlyList<ZoneOccupancyRow> Zones,
    CustomerGroupBreakdown CustomerGroups)
{
    /// <summary>False when the period has no traffic and no money movement (drives the empty-state messages).</summary>
    public bool HasActivity =>
        Daily.Any(d => d.CheckIns != 0 || d.CheckOuts != 0 || d.NetRevenue != 0m || d.RefundTotal != 0m || d.AdjustmentTotal != 0m)
        || Kpis.CheckIns.Current != 0m || Kpis.CheckOuts.Current != 0m || Kpis.NetRevenue.Current != 0m
        || Kpis.VehiclesInLotNow != 0 || (Kpis.MonthlyTicketRevenue?.Current ?? 0m) != 0m;
}
