using System.IO;
using ClosedXML.Excel;
using SmartPS.Models.Parking;
using SmartPS.Models.Reports;
using SmartPS.Models.Shifts;
using SmartPS.Services.Audit;

namespace SmartPS.Services.Reports;

/// <summary>
/// Builds the six-sheet .xlsx workbook with ClosedXML. It has no database and no WPF dependency and only reads the
/// pre-resolved <see cref="ReportWorkbookLabels"/>, so it is safe to run on a worker thread.
/// </summary>
public sealed class ReportWorkbookWriter
{
    private const string MoneyFormat = "#,##0 \"₫\"";
    private const string DateFormat = "dd/MM/yyyy";
    private const string DateTimeFormat = "dd/MM/yyyy HH:mm";
    private const string OneDecimal = "0.0";
    private const string TwoDecimals = "0.00";
    private const string IntegerFormat = "#,##0";
    private const int AutoFitRowLimit = 1000;

    private readonly ReportWorkbookLabels _labels;

    public ReportWorkbookWriter(ReportWorkbookLabels labels)
    {
        _labels = labels ?? throw new ArgumentNullException(nameof(labels));
    }

    public ReportExportRowCounts Write(Stream output, ReportResult result, SessionDetailPage sessions)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(sessions);

        using var workbook = new XLWorkbook();
        WriteOverview(workbook.AddWorksheet(ReportSheetNames.Overview), result, sessions);
        WriteDaily(workbook.AddWorksheet(ReportSheetNames.Daily), result);
        WriteHourly(workbook.AddWorksheet(ReportSheetNames.Hourly), result);
        WriteShifts(workbook.AddWorksheet(ReportSheetNames.Shifts), result);
        WriteVehicleTypes(workbook.AddWorksheet(ReportSheetNames.VehicleTypes), result);
        WriteSessions(workbook.AddWorksheet(ReportSheetNames.Sessions), sessions);
        workbook.SaveAs(output);

