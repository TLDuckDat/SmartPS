namespace SmartPS.Models.GateControl;

public class OverviewKpiData
{
    public int TotalParkedVehicles { get; set; }
    public int TotalSlots { get; set; } = 20;
    public int AvailableSlots { get; set; } = 20;
    public int OccupiedSlots { get; set; }
    public double OccupancyRate { get; set; }
    public int TodayCheckIns { get; set; }
    public int TodayCheckOuts { get; set; }
    public decimal TodayRevenue { get; set; }
    public int MotorbikeParkedCount { get; set; }
    public int CarParkedCount { get; set; }
    public int MonthlyParkedCount { get; set; }
    public int RegularParkedCount { get; set; }
}
