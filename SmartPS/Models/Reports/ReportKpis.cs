namespace SmartPS.Models.Reports;

public sealed record ReportKpis(
    KpiComparison CheckIns,
    KpiComparison CheckOuts,
    int VehiclesInLotNow,
    KpiComparison NetRevenue,
    KpiComparison CashRevenue,
    KpiComparison VietQrRevenue,
    KpiComparison CardRevenue,
    decimal RefundTotal,
    decimal AdjustmentTotal,
    KpiComparison FreeCheckOuts,
    KpiComparison? AverageDurationMinutes,
    int? PeakHour,
    int? PreviousPeakHour,
    KpiComparison? AverageOccupancyPercent,
    KpiComparison? PeakOccupancyPercent,
    KpiComparison? ResidentSharePercent,
    decimal? VisitorSharePercent,
    KpiComparison? MonthlyTicketRevenue,
    MonthlyTicketSales? MonthlyTicketSales);
