using SmartPS.Models.Parking;
using SmartPS.Models.Reports;

namespace SmartPS.Services.Reports;

public interface IReportService
{
    Task<ReportFilterOptions> GetFilterOptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Requires <c>Report.View</c>. All aggregation runs in PostgreSQL; day boundaries are Vietnam days.</summary>
    Task<ReportResult> GetReportAsync(ReportFilter filter, CancellationToken cancellationToken = default);

    /// <summary>Requires <c>Report.Export</c>. Returns at most <paramref name="maxRows"/> rows and flags truncation.</summary>
    Task<SessionDetailPage> GetSessionDetailsAsync(
        ReportFilter filter, int maxRows = ReportLimits.MaxExportSessionRows, CancellationToken cancellationToken = default);

    Task<OverviewSnapshot> GetOverviewSnapshotAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ParkingSession>> GetRecentSessionsAsync(int count = 15, CancellationToken cancellationToken = default);
}
