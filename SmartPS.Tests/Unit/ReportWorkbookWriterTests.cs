using ClosedXML.Excel;

namespace SmartPS.Tests.Unit;

/// <summary>
/// R5 / AC-7 / E1: the workbook writer (ClosedXML, no DB, no WPF) produces the 6 spec sheets with bold frozen headers,
/// ₫ money format, VN date-times, a daily total equal to the net-revenue KPI, and a truncation note when applicable.
/// Labels come from <see cref="ReportWorkbookLabels.FromDictionary"/>; a missing label falls back to its key.
/// </summary>
public class ReportWorkbookWriterTests
{
    private static (XLWorkbook Workbook, ReportExportRowCounts Counts) Write(ReportResult result, SessionDetailPage sessions,
        IReadOnlyDictionary<string, string>? labels = null)
    {
        var writer = new ReportWorkbookWriter(ReportWorkbookLabels.FromDictionary(labels ?? new Dictionary<string, string>()));
        using var output = new MemoryStream();
        var counts = writer.Write(output, result, sessions);
        var bytes = output.ToArray();
        Assert.NotEmpty(bytes);
        return (new XLWorkbook(new MemoryStream(bytes)), counts);
    }

    private static int Column(IXLWorksheet sheet, string header)
    {
        var cell = sheet.Row(1).CellsUsed().FirstOrDefault(c => c.GetString() == header);
        Assert.True(cell is not null, $"sheet '{sheet.Name}' has no header '{header}' (headers: {string.Join(" | ", sheet.Row(1).CellsUsed().Select(c => c.GetString()))})");
        return cell!.Address.ColumnNumber;
    }

    private static int LastRow(IXLWorksheet sheet) => sheet.LastRowUsed()?.RowNumber() ?? 0;

    private static IEnumerable<IXLCell> AllCells(IXLWorksheet sheet) => sheet.CellsUsed();

    [Fact]
    public void Workbook_has_the_six_spec_sheets_in_order()
    {
        var (wb, _) = Write(ReportResults.Sample(), ReportResults.Sessions());

        Assert.Equal(ReportSheetNames.All, wb.Worksheets.Select(w => w.Name).ToArray());
        Assert.Equal(6, wb.Worksheets.Count);
    }

    [Fact]
    public void Every_sheet_has_a_bold_frozen_first_row()
    {
        var (wb, _) = Write(ReportResults.Sample(), ReportResults.Sessions());

        foreach (var sheet in wb.Worksheets)
        {
            Assert.True(sheet.Row(1).CellsUsed().Any(), $"{sheet.Name} has an empty first row");
            Assert.All(sheet.Row(1).CellsUsed(), c => Assert.True(c.Style.Font.Bold, $"{sheet.Name}!{c.Address} is not bold"));
            Assert.Equal(1, sheet.SheetView.SplitRow);
        }
    }

    [Fact]
    public void Daily_sheet_has_one_row_per_day_plus_a_total_equal_to_the_net_revenue_kpi()
    {
        var result = ReportResults.Sample();
        var (wb, counts) = Write(result, ReportResults.Sessions());
        var sheet = wb.Worksheet(ReportSheetNames.Daily);
        var net = Column(sheet, "Str_Rpt_Col_NetRevenue");
        var last = LastRow(sheet);

        Assert.Equal(result.Daily.Count + 2, last); // header + days + total
        var dataSum = Enumerable.Range(2, result.Daily.Count).Sum(r => sheet.Cell(r, net).GetValue<decimal>());
        Assert.Equal(result.Kpis.NetRevenue.Current, dataSum);
        Assert.Equal(result.Kpis.NetRevenue.Current, sheet.Cell(last, net).GetValue<decimal>());
        Assert.Contains(sheet.Row(last).CellsUsed(), c => c.GetString() == "Str_Rpt_Total");
        Assert.Equal(result.Daily.Count, counts.Daily);
    }

    [Fact]
    public void Money_cells_use_the_dong_format_and_dates_dd_MM_yyyy()
    {
        var (wb, _) = Write(ReportResults.Sample(), ReportResults.Sessions());
        var sheet = wb.Worksheet(ReportSheetNames.Daily);

        Assert.Contains("₫", sheet.Cell(2, Column(sheet, "Str_Rpt_Col_NetRevenue")).Style.NumberFormat.Format);
        Assert.Contains("₫", sheet.Cell(2, Column(sheet, "Str_Rpt_Col_Cash")).Style.NumberFormat.Format);

        var date = sheet.Cell(2, Column(sheet, "Str_Rpt_Col_Date"));
        Assert.Equal(new DateTime(2026, 10, 1), date.GetDateTime().Date);
        Assert.Equal("dd/MM/yyyy", date.Style.NumberFormat.Format);
    }

