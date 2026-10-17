using System.Text.Json;
using ClosedXML.Excel;
using SmartPS.Models.Auth;
using SmartPS.Services.Localization;

namespace SmartPS.Tests.Unit;

/// <summary>
/// R5 / AC-7 / AC-8 / E6 and plan §1.5 / §1.7 / m6 with a real AuthorizationGuard + CurrentUserContext, a recording audit
/// service and a fake report service: permission guard first, lock probe, temp file + move, REPORT_EXPORT written after
/// the file exists, file name only in the details, labels resolved synchronously before the first await.
/// </summary>
public sealed class ReportExportServiceUnitTests : IDisposable
{
    private static readonly ReportFilter Filter = new(ReportResults.SampleRange, Preset: ReportPeriodPreset.Custom);

    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("smartps-export-unit-");

    public void Dispose()
    {
        try
        {
            _dir.Delete(recursive: true);
        }
        catch (IOException)
        {
            // best effort
        }
    }

    private string PathFor(string name) => Path.Combine(_dir.FullName, name);

    private sealed record Harness(ReportExportService Service, FakeAuditService Audit, FakeReportService Reports);

    private static Harness Create(User user, ILocalizationService? localization = null)
    {
        var context = new CurrentUserContext();
        context.SetUser(user);
        var audit = new FakeAuditService();
        var guard = new AuthorizationGuard(new PermissionService(context), context, audit);
        var reports = new FakeReportService();
        var service = new ReportExportService(reports, guard, audit, localization ?? new FakeLocalizationService(),
            new FixedTimeProvider(new DateTime(2026, 10, 3, 5, 0, 0, DateTimeKind.Utc)));
        return new Harness(service, audit, reports);
    }

