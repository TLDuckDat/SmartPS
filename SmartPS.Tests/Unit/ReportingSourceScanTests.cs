using System.Text.RegularExpressions;

namespace SmartPS.Tests.Unit;

/// <summary>
/// AC-12 / AC-2 / AC-9 / R6 / R7 and plan m3 / m6: the reporting code path has no 100-row cap, no hard-coded 20 slots,
/// no hard-coded vehicle type ids, no in-memory history source, no constructor-triggered load, and the removed
/// Overview KPI API is gone.
/// </summary>
public class ReportingSourceScanTests
{
    private static string ReportsServices => Path.Combine(RepoPaths.Services, "Reports");
    private static string ReportsViewModels => Path.Combine(RepoPaths.ViewModels, "Reports");
    private static string OverviewViewModels => Path.Combine(RepoPaths.ViewModels, "Overview");

    private static IEnumerable<string> ReportingFiles() =>
        new[] { ReportsServices, ReportsViewModels, OverviewViewModels }.SelectMany(RepoPaths.CSharpFiles);

    public static TheoryData<string> ForbiddenPatterns() => new()
    {
        @"Take\(\s*100\s*\)",
        @"GetAllSessionsHistoryAsync",
        @"GetOverviewKpiAsync",
        @"VehicleTypeId\s*==\s*1\b",
        @"VehicleTypeId\s*==\s*2\b",
        @"/\s*20\.0",
        @"slots\.Count\s*>\s*0\s*\?\s*slots\.Count\s*:\s*20",
    };

    [Fact]
    public void Reporting_folders_exist_and_contain_code()
    {
        Assert.True(Directory.Exists(ReportsServices), ReportsServices);
        Assert.NotEmpty(RepoPaths.CSharpFiles(ReportsServices));
        Assert.NotEmpty(RepoPaths.CSharpFiles(ReportsViewModels));
        Assert.NotEmpty(RepoPaths.CSharpFiles(OverviewViewModels));
    }

    [Theory]
    [MemberData(nameof(ForbiddenPatterns))]
    public void Reporting_code_has_no_forbidden_pattern(string pattern)
    {
        var offenders = ReportingFiles()
            .Where(f => Regex.IsMatch(File.ReadAllText(f), pattern))
            .Select(f => Path.GetRelativePath(RepoPaths.Root, f))
            .ToList();

        Assert.True(offenders.Count == 0, $"'{pattern}' found in: {string.Join(", ", offenders)}");
    }

    [Theory]
    [InlineData("Overview", "OverviewViewModel.cs")]
    [InlineData("Reports", "ReportsViewModel.cs")]
    public void M3_view_model_constructors_do_not_start_loading(string folder, string file)
    {
        var path = Path.Combine(RepoPaths.ViewModels, folder, file);
        Assert.True(File.Exists(path), path);

        Assert.DoesNotMatch(new Regex(@"_\s*=\s*LoadDataAsync\s*\(\s*\)"), File.ReadAllText(path));
    }

    [Fact]
    public void Overview_kpi_api_and_model_are_removed()
    {
        Assert.DoesNotContain("GetOverviewKpiAsync", File.ReadAllText(Path.Combine(RepoPaths.Services, "GateControl", "IGateControlService.cs")), StringComparison.Ordinal);
        Assert.DoesNotContain("GetOverviewKpiAsync", File.ReadAllText(Path.Combine(RepoPaths.Services, "GateControl", "GateControlService.cs")), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(RepoPaths.AppProject, "Models", "GateControl", "OverviewKpiData.cs")));
        Assert.False(File.Exists(Path.Combine(ReportsViewModels, "DailyReportItem.cs")));
    }

    [Fact]
    public void Overview_and_reports_view_models_use_the_shared_report_service()
    {
        Assert.Contains("IReportService", File.ReadAllText(Path.Combine(OverviewViewModels, "OverviewViewModel.cs")), StringComparison.Ordinal);
        Assert.Contains("IReportService", File.ReadAllText(Path.Combine(ReportsViewModels, "ReportsViewModel.cs")), StringComparison.Ordinal);
        Assert.DoesNotContain("IGateControlService", File.ReadAllText(Path.Combine(OverviewViewModels, "OverviewViewModel.cs")), StringComparison.Ordinal);
        Assert.DoesNotContain("IGateControlService", File.ReadAllText(Path.Combine(ReportsViewModels, "ReportsViewModel.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public void M6_workbook_writer_never_touches_the_localization_service()
    {
        var writer = Path.Combine(ReportsServices, "ReportWorkbookWriter.cs");
        Assert.True(File.Exists(writer), writer);

        Assert.DoesNotContain("ILocalizationService", File.ReadAllText(writer), StringComparison.Ordinal);
    }

    [Fact]
    public void Report_services_are_registered_in_the_app()
    {
        var app = File.ReadAllText(Path.Combine(RepoPaths.AppProject, "App.xaml.cs"));

        Assert.Contains("IReportService", app, StringComparison.Ordinal);
        Assert.Contains("IReportExportService", app, StringComparison.Ordinal);
        Assert.Contains("IFileDialogService", app, StringComparison.Ordinal);
    }

    [Fact]
    public void Dialog_service_interface_is_unchanged_by_reporting()
    {
        // Task 1 decision 11: the save dialog lives in IFileDialogService, not IDialogService.
        Assert.DoesNotContain("ShowSaveFileDialog", File.ReadAllText(Path.Combine(RepoPaths.Services, "Dialog", "IDialogService.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public void REPORT_EXPORT_is_the_last_audit_action()
    {
        Assert.Equal("REPORT_EXPORT", AuditActions.ReportExport);
        Assert.Equal(AuditActions.ReportExport, AuditActions.All[^1]);
        Assert.Equal(31, AuditActions.All.Count);
    }
}