    [Fact]
    public void Daily_money_values_match_the_rows()
    {
        var result = ReportResults.Sample();
        var (wb, _) = Write(result, ReportResults.Sessions());
        var sheet = wb.Worksheet(ReportSheetNames.Daily);
        int cash = Column(sheet, "Str_Rpt_Col_Cash"), vietQr = Column(sheet, "Str_Rpt_Col_VietQr"), card = Column(sheet, "Str_Rpt_Col_Card");
        int refund = Column(sheet, "Str_Rpt_Col_Refund"), checkIns = Column(sheet, "Str_Rpt_Col_CheckIns");

        for (var i = 0; i < result.Daily.Count; i++)
        {
            var row = result.Daily[i];
            Assert.Equal(row.CashRevenue, sheet.Cell(i + 2, cash).GetValue<decimal>());
            Assert.Equal(row.VietQrRevenue, sheet.Cell(i + 2, vietQr).GetValue<decimal>());
            Assert.Equal(row.CardRevenue, sheet.Cell(i + 2, card).GetValue<decimal>());
            Assert.Equal(row.RefundTotal, sheet.Cell(i + 2, refund).GetValue<decimal>());
            Assert.Equal(row.CheckIns, sheet.Cell(i + 2, checkIns).GetValue<int>());
        }
    }

    [Fact]
    public void Hourly_sheet_has_24_rows()
    {
        var (wb, counts) = Write(ReportResults.Sample(), ReportResults.Sessions());
        var sheet = wb.Worksheet(ReportSheetNames.Hourly);

        Assert.Equal(25, LastRow(sheet));
        Assert.Equal(24, counts.Hourly);
        var checkIns = Column(sheet, "Str_Rpt_Col_CheckIns");
        Assert.Equal(ReportResults.SampleCheckIns, Enumerable.Range(2, 24).Sum(r => sheet.Cell(r, checkIns).GetValue<int>()));
    }

    [Fact]
    public void Shift_and_vehicle_type_sheets_have_one_row_per_item()
    {
        var result = ReportResults.Sample();
        var (wb, counts) = Write(result, ReportResults.Sessions());

        Assert.Equal(result.Shifts.Count + 1, LastRow(wb.Worksheet(ReportSheetNames.Shifts)));
        Assert.Equal(result.VehicleTypes.Count + 1, LastRow(wb.Worksheet(ReportSheetNames.VehicleTypes)));
        Assert.Equal(result.Shifts.Count, counts.Shifts);
        Assert.Equal(result.VehicleTypes.Count, counts.VehicleTypes);
        Assert.Contains(AllCells(wb.Worksheet(ReportSheetNames.Shifts)), c => c.GetString() == "Nguyễn Văn An");
        Assert.Contains(AllCells(wb.Worksheet(ReportSheetNames.VehicleTypes)), c => c.GetString() == "Xe ô tô");
    }

    [Fact]
    public void AC1_session_checked_in_at_16_30Z_is_written_as_23_30_VN()
    {
        var sessions = ReportResults.Sessions(3);
        var (wb, counts) = Write(ReportResults.Sample(), sessions);
        var sheet = wb.Worksheet(ReportSheetNames.Sessions);
        var checkIn = sheet.Cell(2, Column(sheet, "Str_Rpt_Col_CheckInTime"));

        Assert.Equal(new DateTime(2026, 10, 1, 23, 30, 0), checkIn.GetDateTime());
        Assert.Equal("dd/MM/yyyy HH:mm", checkIn.Style.NumberFormat.Format);
        Assert.Equal(sessions.Rows.Count + 1, LastRow(sheet));
        Assert.Equal(sessions.Rows.Count, counts.Sessions);
        Assert.Contains(sheet.Row(2).CellsUsed(), c => c.GetString() == sessions.Rows[0].LicensePlate);
    }

    [Fact]
    public void Shift_opening_time_is_written_in_VN_time()
    {
        var (wb, _) = Write(ReportResults.Sample(), ReportResults.Sessions());
        var sheet = wb.Worksheet(ReportSheetNames.Shifts);

        // 01:00Z -> 08:00 VN
        Assert.Equal(new DateTime(2026, 10, 1, 8, 0, 0), sheet.Cell(2, Column(sheet, "Str_Rpt_Col_OpenedAt")).GetDateTime());
    }

