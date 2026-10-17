using System.Globalization;
using System.Text.Json;
using SmartPS.Models.Auth;
using SmartPS.ViewModels.Reports;

namespace SmartPS.Tests.Unit;

/// <summary>
/// Fix round 1, K1 and K5 (pure rules): the range key and default file name always use Gregorian invariant digits
/// (challenge U1 / U1b), and validation rejects ranges that start before 2000-01-01 or end more than 366 days after
/// today (VN) (challenge U3 / V3 / D8).
/// </summary>
public class ReportRangeRegressionTests
{
    private static readonly ReportDateRange Range = new(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 8));
    private static readonly DateOnly Today = new(2026, 10, 8);

    [Theory]
    [InlineData("th-TH")] // Thai Buddhist calendar: 2569
    [InlineData("ar-SA")] // Um Al Qura calendar
    [InlineData("fa-IR")] // Persian calendar
    [InlineData("vi-VN")]
    [InlineData("ja-JP")]
    public void K1_key_and_default_file_name_are_Gregorian_whatever_the_current_culture(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        var savedUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            CultureInfo.CurrentUICulture = new CultureInfo(culture);

            Assert.Equal("20261001-20261008", Range.Key);
            Assert.Equal("BaoCao_SmartPS_20261001-20261008.xlsx", ReportFileNames.Default(Range));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
            CultureInfo.CurrentUICulture = savedUi;
        }
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(1999, 12, 31)]
    public void K5_from_before_2000_is_not_valid(int y, int m, int d)
    {
        var from = new DateOnly(y, m, d);

        Assert.NotEqual(ReportRangeValidation.Valid, ReportPeriodCalculator.Validate(from, from));
        Assert.NotEqual(ReportRangeValidation.Valid, ReportPeriodCalculator.Validate(from, from, Today));
    }

    [Fact]
    public void K5_DateOnly_extremes_are_not_valid()
    {
        Assert.NotEqual(ReportRangeValidation.Valid, ReportPeriodCalculator.Validate(DateOnly.MinValue, DateOnly.MinValue));
        Assert.NotEqual(ReportRangeValidation.Valid, ReportPeriodCalculator.Validate(DateOnly.MinValue, DateOnly.MinValue, Today));
        Assert.NotEqual(ReportRangeValidation.Valid, ReportPeriodCalculator.Validate(DateOnly.MaxValue, DateOnly.MaxValue, Today));
    }

    [Fact]
    public void K5_first_of_January_2000_is_the_earliest_valid_day()
    {
        var first = new DateOnly(2000, 1, 1);

        Assert.Equal(ReportRangeValidation.Valid, ReportPeriodCalculator.Validate(first, first));
        Assert.Equal(ReportRangeValidation.Valid, ReportPeriodCalculator.Validate(first, first, Today));
        Assert.NotEqual(ReportRangeValidation.Valid, ReportPeriodCalculator.Validate(first.AddDays(-1), first, Today));
    }

    [Fact]
    public void K5_to_may_be_at_most_366_days_after_today_VN()
    {
        var limit = Today.AddDays(366);

        Assert.Equal(ReportRangeValidation.Valid, ReportPeriodCalculator.Validate(limit, limit, Today));
        Assert.NotEqual(ReportRangeValidation.Valid, ReportPeriodCalculator.Validate(limit.AddDays(1), limit.AddDays(1), Today));
        // a short range entirely beyond the limit is rejected as out of bounds, not as "too long"
        Assert.NotEqual(ReportRangeValidation.TooLong, ReportPeriodCalculator.Validate(limit.AddDays(10), limit.AddDays(20), Today));
    }

    [Fact]
    public void K5_existing_rules_still_apply_with_today()
    {
        Assert.Equal(ReportRangeValidation.Missing, ReportPeriodCalculator.Validate(null, Today, Today));
        Assert.Equal(ReportRangeValidation.FromAfterTo, ReportPeriodCalculator.Validate(Today, Today.AddDays(-1), Today));
        Assert.Equal(ReportRangeValidation.TooLong, ReportPeriodCalculator.Validate(Today.AddDays(-366), Today, Today));
        Assert.Equal(ReportRangeValidation.Valid, ReportPeriodCalculator.Validate(Today.AddDays(-365), Today, Today));
    }
}

/// <summary>
/// Fix round 1, K2 / K3 / K4 in the export service (challenge X1, X2, X8): a failed export reports whether its Failed
/// row was really written; a read-only target is an I/O error, while a target locked after the probe is "locked";
/// an unexpected writer error is audited as Failed/"Error" before the exception propagates.
/// </summary>
public sealed class ReportExportRegressionTests : IDisposable
{
    private static readonly ReportFilter Filter = new(ReportResults.SampleRange, Preset: ReportPeriodPreset.Custom);

    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("smartps-export-fr1-");

