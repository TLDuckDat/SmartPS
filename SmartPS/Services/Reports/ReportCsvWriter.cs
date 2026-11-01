using System.Globalization;
using System.IO;
using System.Text;
using SmartPS.Models.Parking;
using SmartPS.Models.Reports;
using SmartPS.Models.Shifts;
using SmartPS.Services.Audit;

namespace SmartPS.Services.Reports;

/// <summary>
/// Exports the report as a CSV file with UTF-8 BOM encoding and all filtered sections.
/// </summary>
public sealed class ReportCsvWriter
{
    private const string DateFormat = "dd/MM/yyyy";
    private const string DateTimeFormat = "dd/MM/yyyy HH:mm";

    private readonly ReportWorkbookLabels _labels;

    public ReportCsvWriter(ReportWorkbookLabels labels)
    {
        _labels = labels ?? throw new ArgumentNullException(nameof(labels));
    }

    public ReportExportRowCounts Write(Stream output, ReportResult result, SessionDetailPage sessions)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(sessions);

        using var writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), bufferSize: 1024, leaveOpen: true);

        // 1. Overview & KPIs
        writer.WriteLine(Escape(_labels[ReportTextKeys.XlsxTitle]));
        writer.WriteLine($"{Escape(_labels[ReportTextKeys.XlsxGeneratedAt])},{Escape(AuditTime.ToVietnamTime(result.GeneratedAtUtc).ToString(DateTimeFormat, CultureInfo.InvariantCulture))}");
        writer.WriteLine($"{Escape(_labels[ReportTextKeys.XlsxPeriod])},{Escape($"{result.Filter.Range.FromVn:dd/MM/yyyy} - {result.Filter.Range.ToVn:dd/MM/yyyy}")}");
        writer.WriteLine($"{Escape(_labels[ReportTextKeys.XlsxPreviousPeriod])},{Escape($"{result.PreviousRange.FromVn:dd/MM/yyyy} - {result.PreviousRange.ToVn:dd/MM/yyyy}")}");
        writer.WriteLine($"{Escape(_labels[ReportTextKeys.XlsxFilters])},{Escape(FilterText(result))}");
        writer.WriteLine();

        // KPI Summary Table
        writer.WriteLine($"{Escape(_labels[ReportTextKeys.XlsxMetric])},{Escape(_labels[ReportTextKeys.XlsxCurrent])},{Escape(_labels[ReportTextKeys.XlsxPrevious])},{Escape(_labels[ReportTextKeys.XlsxChange])}");
        WriteKpiRow(writer, ReportTextKeys.KpiCheckIns, result.Kpis.CheckIns);
        WriteKpiRow(writer, ReportTextKeys.KpiCheckOuts, result.Kpis.CheckOuts);
        writer.WriteLine($"{Escape(_labels[ReportTextKeys.KpiInLotNow])},{result.Kpis.VehiclesInLotNow},,");
        WriteKpiRow(writer, ReportTextKeys.KpiNetRevenue, result.Kpis.NetRevenue);
        WriteKpiRow(writer, ReportTextKeys.KpiCash, result.Kpis.CashRevenue);
        WriteKpiRow(writer, ReportTextKeys.KpiVietQr, result.Kpis.VietQrRevenue);
        WriteKpiRow(writer, ReportTextKeys.KpiCard, result.Kpis.CardRevenue);
        WriteKpiRow(writer, ReportTextKeys.KpiFreeCheckOuts, result.Kpis.FreeCheckOuts);
        WriteKpiRow(writer, ReportTextKeys.KpiAvgDuration, result.Kpis.AverageDurationMinutes);
        writer.WriteLine();

        // 2. Daily Breakdown
        writer.WriteLine($"# {Escape(_labels[ReportTextKeys.TabDaily])}");
        writer.WriteLine(string.Join(",", new[]
        {
            Escape(_labels[ReportTextKeys.ColDate]),
            Escape(_labels[ReportTextKeys.ColCheckIns]),
            Escape(_labels[ReportTextKeys.ColCheckOuts]),
            Escape(_labels[ReportTextKeys.ColCash]),
            Escape(_labels[ReportTextKeys.ColVietQr]),
            Escape(_labels[ReportTextKeys.ColCard]),
            Escape(_labels[ReportTextKeys.ColRefund]),
            Escape(_labels[ReportTextKeys.ColNetRevenue]),
            Escape(_labels[ReportTextKeys.ColFreeCheckOuts]),
            Escape(_labels[ReportTextKeys.ColAvgDuration])
        }));
        foreach (var d in result.Daily)
        {
            writer.WriteLine(string.Join(",", new[]
            {
                Escape(d.DayVn.ToString(DateFormat, CultureInfo.InvariantCulture)),
                d.CheckIns.ToString(CultureInfo.InvariantCulture),
                d.CheckOuts.ToString(CultureInfo.InvariantCulture),
                d.CashRevenue.ToString("F0", CultureInfo.InvariantCulture),
                d.VietQrRevenue.ToString("F0", CultureInfo.InvariantCulture),
                d.CardRevenue.ToString("F0", CultureInfo.InvariantCulture),
                d.RefundTotal.ToString("F0", CultureInfo.InvariantCulture),
                d.NetRevenue.ToString("F0", CultureInfo.InvariantCulture),
                d.FreeCheckOuts.ToString(CultureInfo.InvariantCulture),
                d.AverageDurationMinutes.HasValue ? d.AverageDurationMinutes.Value.ToString("F1", CultureInfo.InvariantCulture) : string.Empty
            }));
        }
        writer.WriteLine();

        // 3. Vehicle Types Breakdown
        writer.WriteLine($"# {Escape(_labels[ReportTextKeys.TabVehicleTypes])}");
        writer.WriteLine(string.Join(",", new[]
        {
            Escape(_labels[ReportTextKeys.ColVehicleType]),
            Escape(_labels[ReportTextKeys.ColCheckIns]),
            Escape(_labels[ReportTextKeys.ColCheckOuts]),
            Escape(_labels[ReportTextKeys.ColNetRevenue]),
            Escape(_labels[ReportTextKeys.ColAvgDuration])
        }));
        foreach (var v in result.VehicleTypes)
        {
            writer.WriteLine(string.Join(",", new[]
            {
                Escape(v.VehicleTypeName),
                v.CheckIns.ToString(CultureInfo.InvariantCulture),
                v.CheckOuts.ToString(CultureInfo.InvariantCulture),
                v.NetRevenue.ToString("F0", CultureInfo.InvariantCulture),
                v.AverageDurationMinutes.HasValue ? v.AverageDurationMinutes.Value.ToString("F1", CultureInfo.InvariantCulture) : string.Empty
            }));
        }
        writer.WriteLine();

        // 4. Shifts Breakdown
        writer.WriteLine($"# {Escape(_labels[ReportTextKeys.TabShifts])}");
        writer.WriteLine(string.Join(",", new[]
        {
            Escape(_labels[ReportTextKeys.ColRank]),
            Escape(_labels[ReportTextKeys.ColOpenedBy]),
            Escape(_labels[ReportTextKeys.ColOpenedAt]),
            Escape(_labels[ReportTextKeys.ColClosedAt]),
            Escape(_labels[ReportTextKeys.ColBeginningCash]),
            Escape(_labels[ReportTextKeys.ColExpectedCash]),
            Escape(_labels[ReportTextKeys.ColActualCash]),
            Escape(_labels[ReportTextKeys.ColDifference]),
            Escape(_labels[ReportTextKeys.ColStatus])
        }));
        foreach (var s in result.Shifts)
        {
            writer.WriteLine(string.Join(",", new[]
            {
                s.ShiftId.ToString(CultureInfo.InvariantCulture),
                Escape(s.OpenedByName),
                Escape(AuditTime.ToVietnamTime(s.OpenedAtUtc).ToString(DateTimeFormat, CultureInfo.InvariantCulture)),
                Escape(s.ClosedAtUtc.HasValue ? AuditTime.ToVietnamTime(s.ClosedAtUtc.Value).ToString(DateTimeFormat, CultureInfo.InvariantCulture) : string.Empty),
                s.BeginningCash.ToString("F0", CultureInfo.InvariantCulture),
                s.ExpectedCash.HasValue ? s.ExpectedCash.Value.ToString("F0", CultureInfo.InvariantCulture) : string.Empty,
                s.ActualCash.HasValue ? s.ActualCash.Value.ToString("F0", CultureInfo.InvariantCulture) : string.Empty,
                s.Difference.HasValue ? s.Difference.Value.ToString("F0", CultureInfo.InvariantCulture) : string.Empty,
                Escape(ShiftStatusLabel(s.Status))
            }));
        }
        writer.WriteLine();

        // 5. Session Details
        writer.WriteLine($"# {Escape(ReportSheetNames.Sessions)}");
        writer.WriteLine(string.Join(",", new[]
        {
            Escape(_labels[ReportTextKeys.ColSessionId]),
            Escape(_labels[ReportTextKeys.ColTicketCode]),
            Escape(_labels[ReportTextKeys.ColLicensePlate]),
            Escape(_labels[ReportTextKeys.ColVehicleType]),
            Escape(_labels[ReportTextKeys.ColCustomerGroup]),
            Escape(_labels[ReportTextKeys.ColZone]),
            Escape(_labels[ReportTextKeys.ColSlot]),
            Escape(_labels[ReportTextKeys.ColCheckInTime]),
            Escape(_labels[ReportTextKeys.ColCheckOutTime]),
            Escape(_labels[ReportTextKeys.ColDurationMinutes]),
            Escape(_labels[ReportTextKeys.ColStatus]),
            Escape(_labels[ReportTextKeys.ColFee]),
            Escape(_labels[ReportTextKeys.ColPaymentMethod])
        }));
        var groupLabels = new Dictionary<ReportCustomerGroup, string>
        {
            [ReportCustomerGroup.Resident] = _labels[ReportTextKeys.GroupResident],
            [ReportCustomerGroup.MonthlyPass] = _labels[ReportTextKeys.GroupMonthlyPass],
            [ReportCustomerGroup.Visitor] = _labels[ReportTextKeys.GroupVisitor],
            [ReportCustomerGroup.All] = _labels[ReportTextKeys.GroupAll]
        };
        foreach (var r in sessions.Rows)
        {
            writer.WriteLine(string.Join(",", new[]
            {
                r.SessionId.ToString(CultureInfo.InvariantCulture),
                Escape(r.TicketCode),
                Escape(r.LicensePlate),
                Escape(r.VehicleTypeName),
                Escape(groupLabels.TryGetValue(r.CustomerGroup, out var gl) ? gl : r.CustomerGroup.ToString()),
                Escape(r.ZoneName ?? string.Empty),
                Escape(r.SlotCode ?? string.Empty),
                Escape(AuditTime.ToVietnamTime(r.CheckInUtc).ToString(DateTimeFormat, CultureInfo.InvariantCulture)),
                Escape(r.CheckOutUtc.HasValue ? AuditTime.ToVietnamTime(r.CheckOutUtc.Value).ToString(DateTimeFormat, CultureInfo.InvariantCulture) : string.Empty),
                r.DurationMinutes.HasValue ? Math.Round(r.DurationMinutes.Value, 1).ToString("F1", CultureInfo.InvariantCulture) : string.Empty,
                Escape(SessionStatusLabel(r.Status)),
                r.TotalFee.ToString("F0", CultureInfo.InvariantCulture),
                Escape(PaymentLabel(r.PaymentMethod))
            }));
        }

        writer.Flush();
        return new ReportExportRowCounts(result.Daily.Count, result.Hourly.Count, result.Shifts.Count, result.VehicleTypes.Count, sessions.Rows.Count);
    }

    private void WriteKpiRow(StreamWriter writer, string labelKey, KpiComparison? comparison)
    {
        var label = _labels[labelKey];
        if (comparison is null)
        {
            writer.WriteLine($"{Escape(label)},-,,-");
            return;
        }

        var cur = comparison.Current.ToString("N0", CultureInfo.InvariantCulture);
        var prev = comparison.Previous.HasValue
            ? comparison.Previous.Value.ToString("N0", CultureInfo.InvariantCulture)
            : string.Empty;
        var change = ReportFormat.Change(comparison);

        writer.WriteLine($"{Escape(label)},{Escape(cur)},{Escape(prev)},{Escape(change)}");
    }

    private string FilterText(ReportResult result)
    {
        var filter = result.Filter;
        var all = _labels[ReportTextKeys.FilterAll];
        var vehicle = filter.VehicleTypeId is { } vt
            ? result.VehicleTypes.FirstOrDefault(v => v.VehicleTypeId == vt)?.VehicleTypeName ?? vt.ToString(CultureInfo.InvariantCulture)
            : all;
        var zone = filter.ZoneId is { } z
            ? result.Zones.FirstOrDefault(x => x.ZoneId == z)?.ZoneName ?? z.ToString(CultureInfo.InvariantCulture)
            : all;
        var group = filter.CustomerGroup switch
        {
            ReportCustomerGroup.Resident => _labels[ReportTextKeys.GroupResident],
            ReportCustomerGroup.MonthlyPass => _labels[ReportTextKeys.GroupMonthlyPass],
            ReportCustomerGroup.Visitor => _labels[ReportTextKeys.GroupVisitor],
            _ => all
        };

        var staff = filter.StaffUserId.HasValue
            ? filter.StaffUserId.Value.ToString(CultureInfo.InvariantCulture)
            : all;
        var shift = filter.ShiftId.HasValue
            ? filter.ShiftId.Value.ToString(CultureInfo.InvariantCulture)
            : all;
        var method = filter.PaymentMethod switch
        {
            PaymentMethod.Cash => _labels[ReportTextKeys.MethodCash],
            PaymentMethod.VietQR => _labels[ReportTextKeys.MethodVietQr],
            PaymentMethod.Card => _labels[ReportTextKeys.MethodCard],
            _ => all
        };

        return $"{_labels[ReportTextKeys.FilterVehicleType]}: {vehicle}; {_labels[ReportTextKeys.FilterCustomerGroup]}: {group}; {_labels[ReportTextKeys.FilterZone]}: {zone}; {_labels[ReportTextKeys.FilterStaff]}: {staff}; {_labels[ReportTextKeys.FilterShift]}: {shift}; {_labels[ReportTextKeys.FilterPaymentMethod]}: {method}";
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

    private static string Escape(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        if (s.Contains(',') || s.Contains('"') || s.Contains('\r') || s.Contains('\n'))
        {
            return $"\"{s.Replace("\"", "\"\"")}\"";
        }
        return s;
    }
}
