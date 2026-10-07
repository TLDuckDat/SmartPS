using System.IO;
using SmartPS.Constants;
using SmartPS.Models.Audit;
using SmartPS.Models.Reports;
using SmartPS.Services.Audit;
using SmartPS.Services.Authorization;
using SmartPS.Services.Localization;

namespace SmartPS.Services.Reports;

/// <summary>
/// Excel export. Order: labels (before the first await) -> Report.Export guard -> lock probe -> queries -> temp file + move ->
/// REPORT_EXPORT audit. No audited transaction is used, so file I/O never runs while the audit lock is held.
/// </summary>
public sealed class ReportExportService : IReportExportService
{
    private const string EntityType = "Report";
    private const int SharingViolation = 32;
    private const int LockViolation = 33;

    private readonly IReportService _reports;
    private readonly IAuthorizationGuard _guard;
    private readonly IAuditService _audit;
    private readonly ILocalizationService _localization;
    private readonly TimeProvider _timeProvider;

    public ReportExportService(
        IReportService reports,
        IAuthorizationGuard guard,
        IAuditService audit,
        ILocalizationService localization,
        TimeProvider? timeProvider = null)
    {
        _reports = reports ?? throw new ArgumentNullException(nameof(reports));
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string GetDefaultFileName(ReportDateRange range) => ReportFileNames.Default(range);

    public async Task<ReportExportResult> ExportAsync(ReportFilter filter, string filePath, CancellationToken cancellationToken = default)
    {
        // Must stay the first statement: localization lookups need the caller's (UI) thread and run before any await.
        var labels = ReportWorkbookLabels.Resolve(_localization);

        ArgumentNullException.ThrowIfNull(filter);
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("A target file path is required.", nameof(filePath));
        }

        if (!string.Equals(Path.GetExtension(filePath), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The target file must have the .xlsx extension.", nameof(filePath));
        }

        var fullPath = Path.GetFullPath(filePath);
        var fileName = Path.GetFileName(fullPath);

        await _guard.DemandAsync(Permissions.ReportExport, EntityType, filter.Range.Key, cancellationToken);

        if (IsLocked(fullPath))
        {
            await LogFailureAsync(filter, fileName, "FileLocked", cancellationToken);
            return Failed(ReportExportStatus.FileLocked);
        }

        var report = await _reports.GetReportAsync(filter, cancellationToken);
        var sessions = await _reports.GetSessionDetailsAsync(filter, ReportLimits.MaxExportSessionRows, cancellationToken);
        report = report with { GeneratedAtUtc = _timeProvider.GetUtcNow().UtcDateTime };

        var tempPath = Path.Combine(Path.GetDirectoryName(fullPath) ?? string.Empty, $"{fileName}.{Guid.NewGuid():N}.tmp");
        ReportExportRowCounts counts;
        try
        {
            counts = await Task.Run(() =>
            {
                using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var written = new ReportWorkbookWriter(labels).Write(stream, report, sessions);
                    stream.Flush(true);
                    return written;
                }
            }, cancellationToken);

            File.Move(tempPath, fullPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(tempPath);
            var reason = IsLockViolation(ex) ? "FileLocked" : "IoError";
            await LogFailureAsync(filter, fileName, reason, cancellationToken);
            return Failed(reason == "FileLocked" ? ReportExportStatus.FileLocked : ReportExportStatus.IoError);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }

        var auditWritten = await _audit.LogAsync(
            new AuditEntry(
                AuditActions.ReportExport,
                AuditOutcome.Success,
                EntityType,
                filter.Range.Key,
                Details(filter, fileName, counts, sessions.IsTruncated, reason: null)),
            cancellationToken);

        return new ReportExportResult(ReportExportStatus.Success, fullPath, counts, sessions.IsTruncated, auditWritten);
    }

    private static ReportExportResult Failed(ReportExportStatus status)
        => new(status, null, new ReportExportRowCounts(0, 0, 0, 0, 0), false, true);

    private Task<bool> LogFailureAsync(ReportFilter filter, string fileName, string reason, CancellationToken cancellationToken)
        => _audit.LogAsync(
            new AuditEntry(
                AuditActions.ReportExport,
                AuditOutcome.Failed,
                EntityType,
                filter.Range.Key,
                Details(filter, fileName, counts: null, truncated: null, reason)),
            cancellationToken);

    private static object Details(ReportFilter filter, string fileName, ReportExportRowCounts? counts, bool? truncated, string? reason)
    {
        var details = new Dictionary<string, object?>
        {
            ["from"] = filter.Range.FromVn.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            ["to"] = filter.Range.ToVn.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            ["preset"] = filter.Preset.ToString(),
            ["vehicleTypeId"] = filter.VehicleTypeId,
            ["customerGroup"] = filter.CustomerGroup.ToString(),
            ["zoneId"] = filter.ZoneId,
            ["fileName"] = fileName
        };

        if (counts is not null)
        {
            details["rowCounts"] = new Dictionary<string, int>
            {
                ["daily"] = counts.Daily,
                ["hourly"] = counts.Hourly,
                ["shifts"] = counts.Shifts,
                ["vehicleTypes"] = counts.VehicleTypes,
                ["sessions"] = counts.Sessions
            };
            details["sessionRowsTruncated"] = truncated ?? false;
        }

        if (reason is not null)
        {
            details["reason"] = reason;
        }

        return details;
    }

    /// <summary>True when the target exists and cannot be opened exclusively (for example it is open in Excel).</summary>
    private static bool IsLocked(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            using var probe = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static bool IsLockViolation(Exception ex)
        => ex is IOException io && ((io.HResult & 0xFFFF) is SharingViolation or LockViolation);

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // best effort
        }
        catch (UnauthorizedAccessException)
        {
            // best effort
        }
    }
}