    public void Dispose()
    {
        try
        {
            foreach (var f in _dir.GetFiles("*", SearchOption.AllDirectories))
            {
                f.Attributes = FileAttributes.Normal;
            }

            _dir.Delete(recursive: true);
        }
        catch (IOException)
        {
            // best effort
        }
    }

    private sealed record Harness(ReportExportService Service, FakeAuditService Audit, FakeReportService Reports);

    private static Harness Create(User user)
    {
        var context = new CurrentUserContext();
        context.SetUser(user);
        var audit = new FakeAuditService();
        var guard = new AuthorizationGuard(new PermissionService(context), context, audit);
        var reports = new FakeReportService();
        var service = new ReportExportService(reports, guard, audit, new FakeLocalizationService(),
            new FixedTimeProvider(new DateTime(2026, 10, 3, 5, 0, 0, DateTimeKind.Utc)));
        return new Harness(service, audit, reports);
    }

    private string PathFor(string name) => Path.Combine(_dir.FullName, name);

    private static string? Reason(AuditEntry entry)
    {
        using var doc = JsonDocument.Parse(AuditDetails.ToCanonicalJson(entry.Details));
        return doc.RootElement.TryGetProperty("reason", out var r) ? r.GetString() : null;
    }

    [Fact]
    public async Task K2_locked_export_whose_failed_row_was_not_written_reports_AuditWritten_false()
    {
        var h = Create(TestUsers.Manager());
        h.Audit.LogResult = false;
        var path = PathFor("locked.xlsx");
        await File.WriteAllBytesAsync(path, new byte[] { 1 });

        ReportExportResult result;
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            result = await h.Service.ExportAsync(Filter, path);
        }

        Assert.Equal(ReportExportStatus.FileLocked, result.Status);
        Assert.False(result.AuditWritten);
        Assert.Single(h.Audit.Entries, e => e.Action == AuditActions.ReportExport && e.Outcome == AuditOutcome.Failed);
    }

    [Fact]
    public async Task K2_io_error_export_whose_failed_row_was_not_written_reports_AuditWritten_false()
    {
        var h = Create(TestUsers.Manager());
        h.Audit.LogResult = false;

        var result = await h.Service.ExportAsync(Filter, Path.Combine(_dir.FullName, "missing-folder", "r.xlsx"));

        Assert.Equal(ReportExportStatus.IoError, result.Status);
        Assert.False(result.AuditWritten);
    }

    [Fact]
    public async Task K2_failed_export_with_a_written_failed_row_reports_AuditWritten_true()
    {
        var h = Create(TestUsers.Manager());
        var path = PathFor("locked-ok.xlsx");
        await File.WriteAllBytesAsync(path, new byte[] { 1 });

        ReportExportResult result;
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            result = await h.Service.ExportAsync(Filter, path);
        }

        Assert.Equal(ReportExportStatus.FileLocked, result.Status);
        Assert.True(result.AuditWritten);
    }

    [Fact]
    public async Task K3_read_only_target_is_an_io_error_not_a_lock()
    {
        var h = Create(TestUsers.Manager());
        var path = PathFor("readonly.xlsx");
        var original = new byte[] { 7, 7, 7 };
        await File.WriteAllBytesAsync(path, original);
        File.SetAttributes(path, FileAttributes.ReadOnly);

        var result = await h.Service.ExportAsync(Filter, path);

        Assert.Equal(ReportExportStatus.IoError, result.Status);
        var failed = Assert.Single(h.Audit.Entries, e => e.Action == AuditActions.ReportExport);
        Assert.Equal(AuditOutcome.Failed, failed.Outcome);
        Assert.Equal("IoError", Reason(failed));
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        Assert.Empty(_dir.GetFiles("*.tmp"));
    }

    [Fact]
    public async Task K3_target_opened_by_another_process_after_the_probe_is_reported_as_locked()
    {
        var h = Create(TestUsers.Manager());
        var path = PathFor("race.xlsx");
        await File.WriteAllBytesAsync(path, new byte[] { 9, 9, 9 });
        FileStream? hold = null;
        // The report query runs after the lock probe: open the target there, as Excel would mid-export.
        h.Reports.OnReportCall = _ => hold = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            var result = await h.Service.ExportAsync(Filter, path);

            Assert.NotNull(hold);
            Assert.Equal(ReportExportStatus.FileLocked, result.Status);
            var failed = Assert.Single(h.Audit.Entries, e => e.Action == AuditActions.ReportExport);
            Assert.Equal(AuditOutcome.Failed, failed.Outcome);
            Assert.Equal("FileLocked", Reason(failed));
            Assert.Empty(_dir.GetFiles("*.tmp"));
            Assert.Equal(3, new FileInfo(path).Length);
        }
        finally
        {
            hold?.Dispose();
        }
    }

    [Fact]
    public async Task K4_unexpected_writer_error_is_audited_as_failed_Error_then_rethrown()
    {
        var h = Create(TestUsers.Manager());
        // A malformed result makes the workbook writer fail with a non-I/O exception.
        h.Reports.ResultFactory = f => ReportResults.Sample(filter: f) with { Daily = null! };
        var path = PathFor("broken.xlsx");

        var ex = await Record.ExceptionAsync(() => h.Service.ExportAsync(Filter, path));

        Assert.NotNull(ex);
        Assert.IsNotAssignableFrom<OperationCanceledException>(ex);
        Assert.IsNotAssignableFrom<IOException>(ex);
        var failed = Assert.Single(h.Audit.Entries, e => e.Action == AuditActions.ReportExport);
        Assert.Equal(AuditOutcome.Failed, failed.Outcome);
        Assert.Equal("Error", Reason(failed));
        Assert.Equal(Filter.Range.Key, failed.EntityId);
        Assert.False(File.Exists(path));
        Assert.Empty(_dir.GetFiles("*.tmp"));
    }

    [Fact]
    public async Task K4_cancellation_is_not_audited_as_an_error()
    {
        var h = Create(TestUsers.Manager());
        using var cts = new CancellationTokenSource();
        h.Reports.OnReportCall = _ => cts.Cancel();
        var path = PathFor("cancelled.xlsx");

        var ex = await Record.ExceptionAsync(() => h.Service.ExportAsync(Filter, path, cts.Token));

        Assert.IsAssignableFrom<OperationCanceledException>(ex);
        Assert.DoesNotContain(h.Audit.Entries, e => e.Action == AuditActions.ReportExport && Reason(e) == "Error");
        Assert.False(File.Exists(path));
        Assert.Empty(_dir.GetFiles("*.tmp"));
    }
}

