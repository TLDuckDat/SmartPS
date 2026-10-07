namespace SmartPS.Services.Reports;

/// <summary>Worksheet names are fixed Vietnamese names from the spec; only headers follow the UI language.</summary>
public static class ReportSheetNames
{
    public const string Overview = "Tổng quan";
    public const string Daily = "Theo ngày";
    public const string Hourly = "Theo giờ";
    public const string Shifts = "Theo ca";
    public const string VehicleTypes = "Theo loại xe";
    public const string Sessions = "Chi tiết lượt gửi";

    public static IReadOnlyList<string> All { get; } = new[] { Overview, Daily, Hourly, Shifts, VehicleTypes, Sessions };
}
