namespace SmartPS.Tests.TestSupport;

/// <summary>Records export requests for view-model tests and returns <see cref="Result"/> (default: Success, audit written).</summary>
public sealed class FakeReportExportService : IReportExportService
{
    public List<(ReportFilter Filter, string FilePath)> Exports { get; } = new();

    public TaskCompletionSource<(ReportFilter Filter, string FilePath)> ExportReceived { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ReportExportStatus Status { get; set; } = ReportExportStatus.Success;

    public bool AuditWritten { get; set; } = true;

    public Exception? ExportException { get; set; }

    public string GetDefaultFileName(ReportDateRange range) => ReportFileNames.Default(range);

    public Task<ReportExportResult> ExportAsync(ReportFilter filter, string filePath, CancellationToken cancellationToken = default)
    {
        Exports.Add((filter, filePath));
        ExportReceived.TrySetResult((filter, filePath));
        if (ExportException is not null)
        {
            return Task.FromException<ReportExportResult>(ExportException);
        }

        return Task.FromResult(new ReportExportResult(
            Status,
            Status == ReportExportStatus.Success ? filePath : null,
            new ReportExportRowCounts(7, 24, 1, 3, 10),
            SessionRowsTruncated: false,
            AuditWritten: AuditWritten));
    }
}
