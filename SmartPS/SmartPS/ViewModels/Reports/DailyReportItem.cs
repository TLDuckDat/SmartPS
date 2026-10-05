namespace SmartPS.ViewModels.Reports;

public class DailyReportItem : ViewModelBase
{
    public string DateDisplay { get; set; } = string.Empty;
    public int TotalCheckIns { get; set; }
    public int TotalCheckOuts { get; set; }
    public decimal Revenue { get; set; }
    public string RevenueFormatted => $"{Revenue:N0} đ";
    public int MotorbikeCount { get; set; }
    public int CarCount { get; set; }
}
