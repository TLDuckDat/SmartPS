using SmartPS.Models.Shifts;

namespace SmartPS.Models.Reports;

public sealed record DailyReportRow(
    DateOnly DayVn, int CheckIns, int CheckOuts, decimal CashRevenue, decimal VietQrRevenue, decimal CardRevenue,
    decimal RefundTotal, decimal AdjustmentTotal, decimal NetRevenue, int FreeCheckOuts, double? AverageDurationMinutes);

public sealed record HourlyReportRow(int HourVn, int CheckIns, int CheckOuts, double AverageCheckInsPerDay, double AverageCheckOutsPerDay);

public sealed record ShiftReportRow(
    int ShiftId, string OpenedByName, DateTime OpenedAtUtc, DateTime? ClosedAtUtc, decimal BeginningCash,
    decimal? ExpectedCash, decimal? ActualCash, decimal? Difference, ShiftStatus Status);

public sealed record VehicleTypeReportRow(
    int VehicleTypeId, string VehicleTypeName, int CheckIns, int CheckOuts, decimal NetRevenue, double? AverageDurationMinutes);

public sealed record TopPlateRow(int Rank, string LicensePlate, int Visits, DateTime LastCheckInUtc);

public sealed record ZoneOccupancyRow(int ZoneId, string ZoneName, int OccupiedSlots, int TotalSlots)
{
    public double? OccupancyPercent => TotalSlots == 0 ? null : Math.Round(OccupiedSlots * 100.0 / TotalSlots, 1);
}
