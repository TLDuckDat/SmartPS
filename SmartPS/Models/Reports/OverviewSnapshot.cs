namespace SmartPS.Models.Reports;

public sealed record VehicleTypeCount(int VehicleTypeId, string Name, int Count);

public sealed record OverviewSnapshot(
    DateOnly TodayVn, int TotalSlots, int AvailableSlots, int MaintenanceSlots, int OccupiedNow, double OccupancyPercent,
    int TodayCheckIns, int TodayCheckOuts, decimal TodayNetRevenue, IReadOnlyList<VehicleTypeCount> ParkedByVehicleType,
    int ParkedResidents, int ParkedMonthlyPass, int ParkedVisitors);