    private static JsonElement Details(AuditEntry entry)
    {
        using var doc = JsonDocument.Parse(AuditDetails.ToCanonicalJson(entry.Details));
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task AC8_without_Report_Export_is_denied_audited_and_writes_nothing()
    {
        var h = Create(TestUsers.Operator());
        var path = PathFor("denied.xlsx");

        var ex = await Assert.ThrowsAsync<PermissionDeniedException>(() => h.Service.ExportAsync(Filter, path));

        Assert.Equal(new[] { Permissions.ReportExport }, ex.RequiredPermissions);
        var denied = Assert.Single(h.Audit.Entries);
        Assert.Equal(AuditActions.AccessDenied, denied.Action);
        Assert.Equal(AuditOutcome.Denied, denied.Outcome);
        Assert.Equal(new[] { "Report.Export" }, Details(denied).GetProperty("requiredPermissions").EnumerateArray().Select(e => e.GetString()).ToArray());
        Assert.DoesNotContain(h.Audit.Entries, e => e.Action == AuditActions.ReportExport);
        Assert.Empty(h.Reports.ReportCalls);
        Assert.Empty(h.Reports.SessionDetailCalls);
        Assert.False(File.Exists(path));
        Assert.Empty(_dir.GetFiles());
    }

    [Fact]
    public async Task E6_target_held_open_returns_FileLocked_leaves_no_tmp_and_logs_a_failed_export()
    {
        var h = Create(TestUsers.Manager());
        var path = PathFor("locked.xlsx");
        var original = new byte[] { 1, 2, 3, 4 };
        await File.WriteAllBytesAsync(path, original);

        ReportExportResult result;
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            result = await h.Service.ExportAsync(Filter, path);
        }

        Assert.Equal(ReportExportStatus.FileLocked, result.Status);
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        Assert.Empty(_dir.GetFiles("*.tmp"));
        Assert.Single(_dir.GetFiles());

        var failed = Assert.Single(h.Audit.Entries, e => e.Action == AuditActions.ReportExport);
        Assert.Equal(AuditOutcome.Failed, failed.Outcome);
        var details = Details(failed);
        Assert.Equal("FileLocked", details.GetProperty("reason").GetString());
        Assert.Equal("locked.xlsx", details.GetProperty("fileName").GetString());
        Assert.DoesNotContain(_dir.FullName, failed.Details is null ? string.Empty : AuditDetails.ToCanonicalJson(failed.Details), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Success_writes_the_file_before_REPORT_EXPORT_and_logs_filter_counts_and_file_name_only()
    {
        var h = Create(TestUsers.Manager());
        var path = PathFor("report.xlsx");
        bool? fileExistedWhenAudited = null;
        h.Audit.OnLog = entry =>
        {
            if (entry.Action == AuditActions.ReportExport)
            {
                fileExistedWhenAudited = File.Exists(path);
            }
        };

        var result = await h.Service.ExportAsync(Filter, path);

        Assert.Equal(ReportExportStatus.Success, result.Status);
        Assert.Equal(path, result.FilePath);
        Assert.True(result.AuditWritten);
        Assert.False(result.SessionRowsTruncated);
        Assert.Equal(h.Reports.SessionPage.Rows.Count, result.RowCounts.Sessions);
        Assert.Equal(ReportResults.SampleRange.DayCount, result.RowCounts.Daily);
        Assert.Equal(24, result.RowCounts.Hourly);
        Assert.True(fileExistedWhenAudited, "REPORT_EXPORT must be logged after the file is written");
        Assert.True(File.Exists(path));
        Assert.Empty(_dir.GetFiles("*.tmp"));

        var (filter, maxRows) = Assert.Single(h.Reports.SessionDetailCalls);
        Assert.Equal(Filter, filter);
        Assert.Equal(ReportLimits.MaxExportSessionRows, maxRows);
        Assert.Equal(Filter, Assert.Single(h.Reports.ReportCalls));

        var entry = Assert.Single(h.Audit.Entries, e => e.Action == AuditActions.ReportExport);
        Assert.Equal(AuditOutcome.Success, entry.Outcome);
        Assert.Equal("Report", entry.EntityType);
        Assert.Equal(Filter.Range.Key, entry.EntityId);
        var details = Details(entry);
        foreach (var key in new[] { "from", "to", "preset", "vehicleTypeId", "customerGroup", "zoneId", "rowCounts", "sessionRowsTruncated", "fileName" })
        {
            Assert.True(details.TryGetProperty(key, out _), $"REPORT_EXPORT details lack '{key}': {details.GetRawText()}");
        }

        foreach (var key in new[] { "daily", "hourly", "shifts", "vehicleTypes", "sessions" })
        {
            Assert.True(details.GetProperty("rowCounts").TryGetProperty(key, out _), $"rowCounts lacks '{key}': {details.GetRawText()}");
        }

        Assert.Equal("report.xlsx", details.GetProperty("fileName").GetString());
        Assert.DoesNotContain(_dir.FullName, details.GetRawText(), StringComparison.OrdinalIgnoreCase);
        var fileName = details.GetProperty("fileName").GetString()!;
        Assert.Equal(Path.GetFileName(fileName), fileName);

        using var wb = new XLWorkbook(path);
        Assert.Equal(ReportSheetNames.All, wb.Worksheets.Select(w => w.Name).ToArray());
    }

    [Fact]
    public async Task Existing_unlocked_target_is_replaced()
    {
        var h = Create(TestUsers.Manager());
        var path = PathFor("replace.xlsx");
        await File.WriteAllTextAsync(path, "old content");

        var result = await h.Service.ExportAsync(Filter, path);

        Assert.Equal(ReportExportStatus.Success, result.Status);
        using var wb = new XLWorkbook(path);
        Assert.Equal(6, wb.Worksheets.Count);
        Assert.Empty(_dir.GetFiles("*.tmp"));
    }

    [Fact]
    public async Task Truncated_session_page_is_reported()
    {
        var h = Create(TestUsers.Manager());
        h.Reports.SessionPage = ReportResults.Sessions(5, truncated: true, totalCount: 100_001);

        var result = await h.Service.ExportAsync(Filter, PathFor("truncated.xlsx"));

        Assert.True(result.SessionRowsTruncated);
        var entry = Assert.Single(h.Audit.Entries, e => e.Action == AuditActions.ReportExport);
        Assert.True(Details(entry).GetProperty("sessionRowsTruncated").GetBoolean());
    }

    [Fact]
    public async Task Audit_not_written_is_surfaced_in_the_result()
    {
        var h = Create(TestUsers.Manager());
        h.Audit.LogResult = false;
        var path = PathFor("noaudit.xlsx");

        var result = await h.Service.ExportAsync(Filter, path);

        Assert.Equal(ReportExportStatus.Success, result.Status);
        Assert.False(result.AuditWritten);
        Assert.True(File.Exists(path));
    }

    [Theory]
    [InlineData("report.csv")]
    [InlineData("report")]
    [InlineData("report.xls")]
    public async Task Non_xlsx_path_is_rejected(string name)
    {
        var h = Create(TestUsers.Manager());

        await Assert.ThrowsAnyAsync<ArgumentException>(() => h.Service.ExportAsync(Filter, PathFor(name)));

        Assert.False(File.Exists(PathFor(name)));
        Assert.DoesNotContain(h.Audit.Entries, e => e.Action == AuditActions.ReportExport && e.Outcome == AuditOutcome.Success);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_path_is_rejected(string path)
    {
        var h = Create(TestUsers.Manager());

        await Assert.ThrowsAnyAsync<ArgumentException>(() => h.Service.ExportAsync(Filter, path));
    }

    [Fact]
    public async Task M6_labels_are_resolved_synchronously_on_the_calling_thread_and_never_by_the_writer()
    {
        var localization = new CountingLocalizationService();
        var h = Create(TestUsers.Manager(), localization);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Reports.ReportGate = gate.Task;
        var callingThread = Environment.CurrentManagedThreadId;

        var export = h.Service.ExportAsync(Filter, PathFor("labels.xlsx"));
        var lookupsBeforeFirstAwait = localization.CallCount;

        Assert.False(export.IsCompleted, "the export should be waiting for the gated report query");
        Assert.True(lookupsBeforeFirstAwait > 0, "labels must be resolved before the first await");
        Assert.Contains(localization.Calls, c => c.Key == "Str_Rpt_Col_NetRevenue");

        gate.SetResult();
        var result = await export;

        Assert.Equal(ReportExportStatus.Success, result.Status);
        Assert.Equal(lookupsBeforeFirstAwait, localization.CallCount);
        Assert.All(localization.Calls, c => Assert.Equal(callingThread, c.ThreadId));
    }

    [Fact]
    public void Default_file_name_follows_the_spec()
    {
        var h = Create(TestUsers.Manager());

        Assert.Equal("BaoCao_SmartPS_20261001-20261003.xlsx", h.Service.GetDefaultFileName(ReportResults.SampleRange));
    }
}
