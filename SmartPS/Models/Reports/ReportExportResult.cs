namespace SmartPS.Models.Reports;

public sealed record ReportExportRowCounts(int Daily, int Hourly, int Shifts, int VehicleTypes, int Sessions);

public sealed record ReportExportResult(
    ReportExportStatus Status, string? FilePath, ReportExportRowCounts RowCounts, bool SessionRowsTruncated, bool AuditWritten);
