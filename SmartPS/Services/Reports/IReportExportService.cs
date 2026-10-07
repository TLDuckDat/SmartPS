using SmartPS.Models.Reports;

namespace SmartPS.Services.Reports;

public interface IReportExportService
{
    string GetDefaultFileName(ReportDateRange range);

    /// <summary>
    /// Resolves the labels synchronously first, then demands <c>Report.Export</c>
    /// (<see cref="SmartPS.Services.Authorization.PermissionDeniedException"/> after an ACCESS_DENIED audit),
    /// writes the workbook through a temporary file and finally logs REPORT_EXPORT.
    /// </summary>
    /// <exception cref="ArgumentException">The path is empty or does not end with ".xlsx".</exception>
    Task<ReportExportResult> ExportAsync(ReportFilter filter, string filePath, CancellationToken cancellationToken = default);
}
