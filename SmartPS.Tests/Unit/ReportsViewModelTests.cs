using SmartPS.Models.Auth;
using SmartPS.ViewModels.Reports;

namespace SmartPS.Tests.Unit;

/// <summary>
/// R8 / R9 / AC-8 (UI) / AC-5 (tiles) / AC-11 (VM series) and plan §1.11 / m3 / m7: no load in the constructor,
/// debounce with an injected delay (no wall clock), localized errors without crashing, custom range validation,
/// 14 KPI tiles, export command gated by Report.Export and its result dialogs.
/// </summary>
public class ReportsViewModelTests
{
    private static readonly DateTime NowUtc = new(2026, 10, 8, 3, 0, 0, DateTimeKind.Utc); // 10:00 VN on 08/10
    private static readonly DateOnly TodayVn = new(2026, 10, 8);

    private sealed record Harness(
        ReportsViewModel Vm,
        FakeReportService Reports,
        FakeReportExportService Export,
        FakeDialogService Dialogs,
        FakeFileDialogService Files);

    private static Harness Create(User user)
    {
        var context = new CurrentUserContext();
        context.SetUser(user);
        var reports = new FakeReportService();
        var export = new FakeReportExportService();
        var dialogs = new FakeDialogService();
        var files = new FakeFileDialogService();
        var vm = new ReportsViewModel(reports, export, new PermissionService(context), dialogs, files, new FakeLocalizationService(),
            new FixedTimeProvider(NowUtc));
        return new Harness(vm, reports, export, dialogs, files);
    }

    private static async Task<Harness> CreateLoadedAsync(User user)
    {
        var h = Create(user);
        await h.Vm.LoadDataAsync();
        return h;
    }

    private static IEnumerable<string> AllDialogMessages(FakeDialogService d) => d.Successes.Concat(d.Infos).Concat(d.Warnings).Concat(d.Errors);

    [Fact]
    public void M3_constructor_does_not_call_the_service()
    {
        var h = Create(TestUsers.Manager());

        Assert.Empty(h.Reports.ReportCalls);
        Assert.Equal(0, h.Reports.FilterOptionsCalls);
        Assert.Null(h.Vm.CurrentResult);
    }

    [Fact]
    public async Task Load_fills_options_once_then_loads_the_default_last_7_days()
    {
        var h = await CreateLoadedAsync(TestUsers.Manager());

        Assert.Equal(ReportPeriodPreset.Last7Days, h.Vm.SelectedPreset.Preset);
        var filter = Assert.Single(h.Reports.ReportCalls);
        Assert.Equal(new ReportDateRange(new DateOnly(2026, 10, 2), TodayVn), filter.Range);
        Assert.Equal(ReportPeriodPreset.Last7Days, filter.Preset);
        Assert.Null(filter.VehicleTypeId);
        Assert.Null(filter.ZoneId);
        Assert.Equal(ReportCustomerGroup.All, filter.CustomerGroup);
        Assert.Equal(1, h.Reports.FilterOptionsCalls);
        Assert.NotNull(h.Vm.CurrentResult);
        Assert.False(h.Vm.IsLoading);
        Assert.False(h.Vm.HasError);

        // "All" plus one option per vehicle type / zone
        Assert.Contains(h.Vm.VehicleTypeOptions, o => o.Id is null);
        Assert.Equal(new int?[] { 1, 2, 3 }, h.Vm.VehicleTypeOptions.Where(o => o.Id is not null).Select(o => o.Id).ToArray());
        Assert.Contains(h.Vm.ZoneOptions, o => o.Id is null);
        Assert.Equal(2, h.Vm.ZoneOptions.Count(o => o.Id is not null));
        Assert.Equal(7, h.Vm.Presets.Count);
        Assert.Equal(4, h.Vm.CustomerGroupOptions.Count);
    }