/// <summary>
/// Fix round 1, K2 / K5 / K7 in the Reports view model (challenge V3, V7): failed exports warn when their audit row was
/// not written; out-of-bounds custom dates are rejected without a query; export is disabled while the shown result does
/// not match the selected filter (invalid range or debounced reload pending).
/// </summary>
public class ReportsViewModelRegressionTests
{
    private static readonly DateTime NowUtc = new(2026, 10, 8, 3, 0, 0, DateTimeKind.Utc);

    private sealed record Harness(ReportsViewModel Vm, FakeReportService Reports, FakeReportExportService Export, FakeDialogService Dialogs, FakeFileDialogService Files);

    private static async Task<Harness> CreateLoadedAsync()
    {
        var context = new CurrentUserContext();
        context.SetUser(TestUsers.Manager());
        var reports = new FakeReportService();
        var export = new FakeReportExportService();
        var dialogs = new FakeDialogService();
        var files = new FakeFileDialogService();
        var vm = new ReportsViewModel(reports, export, new PermissionService(context), dialogs, files, new FakeLocalizationService(),
            new FixedTimeProvider(NowUtc));
        await vm.LoadDataAsync();
        return new Harness(vm, reports, export, dialogs, files);
    }

    private static IEnumerable<string> AllDialogMessages(FakeDialogService d) => d.Successes.Concat(d.Infos).Concat(d.Warnings).Concat(d.Errors);

    [Theory]
    [InlineData(ReportExportStatus.FileLocked, "Msg_Rpt_ExportFileLocked")]
    [InlineData(ReportExportStatus.IoError, "Msg_Rpt_ExportError")]
    public async Task K2_failed_export_without_its_audit_row_also_warns(ReportExportStatus status, string failureMessage)
    {
        var h = await CreateLoadedAsync();
        h.Files.SaveFilePath = @"C:\Reports\failed.xlsx";
        h.Export.Status = status;
        h.Export.AuditWritten = false;

        h.Vm.ExportCommand.Execute(null);

        await TestWait.UntilAsync(() => AllDialogMessages(h.Dialogs).Contains(failureMessage), $"{failureMessage} is shown");
        await TestWait.UntilAsync(() => AllDialogMessages(h.Dialogs).Contains("Msg_Rpt_ExportAuditNotWritten"), "the audit warning is shown");
    }

    [Fact]
    public async Task K2_failed_export_with_its_audit_row_does_not_warn_about_audit()
    {
        var h = await CreateLoadedAsync();
        h.Files.SaveFilePath = @"C:\Reports\failed.xlsx";
        h.Export.Status = ReportExportStatus.FileLocked;
        h.Export.AuditWritten = true;

        h.Vm.ExportCommand.Execute(null);
        await TestWait.UntilAsync(() => AllDialogMessages(h.Dialogs).Contains("Msg_Rpt_ExportFileLocked"), "the lock message is shown");

        Assert.DoesNotContain("Msg_Rpt_ExportAuditNotWritten", AllDialogMessages(h.Dialogs));
    }