        return new ReportExportRowCounts(result.Daily.Count, result.Hourly.Count, result.Shifts.Count, result.VehicleTypes.Count, sessions.Rows.Count);
    }

    // ---- sheets ----------------------------------------------------------------------------------------------------

    private void WriteOverview(IXLWorksheet ws, ReportResult result, SessionDetailPage sessions)
    {
        var kpis = result.Kpis;
        var row = 1;

        ws.Cell(row, 1).Value = _labels[ReportTextKeys.XlsxTitle];
        ws.Row(row).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Font.FontSize = 14;
        row++;

        ws.Cell(row, 1).Value = _labels[ReportTextKeys.XlsxGeneratedAt];
        SetDateTime(ws.Cell(row, 2), result.GeneratedAtUtc);
        row++;
        ws.Cell(row, 1).Value = _labels[ReportTextKeys.XlsxPeriod];
        ws.Cell(row, 2).Value = RangeText(result.Filter.Range);
        row++;
        ws.Cell(row, 1).Value = _labels[ReportTextKeys.XlsxPreviousPeriod];
        ws.Cell(row, 2).Value = RangeText(result.PreviousRange);
        row++;
        ws.Cell(row, 1).Value = _labels[ReportTextKeys.XlsxFilters];
        ws.Cell(row, 2).Value = FilterText(result);
        row++;
        row++;

        Header(ws, row, ReportTextKeys.XlsxMetric, ReportTextKeys.XlsxCurrent, ReportTextKeys.XlsxPrevious, ReportTextKeys.XlsxChange);
        row++;

        row = KpiRow(ws, row, ReportTextKeys.KpiCheckIns, kpis.CheckIns, IntegerFormat);
        row = KpiRow(ws, row, ReportTextKeys.KpiCheckOuts, kpis.CheckOuts, IntegerFormat);
        row = SingleRow(ws, row, ReportTextKeys.KpiInLotNow, kpis.VehiclesInLotNow, IntegerFormat);
        row = KpiRow(ws, row, ReportTextKeys.KpiNetRevenue, kpis.NetRevenue, MoneyFormat);
        row = KpiRow(ws, row, ReportTextKeys.KpiCash, kpis.CashRevenue, MoneyFormat);
        row = KpiRow(ws, row, ReportTextKeys.KpiVietQr, kpis.VietQrRevenue, MoneyFormat);
        row = KpiRow(ws, row, ReportTextKeys.KpiCard, kpis.CardRevenue, MoneyFormat);
        row = SingleRow(ws, row, ReportTextKeys.ColRefund, kpis.RefundTotal, MoneyFormat);
        row = SingleRow(ws, row, ReportTextKeys.ColAdjustment, kpis.AdjustmentTotal, MoneyFormat);
        row = KpiRow(ws, row, ReportTextKeys.KpiFreeCheckOuts, kpis.FreeCheckOuts, IntegerFormat);
        row = KpiRow(ws, row, ReportTextKeys.KpiAvgDuration, kpis.AverageDurationMinutes, OneDecimal);

        ws.Cell(row, 1).Value = _labels[ReportTextKeys.KpiPeakHour];
        ws.Cell(row, 2).Value = ReportFormat.HourRange(kpis.PeakHour);
        ws.Cell(row, 3).Value = ReportFormat.HourRange(kpis.PreviousPeakHour);
        row++;

        row = KpiRow(ws, row, ReportTextKeys.KpiAvgOccupancy, kpis.AverageOccupancyPercent, OneDecimal);
        row = KpiRow(ws, row, ReportTextKeys.KpiPeakOccupancy, kpis.PeakOccupancyPercent, OneDecimal);
        row = KpiRow(ws, row, ReportTextKeys.KpiResidentShare, kpis.ResidentSharePercent, OneDecimal);
        row = KpiRow(ws, row, ReportTextKeys.KpiMonthlyTicketRevenue, kpis.MonthlyTicketRevenue, MoneyFormat);
        row = SingleRow(ws, row, ReportTextKeys.KpiMonthlyTicketNew, kpis.MonthlyTicketSales?.NewRevenue, MoneyFormat);
        row = SingleRow(ws, row, ReportTextKeys.KpiMonthlyTicketRenew, kpis.MonthlyTicketSales?.RenewRevenue, MoneyFormat);

        row++;
        ws.Cell(row, 1).Value = _labels[ReportTextKeys.XlsxTopPlates];
        ws.Cell(row, 1).Style.Font.Bold = true;
        row++;
        Header(ws, row, ReportTextKeys.ColRank, ReportTextKeys.ColLicensePlate, ReportTextKeys.ColVisits, ReportTextKeys.ColLastCheckIn);
        row++;
        foreach (var plate in result.TopPlates)
        {
            ws.Cell(row, 1).Value = plate.Rank;
            ws.Cell(row, 2).Value = plate.LicensePlate;
            ws.Cell(row, 3).Value = plate.Visits;
            SetDateTime(ws.Cell(row, 4), plate.LastCheckInUtc);
            row++;
        }

        if (sessions.IsTruncated)
        {
            row++;
            ws.Cell(row, 1).Value = _labels.Format(ReportTextKeys.XlsxTruncated, sessions.Rows.Count, sessions.TotalCount);
            ws.Cell(row, 1).Style.Font.Italic = true;
        }

        Finish(ws, row);
    }

    private void WriteDaily(IXLWorksheet ws, ReportResult result)
    {
        string[] headers =
        {
            ReportTextKeys.ColDate, ReportTextKeys.ColCheckIns, ReportTextKeys.ColCheckOuts, ReportTextKeys.ColCash, ReportTextKeys.ColVietQr,
            ReportTextKeys.ColCard, ReportTextKeys.ColRefund, ReportTextKeys.ColAdjustment, ReportTextKeys.ColNetRevenue,
            ReportTextKeys.ColFreeCheckOuts, ReportTextKeys.ColAvgDuration
        };
        Header(ws, 1, headers);

        var rows = result.Daily.Select(d => new object?[]
        {
            d.DayVn.ToDateTime(TimeOnly.MinValue), d.CheckIns, d.CheckOuts, d.CashRevenue, d.VietQrRevenue, d.CardRevenue,
            d.RefundTotal, d.AdjustmentTotal, d.NetRevenue, d.FreeCheckOuts, d.AverageDurationMinutes
        }).ToList();
        Insert(ws, rows);

        var total = result.Daily.Count + 2;
        ws.Cell(total, 1).Value = _labels[ReportTextKeys.Total];
        ws.Cell(total, 2).Value = result.Daily.Sum(d => d.CheckIns);
        ws.Cell(total, 3).Value = result.Daily.Sum(d => d.CheckOuts);
        ws.Cell(total, 4).Value = result.Daily.Sum(d => d.CashRevenue);
        ws.Cell(total, 5).Value = result.Daily.Sum(d => d.VietQrRevenue);
        ws.Cell(total, 6).Value = result.Daily.Sum(d => d.CardRevenue);
        ws.Cell(total, 7).Value = result.Daily.Sum(d => d.RefundTotal);
        ws.Cell(total, 8).Value = result.Daily.Sum(d => d.AdjustmentTotal);
        ws.Cell(total, 9).Value = result.Daily.Sum(d => d.NetRevenue);
        ws.Cell(total, 10).Value = result.Daily.Sum(d => d.FreeCheckOuts);
        ws.Row(total).Style.Font.Bold = true;

        var last = total;
        Format(ws, 1, 1, last, DateFormat);
        Format(ws, 2, 3, last, IntegerFormat);
        Format(ws, 4, 9, last, MoneyFormat);
        Format(ws, 10, 10, last, IntegerFormat);
        Format(ws, 11, 11, last, OneDecimal);
        Finish(ws, last);
    }

    private void WriteHourly(IXLWorksheet ws, ReportResult result)
    {
        Header(ws, 1, ReportTextKeys.ColHour, ReportTextKeys.ColCheckIns, ReportTextKeys.ColCheckOuts,
            ReportTextKeys.ColAvgCheckInsPerDay, ReportTextKeys.ColAvgCheckOutsPerDay);
        Insert(ws, result.Hourly.Select(h => new object?[]
        {
            ReportFormat.HourRange(h.HourVn), h.CheckIns, h.CheckOuts, h.AverageCheckInsPerDay, h.AverageCheckOutsPerDay
        }).ToList());

        var last = result.Hourly.Count + 1;
        Format(ws, 2, 3, last, IntegerFormat);
        Format(ws, 4, 5, last, TwoDecimals);
        Finish(ws, last);
    }

    private void WriteShifts(IXLWorksheet ws, ReportResult result)
    {
        Header(ws, 1, ReportTextKeys.ColOpenedBy, ReportTextKeys.ColOpenedAt, ReportTextKeys.ColClosedAt, ReportTextKeys.ColBeginningCash,
            ReportTextKeys.ColExpectedCash, ReportTextKeys.ColActualCash, ReportTextKeys.ColDifference, ReportTextKeys.ColStatus);
        Insert(ws, result.Shifts.Select(s => new object?[]
        {
            s.OpenedByName, ToVn(s.OpenedAtUtc), s.ClosedAtUtc is null ? null : ToVn(s.ClosedAtUtc.Value), s.BeginningCash,
            s.ExpectedCash, s.ActualCash, s.Difference, ShiftStatusLabel(s.Status)
        }).ToList());

        var last = result.Shifts.Count + 1;
        Format(ws, 2, 3, last, DateTimeFormat);
        Format(ws, 4, 7, last, MoneyFormat);
        Finish(ws, last);
    }

    private void WriteVehicleTypes(IXLWorksheet ws, ReportResult result)
    {
        Header(ws, 1, ReportTextKeys.ColVehicleType, ReportTextKeys.ColCheckIns, ReportTextKeys.ColCheckOuts, ReportTextKeys.ColNetRevenue,
            ReportTextKeys.ColAvgDuration);
        Insert(ws, result.VehicleTypes.Select(v => new object?[]
        {
            v.VehicleTypeName, v.CheckIns, v.CheckOuts, v.NetRevenue, v.AverageDurationMinutes
        }).ToList());

        var last = result.VehicleTypes.Count + 1;
        Format(ws, 2, 3, last, IntegerFormat);
        Format(ws, 4, 4, last, MoneyFormat);
        Format(ws, 5, 5, last, OneDecimal);
        Finish(ws, last);
    }

    private void WriteSessions(IXLWorksheet ws, SessionDetailPage sessions)
    {
        Header(ws, 1, ReportTextKeys.ColSessionId, ReportTextKeys.ColTicketCode, ReportTextKeys.ColLicensePlate, ReportTextKeys.ColVehicleType,
            ReportTextKeys.ColCustomerGroup, ReportTextKeys.ColZone, ReportTextKeys.ColSlot, ReportTextKeys.ColCheckInTime,
            ReportTextKeys.ColCheckOutTime, ReportTextKeys.ColDurationMinutes, ReportTextKeys.ColStatus, ReportTextKeys.ColFee,
            ReportTextKeys.ColPaymentMethod);

        var groupLabels = new Dictionary<ReportCustomerGroup, string>
        {
            [ReportCustomerGroup.Resident] = _labels[ReportTextKeys.GroupResident],
            [ReportCustomerGroup.MonthlyPass] = _labels[ReportTextKeys.GroupMonthlyPass],
            [ReportCustomerGroup.Visitor] = _labels[ReportTextKeys.GroupVisitor],
            [ReportCustomerGroup.All] = _labels[ReportTextKeys.GroupAll]
        };

        Insert(ws, sessions.Rows.Select(r => new object?[]
        {
            r.SessionId, r.TicketCode, r.LicensePlate, r.VehicleTypeName, groupLabels[r.CustomerGroup], r.ZoneName, r.SlotCode,
            ToVn(r.CheckInUtc), r.CheckOutUtc is null ? null : ToVn(r.CheckOutUtc.Value), r.DurationMinutes.HasValue ? Math.Round(r.DurationMinutes.Value, 1) : null,
            SessionStatusLabel(r.Status), r.TotalFee, PaymentLabel(r.PaymentMethod)
        }));

        var last = sessions.Rows.Count + 1;
        Format(ws, 8, 9, last, DateTimeFormat);
        Format(ws, 10, 10, last, OneDecimal);
        Format(ws, 12, 12, last, MoneyFormat);
        Finish(ws, last);
    }

    // ---- helpers ---------------------------------------------------------------------------------------------------

    private static void Insert(IXLWorksheet ws, IEnumerable<object?[]> rows)
    {
        var list = rows as IReadOnlyCollection<object?[]> ?? rows.ToList();
        if (list.Count > 0)
        {
            ws.Cell(2, 1).InsertData(list);
        }
    }

    private void Header(IXLWorksheet ws, int row, params string[] keys)
    {
        for (var i = 0; i < keys.Length; i++)
        {
            ws.Cell(row, i + 1).Value = _labels[keys[i]];
        }

        ws.Range(row, 1, row, keys.Length).Style.Font.Bold = true;
    }

    private int KpiRow(IXLWorksheet ws, int row, string labelKey, KpiComparison? comparison, string format)
    {
        ws.Cell(row, 1).Value = _labels[labelKey];
        if (comparison is null)
        {
            ws.Cell(row, 2).Value = ReportFormat.Dash;
        }
        else
        {
            ws.Cell(row, 2).Value = comparison.Current;
            ws.Cell(row, 2).Style.NumberFormat.Format = format;
            if (comparison.Previous is { } previous)
            {
                ws.Cell(row, 3).Value = previous;
                ws.Cell(row, 3).Style.NumberFormat.Format = format;
            }

            ws.Cell(row, 4).Value = ReportFormat.Change(comparison);
        }

        return row + 1;
    }

    private int SingleRow(IXLWorksheet ws, int row, string labelKey, decimal? value, string format)
    {
        ws.Cell(row, 1).Value = _labels[labelKey];
        if (value is { } number)
        {
            ws.Cell(row, 2).Value = number;
            ws.Cell(row, 2).Style.NumberFormat.Format = format;
        }
        else
        {
            ws.Cell(row, 2).Value = ReportFormat.Dash;
        }

        return row + 1;
    }

    private string RangeText(ReportDateRange range)
        => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{range.FromVn:dd/MM/yyyy} - {range.ToVn:dd/MM/yyyy}");

    private string FilterText(ReportResult result)
    {
        var filter = result.Filter;
        var all = _labels[ReportTextKeys.FilterAll];
        var vehicle = filter.VehicleTypeId is { } vt
            ? result.VehicleTypes.FirstOrDefault(v => v.VehicleTypeId == vt)?.VehicleTypeName ?? vt.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : all;
        var zone = filter.ZoneId is { } z
            ? result.Zones.FirstOrDefault(x => x.ZoneId == z)?.ZoneName ?? z.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : all;
        var group = filter.CustomerGroup switch
        {
            ReportCustomerGroup.Resident => _labels[ReportTextKeys.GroupResident],
            ReportCustomerGroup.MonthlyPass => _labels[ReportTextKeys.GroupMonthlyPass],
            ReportCustomerGroup.Visitor => _labels[ReportTextKeys.GroupVisitor],
            _ => all
        };

        return $"{_labels[ReportTextKeys.FilterVehicleType]}: {vehicle}; {_labels[ReportTextKeys.FilterCustomerGroup]}: {group}; {_labels[ReportTextKeys.FilterZone]}: {zone}";
    }

    private string ShiftStatusLabel(ShiftStatus status) => status switch
    {
        ShiftStatus.Active => _labels[ReportTextKeys.ShiftStatusActive],
        ShiftStatus.Locked => _labels[ReportTextKeys.ShiftStatusLocked],
        _ => _labels[ReportTextKeys.ShiftStatusReviewed]
    };

    private string SessionStatusLabel(SessionStatus status) => status switch
    {
        SessionStatus.Active => _labels[ReportTextKeys.SessionStatusActive],
        SessionStatus.Completed => _labels[ReportTextKeys.SessionStatusCompleted],
        _ => _labels[ReportTextKeys.SessionStatusCancelled]
    };

    private string PaymentLabel(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => _labels[ReportTextKeys.MethodCash],
        PaymentMethod.VietQR => _labels[ReportTextKeys.MethodVietQr],
        PaymentMethod.Card => _labels[ReportTextKeys.MethodCard],
        _ => _labels[ReportTextKeys.MethodFree]
    };

    private static DateTime ToVn(DateTime utc) => AuditTime.ToVietnamTime(utc);

    private static void SetDateTime(IXLCell cell, DateTime utc)
    {
        cell.Value = ToVn(utc);
        cell.Style.NumberFormat.Format = DateTimeFormat;
    }

    /// <summary>Applies a number format to columns [fromColumn, toColumn] for rows [firstRow, lastRow].</summary>
    private static void Format(IXLWorksheet ws, int fromColumn, int toColumn, int lastRow, string format, int firstRow = 2)
    {
        if (lastRow < firstRow)
        {
            return;
        }

        ws.Range(firstRow, fromColumn, lastRow, toColumn).Style.NumberFormat.Format = format;
    }

    private static void Finish(IXLWorksheet ws, int lastRow)
    {
        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents(1, Math.Max(1, Math.Min(lastRow, AutoFitRowLimit)));
    }
}