    [Fact]
    public async Task Second_load_reuses_the_options_and_reloads_the_report()
    {
        var h = await CreateLoadedAsync(TestUsers.Manager());

        await h.Vm.LoadDataAsync();

        Assert.Equal(1, h.Reports.FilterOptionsCalls);
        Assert.Equal(2, h.Reports.ReportCalls.Count);
    }

    [Fact]
    public async Task AC8_operator_without_Report_Export_cannot_export()
    {
        var h = await CreateLoadedAsync(TestUsers.Operator());

        Assert.False(h.Vm.CanExport);
        Assert.False(h.Vm.ExportCommand.CanExecute(null));
    }

    [Fact]
    public async Task AC8_manager_can_export_after_a_report_is_loaded()
    {
        var h = Create(TestUsers.Manager());
        Assert.False(h.Vm.ExportCommand.CanExecute(null)); // no result yet

        await h.Vm.LoadDataAsync();

        Assert.True(h.Vm.CanExport);
        Assert.True(h.Vm.ExportCommand.CanExecute(null));
    }

    [Fact]
    public async Task R9_rapid_filter_changes_reload_once_with_the_last_filter()
    {
        var h = await CreateLoadedAsync(TestUsers.Manager());
        var delay = new ManualDelay();
        h.Vm.DelayAsync = delay.DelayAsync;
        var callsBefore = h.Reports.ReportCalls.Count;

        h.Vm.SelectedPreset = h.Vm.Presets.First(p => p.Preset == ReportPeriodPreset.Last30Days);
        h.Vm.SelectedVehicleType = h.Vm.VehicleTypeOptions.First(o => o.Id == 2);
        h.Vm.SelectedCustomerGroup = h.Vm.CustomerGroupOptions.First(o => o.Group == ReportCustomerGroup.Resident);

        Assert.Equal(3, delay.Calls.Count);
        Assert.All(delay.Calls, c => Assert.Equal(h.Vm.DebounceDelay, c.Requested));
        Assert.True(delay.Calls[0].IsCancelled, "the first pending reload must be cancelled");
        Assert.True(delay.Calls[1].IsCancelled, "the second pending reload must be cancelled");
        Assert.False(delay.Calls[2].IsCompleted);
        Assert.Equal(callsBefore, h.Reports.ReportCalls.Count);

        var next = h.Reports.NextCallAsync();
        delay.Release(2);
        var filter = await TestWait.WithTimeout(next, "the debounced reload");

        Assert.Equal(callsBefore + 1, h.Reports.ReportCalls.Count);
        Assert.Equal(ReportPeriodCalculator.Resolve(ReportPeriodPreset.Last30Days, TodayVn), filter.Range);
        Assert.Equal(ReportPeriodPreset.Last30Days, filter.Preset);
        Assert.Equal(2, filter.VehicleTypeId);
        Assert.Equal(ReportCustomerGroup.Resident, filter.CustomerGroup);
        Assert.Null(filter.ZoneId);
    }

    [Fact]
    public void Debounce_delay_defaults_to_300_ms()
    {
        var h = Create(TestUsers.Manager());

        Assert.Equal(TimeSpan.FromMilliseconds(ReportLimits.DebounceMilliseconds), h.Vm.DebounceDelay);
        Assert.NotNull(h.Vm.DelayAsync);
    }

    [Fact]
    public async Task Zone_filter_is_passed_to_the_service()
    {
        var h = await CreateLoadedAsync(TestUsers.Manager());
        var delay = new ManualDelay();
        h.Vm.DelayAsync = delay.DelayAsync;

        h.Vm.SelectedZone = h.Vm.ZoneOptions.First(o => o.Id == 2);
        var next = h.Reports.NextCallAsync();
        delay.ReleaseAll();
        var filter = await TestWait.WithTimeout(next, "the zone reload");

        Assert.Equal(2, filter.ZoneId);
    }

