namespace SmartPS.Models.Reports;

public enum ReportPeriodPreset { Today, Yesterday, Last7Days, Last30Days, ThisMonth, LastMonth, Custom }

/// <summary>Values are sent to SQL as the <c>@grp</c> parameter.</summary>
public enum ReportCustomerGroup { All = 0, Resident = 1, MonthlyPass = 2, Visitor = 3 }

public enum KpiTrend { None = 0, Up = 1, Down = 2, Flat = 3 }

public enum ReportRangeValidation { Valid, Missing, FromAfterTo, TooLong }

public enum ReportExportStatus { Success, FileLocked, IoError }

public enum ChartSeriesKind { StackedColumn, Column, Line, StackedRow }
