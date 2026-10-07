using ClosedXML.Excel;
using Microsoft.Extensions.DependencyInjection;
using SmartPS.Models.Parking;
using SmartPS.Models.Shifts;

namespace SmartPS.Tests.Integration;

/// <summary>
/// R5 / AC-7 / AC-8 / E1 / E6 end to end (DI container as in App.xaml.cs, real PostgreSQL, real audit chain):
/// the exported workbook opens with ClosedXML, has the 6 sheets, its daily net total equals the KPI, and exactly one
/// REPORT_EXPORT row with a valid hash is written; Operator is denied with ACCESS_DENIED and no file.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ReportExportServiceTests : IClassFixture<PostgresDatabaseFixture>, IDisposable
{
    private readonly PostgresDatabaseFixture _db;
    private readonly ReportSeed _seed;
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("smartps-export-it-");

    public ReportExportServiceTests(PostgresDatabaseFixture db)
    {
        _db = db;
        _seed = new ReportSeed(db);
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

    private static DateTime Vn(int y, int mo, int d, int h, int mi = 0) => new DateTime(y, mo, d, h, mi, 0, DateTimeKind.Utc) - AuditTime.VietnamOffset;

    private static ReportFilter Filter(DateOnly from, DateOnly to) => new(new ReportDateRange(from, to), Preset: ReportPeriodPreset.Custom);

    private async Task<ServiceProvider> LoggedInAsync(string role)
    {
        var user = await TestUsers.CreateAsync(_db.Factory, role);
        var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(user.Username);
        return sp;
    }

    private static int Column(IXLWorksheet sheet, string header)
        => sheet.Row(1).CellsUsed().First(c => c.GetString() == header).Address.ColumnNumber;

    [Fact]
    public async Task AC7_manager_export_opens_has_six_sheets_daily_total_equals_kpi_and_is_audited_once()
    {
        _db.RequireAvailable();
        using var sp = await LoggedInAsync("Manager");
        var a = await _seed.PaidSessionAsync(Vn(2031, 5, 3, 8), Vn(2031, 5, 3, 10), 10_000m);
        await _seed.PaidSessionAsync(Vn(2031, 5, 3, 9), Vn(2031, 5, 3, 11), 30_000m, PaymentMethod.VietQR);
        await _seed.PaidSessionAsync(Vn(2031, 5, 5, 23, 30), Vn(2031, 5, 6, 0, 30), 15_000m, PaymentMethod.Card);
        await _seed.SessionAsync(Vn(2031, 5, 6, 7));
        await _seed.SessionAsync(Vn(2031, 5, 7, 7), Vn(2031, 5, 7, 8), totalFee: 0m, paymentMethod: PaymentMethod.Free, customerType: CustomerType.Resident, isMonthlyPass: true);
        await _seed.FinancialAsync(FinancialTransactionType.Refund, PaymentMethod.Cash, -4_000m, Vn(2031, 5, 4, 9), a.SessionId);
        await _seed.FinancialAsync(FinancialTransactionType.Adjustment, PaymentMethod.Cash, 2_500m, Vn(2031, 5, 6, 9));
        var filter = Filter(new DateOnly(2031, 5, 1), new DateOnly(2031, 5, 7));
        var path = Path.Combine(_dir.FullName, "ac7.xlsx");
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var result = await sp.GetRequiredService<IReportExportService>().ExportAsync(filter, path);

        Assert.Equal(ReportExportStatus.Success, result.Status);
        Assert.True(result.AuditWritten);
        Assert.Equal(5, result.RowCounts.Sessions);
        Assert.Equal(7, result.RowCounts.Daily);
        Assert.Empty(_dir.GetFiles("*.tmp"));

        var report = await sp.GetRequiredService<IReportService>().GetReportAsync(filter);
        Assert.Equal(53_500m, report.Kpis.NetRevenue.Current);

        using (var wb = new XLWorkbook(path))
        {
            Assert.Equal(ReportSheetNames.All, wb.Worksheets.Select(w => w.Name).ToArray());
            var daily = wb.Worksheet(ReportSheetNames.Daily);
            var net = Column(daily, "Str_Rpt_Col_NetRevenue");
            var last = daily.LastRowUsed()!.RowNumber();
            Assert.Equal(report.Daily.Count + 2, last);
            Assert.Equal(report.Kpis.NetRevenue.Current, Enumerable.Range(2, report.Daily.Count).Sum(r => daily.Cell(r, net).GetValue<decimal>()));
            Assert.Equal(report.Kpis.NetRevenue.Current, daily.Cell(last, net).GetValue<decimal>());
            Assert.Equal(6, wb.Worksheet(ReportSheetNames.Sessions).LastRowUsed()!.RowNumber()); // header + 5 sessions
            Assert.Equal(25, wb.Worksheet(ReportSheetNames.Hourly).LastRowUsed()!.RowNumber());
        }

        var exports = await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.ReportExport);
        var row = Assert.Single(exports);
        Assert.Equal(AuditOutcome.Success, row.Outcome);
        Assert.Equal("Report", row.EntityType);
        Assert.Equal(filter.Range.Key, row.EntityId);
        AuditDb.HasKeys(row, "from", "to", "preset", "vehicleTypeId", "customerGroup", "zoneId", "rowCounts", "sessionRowsTruncated", "fileName");
        Assert.Equal("ac7.xlsx", AuditDb.String(AuditDb.Details(row), "fileName"));
        Assert.DoesNotContain(_dir.FullName, row.Details, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AuditHashing.ComputeHash(row.PrevHash, row), row.Hash);
        Assert.Empty(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.AccessDenied));
    }

    [Fact]
    public async Task AC8_operator_export_is_denied_audited_and_writes_no_file()
    {
        _db.RequireAvailable();
        using var sp = await LoggedInAsync("Operator");
        var path = Path.Combine(_dir.FullName, "denied.xlsx");
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var ex = await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            sp.GetRequiredService<IReportExportService>().ExportAsync(Filter(new DateOnly(2031, 5, 1), new DateOnly(2031, 5, 7)), path));

        Assert.Equal(new[] { Permissions.ReportExport }, ex.RequiredPermissions);
        Assert.False(File.Exists(path));
        Assert.Empty(_dir.GetFiles());
        var denied = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.AccessDenied));
        Assert.Equal(AuditOutcome.Denied, denied.Outcome);
        Assert.Equal(new[] { "Report.Export" }, AuditDb.StringArray(AuditDb.Details(denied), "requiredPermissions"));
        Assert.Empty(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.ReportExport));
    }

    [Fact]
    public async Task E1_empty_window_still_exports_six_sheets_with_headers()
    {
        _db.RequireAvailable();
        using var sp = await LoggedInAsync("Manager");
        var path = Path.Combine(_dir.FullName, "empty.xlsx");

        var result = await sp.GetRequiredService<IReportExportService>().ExportAsync(Filter(new DateOnly(2031, 12, 1), new DateOnly(2031, 12, 3)), path);

        Assert.Equal(ReportExportStatus.Success, result.Status);
        Assert.Equal(0, result.RowCounts.Sessions);
        using var wb = new XLWorkbook(path);
        Assert.Equal(ReportSheetNames.All, wb.Worksheets.Select(w => w.Name).ToArray());
        Assert.All(wb.Worksheets, ws => Assert.True(ws.Row(1).CellsUsed().Any(), $"{ws.Name} has no header"));
        Assert.Equal(1, wb.Worksheet(ReportSheetNames.Sessions).LastRowUsed()!.RowNumber());
    }

    [Fact]
    public async Task E6_locked_target_returns_FileLocked_and_logs_a_failed_export()
    {
        _db.RequireAvailable();
        using var sp = await LoggedInAsync("Manager");
        var path = Path.Combine(_dir.FullName, "locked.xlsx");
        await File.WriteAllTextAsync(path, "opened in Excel");
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        ReportExportResult result;
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            result = await sp.GetRequiredService<IReportExportService>().ExportAsync(Filter(new DateOnly(2031, 5, 1), new DateOnly(2031, 5, 7)), path);
        }

        Assert.Equal(ReportExportStatus.FileLocked, result.Status);
        Assert.Equal("opened in Excel", await File.ReadAllTextAsync(path));
        Assert.Empty(_dir.GetFiles("*.tmp"));
        var row = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.ReportExport));
        Assert.Equal(AuditOutcome.Failed, row.Outcome);
        Assert.Equal("FileLocked", AuditDb.String(AuditDb.Details(row), "reason"));
        Assert.Equal("locked.xlsx", AuditDb.String(AuditDb.Details(row), "fileName"));
        Assert.Equal(AuditHashing.ComputeHash(row.PrevHash, row), row.Hash);
    }
}