    [Fact]
    public async Task R9_database_error_shows_a_localized_message_without_throwing()
    {
        var h = Create(TestUsers.Manager());
        h.Reports.ReportException = new InvalidOperationException("db down");

        await h.Vm.LoadDataAsync();

        Assert.Equal("Msg_Rpt_LoadError", h.Vm.ErrorMessage);
        Assert.True(h.Vm.HasError);
        Assert.False(h.Vm.IsLoading);
    }

    [Fact]
    public async Task Permission_denied_shows_the_permission_message()
    {
        var h = Create(TestUsers.Manager());
        h.Reports.ReportException = new PermissionDeniedException(new[] { Permissions.ReportView }, "x");

        await h.Vm.LoadDataAsync();

        Assert.Equal("Msg_Auth_PermissionDenied", h.Vm.ErrorMessage);
        Assert.True(h.Vm.HasError);
    }

    [Fact]
    public async Task Invalid_custom_range_shows_a_message_and_does_not_query()
    {
        var h = await CreateLoadedAsync(TestUsers.Manager());
        var delay = new ManualDelay();
        h.Vm.DelayAsync = delay.DelayAsync;
        var callsBefore = h.Reports.ReportCalls.Count;

        h.Vm.SelectedPreset = h.Vm.Presets.First(p => p.Preset == ReportPeriodPreset.Custom);
        Assert.True(h.Vm.IsCustomRange);
        h.Vm.CustomFrom = new DateTime(2026, 10, 8);
        h.Vm.CustomTo = new DateTime(2026, 10, 1);
        delay.ReleaseAll();

        await TestWait.UntilAsync(() => h.Vm.ErrorMessage == "Msg_Rpt_InvalidRange", "the invalid-range message is shown");
        Assert.True(h.Vm.HasError);
        Assert.Equal(callsBefore, h.Reports.ReportCalls.Count);
    }

