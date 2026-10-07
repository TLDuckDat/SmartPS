using SmartPS.Models.Reports;
using SmartPS.Models.Shifts;
using SmartPS.Models.Parking;

namespace SmartPS.Services.Reports;

/// <summary>Sessions counted in one UTC hour (Kind = Utc). The check-out fields describe sessions that ended in that hour.</summary>
public sealed record HourBucket(DateTime HourUtc, int CheckIns, int CheckOuts, int FreeCheckOuts, double CheckOutDurationSeconds);

/// <summary>Financial transactions of one Vietnam day, one method and one type.</summary>
public sealed record FinancialDayBucket(DateOnly DayVn, FinancialTransactionType Type, PaymentMethod Method, decimal Amount);

public sealed record GroupVehicleCount(int VehicleTypeId, ReportCustomerGroup Group, int CheckIns);

public sealed record VehicleCheckOutStat(int VehicleTypeId, int CheckOuts, double DurationSeconds);

public sealed record VehicleRevenue(int VehicleTypeId, decimal NetRevenue);

/// <summary>Everything the pure aggregator needs for one period.</summary>
public sealed record PeriodRawData(
    ReportDateRange Range,
    IReadOnlyList<HourBucket> Hours,
    IReadOnlyList<FinancialDayBucket> Financials,
    IReadOnlyList<GroupVehicleCount> GroupVehicleCounts,
    int InitialOccupancy,
    int Capacity,
    MonthlyTicketSales? MonthlyTickets);

public sealed record PeriodSummary(
    int CheckIns,
    int CheckOuts,
    int FreeCheckOuts,
    double? AverageDurationMinutes,
    decimal Net,
    decimal Cash,
    decimal VietQr,
    decimal Card,
    decimal Refund,
    decimal Adjustment,
    int? PeakHour,
    OccupancyStats Occupancy,
    CustomerGroupBreakdown Groups,
    decimal? ResidentSharePercent,
    MonthlyTicketSales? MonthlyTickets);