    [Fact]
    public void E1_empty_result_still_has_six_sheets_with_headers()
    {
        var empty = ReportResults.Empty();
        var (wb, counts) = Write(empty, new SessionDetailPage(Array.Empty<SessionDetailRow>(), 0, false));

        Assert.Equal(ReportSheetNames.All, wb.Worksheets.Select(w => w.Name).ToArray());
        foreach (var sheet in wb.Worksheets)
        {
            Assert.True(sheet.Row(1).CellsUsed().Any(), $"{sheet.Name} has no header row");
        }

        Assert.Equal(1, LastRow(wb.Worksheet(ReportSheetNames.Sessions)));
        Assert.Equal(1, LastRow(wb.Worksheet(ReportSheetNames.Shifts)));
        Assert.Equal(0, counts.Sessions);
        Assert.Equal(0, counts.Shifts);
        var daily = wb.Worksheet(ReportSheetNames.Daily);
        Assert.Equal(0m, daily.Cell(LastRow(daily), Column(daily, "Str_Rpt_Col_NetRevenue")).GetValue<decimal>());
    }

    [Fact]
    public void Truncation_note_is_written_only_when_session_rows_were_capped()
    {
        var (truncated, _) = Write(ReportResults.Sample(), ReportResults.Sessions(3, truncated: true, totalCount: 150_000));
        var (complete, _) = Write(ReportResults.Sample(), ReportResults.Sessions(3));

        Assert.Contains(AllCells(truncated.Worksheet(ReportSheetNames.Overview)), c => c.GetString().Contains("Str_Rpt_Xlsx_Truncated", StringComparison.Ordinal));
        Assert.DoesNotContain(AllCells(complete.Worksheet(ReportSheetNames.Overview)), c => c.GetString().Contains("Str_Rpt_Xlsx_Truncated", StringComparison.Ordinal));
    }

    [Fact]
    public void Overview_sheet_has_period_kpis_monthly_new_and_renew_and_top_plates()
    {
        var (wb, _) = Write(ReportResults.Sample(), ReportResults.Sessions());
        var sheet = wb.Worksheet(ReportSheetNames.Overview);
        var cells = AllCells(sheet).ToList();

        Assert.Contains(cells, c => c.GetString().Contains("Str_Rpt_Xlsx_Period", StringComparison.Ordinal));
        Assert.Contains(cells, c => c.GetString().Contains("Str_Rpt_Xlsx_PreviousPeriod", StringComparison.Ordinal));
        Assert.Contains(cells, c => c.GetString() == "Str_Rpt_Kpi_NetRevenue");

        Assert.Contains(cells, c => c.GetString() == "Str_Rpt_Kpi_MonthlyTicketNew");
        Assert.Contains(cells, c => c.GetString() == "Str_Rpt_Kpi_MonthlyTicketRenew");
        var newRow = cells.First(c => c.GetString() == "Str_Rpt_Kpi_MonthlyTicketNew").WorksheetRow();
        var renewRow = cells.First(c => c.GetString() == "Str_Rpt_Kpi_MonthlyTicketRenew").WorksheetRow();
        Assert.Contains(newRow.CellsUsed(), c => c.DataType == XLDataType.Number && c.GetValue<decimal>() == 120_000m);
        Assert.Contains(renewRow.CellsUsed(), c => c.DataType == XLDataType.Number && c.GetValue<decimal>() == 300_000m);

        Assert.Contains(cells, c => c.GetString() == "29A-123.45");
    }

    [Fact]
    public void Headers_use_the_resolved_labels()
    {
        var labels = new Dictionary<string, string> { ["Str_Rpt_Col_NetRevenue"] = "Doanh thu thuần" };

        var (wb, _) = Write(ReportResults.Sample(), ReportResults.Sessions(), labels);

        Assert.Contains(wb.Worksheet(ReportSheetNames.Daily).Row(1).CellsUsed(), c => c.GetString() == "Doanh thu thuần");
    }

    [Fact]
    public void Labels_fall_back_to_the_key()
    {
        var labels = ReportWorkbookLabels.FromDictionary(new Dictionary<string, string> { ["a"] = "A" });

        Assert.Equal("A", labels["a"]);
        Assert.Equal("missing.key", labels["missing.key"]);
    }
}