    [Fact]
    public async Task Custom_range_longer_than_366_days_is_rejected_then_a_valid_range_recovers()
    {
        var h = await CreateLoadedAsync(TestUsers.Manager());
        var delay = new ManualDelay();
        h.Vm.DelayAsync = delay.DelayAsync;
        var callsBefore = h.Reports.ReportCalls.Count;

        h.Vm.SelectedPreset = h.Vm.Presets.First(p => p.Preset == ReportPeriodPreset.Custom);
        h.Vm.CustomFrom = new DateTime(2025, 1, 1);
        h.Vm.CustomTo = new DateTime(2026, 10, 8);
        delay.ReleaseAll();

        await TestWait.UntilAsync(() => h.Vm.ErrorMessage == "Msg_Rpt_RangeTooLong", "the range-too-long message is shown");
        Assert.Equal(callsBefore, h.Reports.ReportCalls.Count);

        var next = h.Reports.NextCallAsync();
        h.Vm.CustomFrom = new DateTime(2026, 10, 1);
        h.Vm.CustomTo = new DateTime(2026, 10, 5);
        delay.ReleaseAll();
        var filter = await TestWait.WithTimeout(next, "the reload after a valid custom range");

        Assert.Equal(new ReportDateRange(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 5)), filter.Range);
        Assert.Equal(ReportPeriodPreset.Custom, filter.Preset);
        await TestWait.UntilAsync(() => !h.Vm.HasError, "the error is cleared");
    }

    [Fact]
    public async Task Fourteen_kpi_tiles_in_the_plan_order_with_comparison_text()
    {
        var h = Create(TestUsers.Manager());
        h.Reports.ResultFactory = f => ReportResults.Sample(ReportResults.Cmp(120, 100, 20m, KpiTrend.Up), f);

        await h.Vm.LoadDataAsync();

        Assert.Equal(new[]
        {
            "Str_Rpt_Kpi_CheckIns", "Str_Rpt_Kpi_CheckOuts", "Str_Rpt_Kpi_InLotNow", "Str_Rpt_Kpi_NetRevenue",
            "Str_Rpt_Kpi_Cash", "Str_Rpt_Kpi_VietQr", "Str_Rpt_Kpi_Card", "Str_Rpt_Kpi_FreeCheckOuts",
            "Str_Rpt_Kpi_AvgDuration", "Str_Rpt_Kpi_PeakHour", "Str_Rpt_Kpi_AvgOccupancy", "Str_Rpt_Kpi_PeakOccupancy",
            "Str_Rpt_Kpi_ResidentShare", "Str_Rpt_Kpi_MonthlyTicketRevenue"
        }, h.Vm.KpiTiles.Select(t => t.TitleKey).ToArray());

        KpiTileViewModel Tile(string key) => h.Vm.KpiTiles.Single(t => t.TitleKey == key);
        Assert.Equal("▲20%", Tile("Str_Rpt_Kpi_CheckIns").ChangeText);
        Assert.Equal(KpiTrend.Up, Tile("Str_Rpt_Kpi_CheckIns").Trend);
        Assert.Equal("105.000 ₫", Tile("Str_Rpt_Kpi_NetRevenue").ValueText);
        Assert.Equal("▼12,5%", Tile("Str_Rpt_Kpi_Cash").ChangeText);
        Assert.Equal("—", Tile("Str_Rpt_Kpi_VietQr").ChangeText);
        Assert.Equal(ReportFormat.HourRange(8), Tile("Str_Rpt_Kpi_PeakHour").ValueText);
        Assert.Equal("420.000 ₫", Tile("Str_Rpt_Kpi_MonthlyTicketRevenue").ValueText);
        Assert.Equal("Str_Rpt_Kpi_CheckIns", Tile("Str_Rpt_Kpi_CheckIns").Title);
    }

    [Fact]
    public async Task Charts_and_tables_are_filled_from_the_result()
    {
        var h = await CreateLoadedAsync(TestUsers.Manager());
        var result = h.Vm.CurrentResult!;

        Assert.Equal(4, h.Vm.RevenueSeries.Length);
        Assert.Equal(2, h.Vm.HourlySeries.Length);
        Assert.Equal(2, h.Vm.DailyTrafficSeries.Length);
        Assert.Equal(3, h.Vm.CustomerGroupSeries.Length);
        Assert.Equal(2, h.Vm.ZoneSeries.Length);
        Assert.Equal(ReportChartMapper.VehicleTypeMix(result).Slices.Count, h.Vm.VehicleTypeSeries.Length);
        Assert.NotEmpty(h.Vm.RevenueXAxes);
        Assert.NotEmpty(h.Vm.HourlyXAxes);
        Assert.True(h.Vm.RevenueHasData);
        Assert.True(h.Vm.HourlyHasData);
        Assert.True(h.Vm.DailyTrafficHasData);
        Assert.True(h.Vm.CustomerGroupHasData);
        Assert.True(h.Vm.ZoneHasData);
        Assert.True(h.Vm.VehicleTypeHasData);

        Assert.Equal(result.Daily, h.Vm.DailyRows);
        Assert.Equal(result.Shifts, h.Vm.ShiftRows);
        Assert.Equal(result.VehicleTypes, h.Vm.VehicleTypeRows);
        Assert.Equal(result.TopPlates, h.Vm.TopPlateRows);
        Assert.False(string.IsNullOrWhiteSpace(h.Vm.RangeDisplay));
        Assert.False(string.IsNullOrWhiteSpace(h.Vm.PreviousRangeDisplay));
    }

    [Fact]
    public async Task E1_empty_result_sets_every_chart_empty_flag()
    {
        var h = Create(TestUsers.Manager());
        h.Reports.ResultFactory = f => ReportResults.Empty(f.Range);

        await h.Vm.LoadDataAsync();

        Assert.False(h.Vm.RevenueHasData);
        Assert.False(h.Vm.HourlyHasData);
        Assert.False(h.Vm.DailyTrafficHasData);
        Assert.False(h.Vm.CustomerGroupHasData);
        Assert.False(h.Vm.ZoneHasData);
        Assert.False(h.Vm.VehicleTypeHasData);
        Assert.Equal(14, h.Vm.KpiTiles.Count);
        Assert.False(h.Vm.HasError);
    }

    [Fact]
    public async Task Export_asks_for_a_path_with_the_default_name_and_exports_the_current_filter()
    {
        var h = await CreateLoadedAsync(TestUsers.Manager());
        h.Files.SaveFilePath = @"C:\Reports\custom.xlsx";

        h.Vm.ExportCommand.Execute(null);
        var (filter, path) = await TestWait.WithTimeout(h.Export.ExportReceived.Task, "the export call");

        var dialog = Assert.Single(h.Files.SaveCalls);
        Assert.Equal("BaoCao_SmartPS_20261002-20261008.xlsx", dialog.DefaultFileName);
        Assert.Equal(@"C:\Reports\custom.xlsx", path);
        Assert.Equal(h.Reports.ReportCalls[^1], filter);
        await TestWait.UntilAsync(() => AllDialogMessages(h.Dialogs).Contains("Msg_Rpt_ExportSuccess"), "the success message is shown");
    }

    [Fact]
    public async Task Cancelled_save_dialog_does_not_export()
    {
        var h = await CreateLoadedAsync(TestUsers.Manager());
        h.Files.SaveFilePath = null;

        h.Vm.ExportCommand.Execute(null);
        await TestWait.UntilAsync(() => h.Files.SaveCalls.Count == 1, "the save dialog is shown");
        await TestWait.UntilAsync(() => h.Vm.ExportCommand.CanExecute(null), "the command is idle again");

        Assert.Empty(h.Export.Exports);
        Assert.Empty(AllDialogMessages(h.Dialogs));
    }

    [Theory]
    [InlineData(ReportExportStatus.FileLocked, "Msg_Rpt_ExportFileLocked")]
    [InlineData(ReportExportStatus.IoError, "Msg_Rpt_ExportError")]
    public async Task E6_export_failures_show_a_friendly_error(ReportExportStatus status, string expectedMessage)
    {
        var h = await CreateLoadedAsync(TestUsers.Manager());
        h.Files.SaveFilePath = @"C:\Reports\locked.xlsx";
        h.Export.Status = status;

        h.Vm.ExportCommand.Execute(null);

        await TestWait.UntilAsync(() => h.Dialogs.Errors.Concat(h.Dialogs.Warnings).Contains(expectedMessage), $"{expectedMessage} is shown");
        Assert.DoesNotContain("Msg_Rpt_ExportSuccess", AllDialogMessages(h.Dialogs));
    }

    [Fact]
    public async Task Export_whose_audit_was_not_written_warns()
    {
        var h = await CreateLoadedAsync(TestUsers.Manager());
        h.Files.SaveFilePath = @"C:\Reports\noaudit.xlsx";
        h.Export.AuditWritten = false;

        h.Vm.ExportCommand.Execute(null);

        await TestWait.UntilAsync(() => AllDialogMessages(h.Dialogs).Contains("Msg_Rpt_ExportAuditNotWritten"), "the audit warning is shown");
    }

    [Fact]
    public async Task Export_permission_denied_shows_the_permission_message()
    {
        var h = await CreateLoadedAsync(TestUsers.Manager());
        h.Files.SaveFilePath = @"C:\Reports\denied.xlsx";
        h.Export.ExportException = new PermissionDeniedException(new[] { Permissions.ReportExport }, "x");

        h.Vm.ExportCommand.Execute(null);

        await TestWait.UntilAsync(() => h.Dialogs.Errors.Concat(h.Dialogs.Warnings).Contains("Msg_Auth_PermissionDenied"), "the permission message is shown");
    }
}
