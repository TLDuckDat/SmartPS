using System.Diagnostics;
using ClosedXML.Excel;
using Microsoft.Extensions.DependencyInjection;

namespace SmartPS.Tests.Performance;

/// <summary>
/// AC-10 / N1: with 50,000 sessions and 50,000 FinancialTransactions over 30 VN days, the report loads in ≤ 3 s and
/// the Excel export (6 sheets, 50,000 detail rows) finishes in ≤ 15 s. Run with <c>--filter "Category=Performance"</c>.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Performance")]
public sealed class ReportPerformanceTests : IClassFixture<PostgresDatabaseFixture>, IDisposable
{
    private const int Sessions = 50_000;
    private const int Financials = 50_000;
    private static readonly TimeSpan ReportBudget = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ExportBudget = TimeSpan.FromSeconds(15);
    private static readonly DateOnly StartVn = new(2031, 1, 1);

    private readonly PostgresDatabaseFixture _db;
    private readonly ITestOutputHelper _output;
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("smartps-report-perf-");

    public ReportPerformanceTests(PostgresDatabaseFixture db, ITestOutputHelper output)
    {
        _db = db;
        _output = output;
    }

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

    [Fact]
    public async Task AC10_report_within_3s_and_export_within_15s_for_50k_sessions_and_50k_transactions()
    {
        _db.RequireAvailable();
        var seedClock = Stopwatch.StartNew();
        var seed = await ReportSeed.SeedPerformanceAsync(_db, StartVn, days: 30, sessions: Sessions, financials: Financials);
        _output.WriteLine($"seed: {seed.Sessions} sessions ({seed.CompletedSessions} completed), {seed.FinancialTransactions} transactions in {seedClock.Elapsed.TotalSeconds:F1} s");
        Assert.Equal(Sessions, seed.Sessions);
        Assert.Equal(Financials, seed.FinancialTransactions);

        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAdminAsync();
        var reports = sp.GetRequiredService<IReportService>();
        var export = sp.GetRequiredService<IReportExportService>();
        var filter = new ReportFilter(seed.Range, Preset: ReportPeriodPreset.Custom);

        // warm-up (connection pool, EF model, query plans) on a single day
        await reports.GetReportAsync(new ReportFilter(new ReportDateRange(StartVn, StartVn)));

        var reportClock = Stopwatch.StartNew();
        var result = await reports.GetReportAsync(filter);
        reportClock.Stop();
        _output.WriteLine($"GetReportAsync over {seed.Range.DayCount} days: {reportClock.ElapsedMilliseconds} ms");

        Assert.Equal(Sessions, (int)result.Kpis.CheckIns.Current);
        Assert.Equal(seed.Range.DayCount, result.Daily.Count);
        Assert.True(reportClock.Elapsed <= ReportBudget, $"report took {reportClock.ElapsedMilliseconds} ms (budget {ReportBudget.TotalMilliseconds} ms)");

        var path = Path.Combine(_dir.FullName, ReportFileNames.Default(seed.Range));
        var exportClock = Stopwatch.StartNew();
        var exported = await export.ExportAsync(filter, path);
        exportClock.Stop();
        _output.WriteLine($"ExportAsync: {exportClock.ElapsedMilliseconds} ms, file {new FileInfo(path).Length / 1024} KiB");

        Assert.Equal(ReportExportStatus.Success, exported.Status);
        Assert.Equal(Sessions, exported.RowCounts.Sessions);
        Assert.False(exported.SessionRowsTruncated);
        Assert.True(exportClock.Elapsed <= ExportBudget, $"export took {exportClock.ElapsedMilliseconds} ms (budget {ExportBudget.TotalMilliseconds} ms)");

        using var wb = new XLWorkbook(path);
        Assert.Equal(ReportSheetNames.All, wb.Worksheets.Select(w => w.Name).ToArray());
        Assert.Equal(Sessions + 1, wb.Worksheet(ReportSheetNames.Sessions).LastRowUsed()!.RowNumber());
    }
}