    public static TheoryData<DateTime, DateTime> OutOfBoundsRanges() => new()
    {
        { DateTime.MinValue, DateTime.MinValue },                    // challenge V3
        { new DateTime(1999, 12, 31), new DateTime(2000, 1, 5) },    // starts before 2000-01-01
        { new DateTime(2027, 10, 20), new DateTime(2027, 10, 25) },  // ends more than 366 days after 08/10/2026
        { DateTime.MaxValue.Date, DateTime.MaxValue.Date },
    };

    [Theory]
    [MemberData(nameof(OutOfBoundsRanges))]
    public async Task K5_out_of_bounds_custom_range_shows_invalid_range_and_queries_nothing(DateTime from, DateTime to)
    {
        var h = await CreateLoadedAsync();
        var delay = new ManualDelay();
        h.Vm.DelayAsync = delay.DelayAsync;
        var callsBefore = h.Reports.ReportCalls.Count;

        h.Vm.SelectedPreset = h.Vm.Presets.First(p => p.Preset == ReportPeriodPreset.Custom);
        h.Vm.CustomFrom = from;
        h.Vm.CustomTo = to;
        delay.ReleaseAll();

        await TestWait.UntilAsync(() => h.Vm.ErrorMessage == "Msg_Rpt_InvalidRange", "the invalid-range message is shown");
        Assert.Equal(callsBefore, h.Reports.ReportCalls.Count);
        Assert.False(h.Vm.IsLoading);
    }

    [Fact]
    public async Task K5_explicit_load_with_an_out_of_bounds_range_queries_nothing()
    {
        var h = await CreateLoadedAsync();
        h.Vm.DelayAsync = (_, ct) => Task.FromCanceled(new CancellationToken(true));
        var callsBefore = h.Reports.ReportCalls.Count;
        h.Vm.SelectedPreset = h.Vm.Presets.First(p => p.Preset == ReportPeriodPreset.Custom);
        h.Vm.CustomFrom = DateTime.MinValue;
        h.Vm.CustomTo = DateTime.MinValue;

        await h.Vm.LoadDataAsync();

        Assert.Equal("Msg_Rpt_InvalidRange", h.Vm.ErrorMessage);
        Assert.Equal(callsBefore, h.Reports.ReportCalls.Count);
    }

    [Fact]
    public async Task K7_invalid_custom_range_disables_export()
    {
        var h = await CreateLoadedAsync();
        Assert.True(h.Vm.ExportCommand.CanExecute(null));
        var delay = new ManualDelay();
        h.Vm.DelayAsync = delay.DelayAsync;

        h.Vm.SelectedPreset = h.Vm.Presets.First(p => p.Preset == ReportPeriodPreset.Custom);
        h.Vm.CustomFrom = new DateTime(2026, 10, 8);
        h.Vm.CustomTo = new DateTime(2026, 10, 1);
        delay.ReleaseAll();
        await TestWait.UntilAsync(() => h.Vm.ErrorMessage == "Msg_Rpt_InvalidRange", "the invalid-range message is shown");

        Assert.False(h.Vm.CanExport);
        Assert.False(h.Vm.ExportCommand.CanExecute(null));

        h.Files.SaveFilePath = @"C:\Reports\stale.xlsx";
        h.Vm.ExportCommand.Execute(null);
        Assert.Empty(h.Files.SaveCalls);
        Assert.Empty(h.Export.Exports);
    }

    [Fact]
    public async Task K7_export_is_disabled_while_a_debounced_reload_is_pending_and_then_uses_the_new_filter()
    {
        var h = await CreateLoadedAsync();
        var delay = new ManualDelay();
        h.Vm.DelayAsync = delay.DelayAsync;
        h.Files.SaveFilePath = @"C:\Reports\pending.xlsx";

        h.Vm.SelectedPreset = h.Vm.Presets.First(p => p.Preset == ReportPeriodPreset.LastMonth); // reload pending (challenge V7)

        Assert.False(h.Vm.CanExport);
        Assert.False(h.Vm.ExportCommand.CanExecute(null));
        h.Vm.ExportCommand.Execute(null);
        Assert.Empty(h.Export.Exports);

        var next = h.Reports.NextCallAsync();
        delay.ReleaseAll();
        await TestWait.WithTimeout(next, "the debounced reload");
        await TestWait.UntilAsync(() => h.Vm.CanExport, "export is enabled again after the reload");

        h.Vm.ExportCommand.Execute(null);
        var (filter, _) = await TestWait.WithTimeout(h.Export.ExportReceived.Task, "the export call");
        Assert.Equal(ReportPeriodPreset.LastMonth, filter.Preset);
        Assert.Equal(new ReportDateRange(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)), filter.Range);
    }
}
