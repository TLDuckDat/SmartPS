using System.Globalization;
using System.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SmartPS.Models.Parking;
using SmartPS.Models.Reports;
using SmartPS.Models.Shifts;
using SmartPS.Services.Audit;

namespace SmartPS.Services.Reports;

/// <summary>
/// Professional PDF exporter using QuestPDF. Generates A4 landscape pages with KPI summaries,
/// breakdown tables, and paginated session details matching active report filters.
/// </summary>
public sealed class ReportPdfWriter
{
    private const string DateTimeFormat = "dd/MM/yyyy HH:mm";
    private const string DateFormat = "dd/MM/yyyy";

    private readonly ReportWorkbookLabels _labels;

    public ReportPdfWriter(ReportWorkbookLabels labels)
    {
        _labels = labels ?? throw new ArgumentNullException(nameof(labels));
    }

    public IDocument CreateDocument(ReportResult result, SessionDetailPage sessions)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(sessions);

        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Settings.UseSystemFonts = true;
        QuestPDF.Settings.ThrowOnMissingFontFamilies = false;

        var vnNow = AuditTime.ToVietnamTime(result.GeneratedAtUtc).ToString(DateTimeFormat, CultureInfo.InvariantCulture);
        var periodText = $"{result.Filter.Range.FromVn:dd/MM/yyyy} - {result.Filter.Range.ToVn:dd/MM/yyyy}";
        var filterInfo = FilterText(result);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(24);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(8.5f).FontColor(Colors.Grey.Darken3));

                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text(_labels[ReportTextKeys.XlsxTitle])
                                .FontSize(14).Bold().FontColor("#1E3A8A");
                            c.Item().PaddingTop(2).Text($"{_labels[ReportTextKeys.XlsxGeneratedAt]}: {vnNow}  |  {_labels[ReportTextKeys.XlsxPeriod]}: {periodText}")
                                .FontSize(8.5f).FontColor(Colors.Grey.Darken1);
                            c.Item().PaddingTop(2).Text(filterInfo)
                                .FontSize(8f).FontColor(Colors.Grey.Darken1);
                        });
                    });

                    col.Item().PaddingVertical(6).LineHorizontal(1).LineColor("#E2E8F0");
                });

                page.Content().Column(col =>
                {
                    // 1. KPI Cards Row
                    col.Item().Row(row =>
                    {
                        row.Spacing(8);
                        row.RelativeItem().Element(e => ComposeKpiCard(e, _labels[ReportTextKeys.KpiNetRevenue], $"{result.Kpis.NetRevenue.Current:N0} đ"));
                        row.RelativeItem().Element(e => ComposeKpiCard(e, _labels[ReportTextKeys.KpiCash], $"{result.Kpis.CashRevenue.Current:N0} đ"));
                        row.RelativeItem().Element(e => ComposeKpiCard(e, _labels[ReportTextKeys.KpiVietQr], $"{result.Kpis.VietQrRevenue.Current:N0} đ"));
                        row.RelativeItem().Element(e => ComposeKpiCard(e, $"{_labels[ReportTextKeys.KpiCheckIns]} / {_labels[ReportTextKeys.KpiCheckOuts]}", $"{result.Kpis.CheckIns.Current:N0} / {result.Kpis.CheckOuts.Current:N0}"));
                    });

                    // 2. Daily breakdown table if exists
                    if (result.Daily.Count > 0)
                    {
                        col.Item().PaddingTop(10).Text(_labels[ReportTextKeys.TabDaily]).FontSize(10f).Bold().FontColor("#1E3A8A");
                        col.Item().PaddingTop(3).Element(e => ComposeDailyTable(e, result.Daily));
                    }

                    // 3. Vehicle types breakdown table if exists
                    if (result.VehicleTypes.Count > 0)
                    {
                        col.Item().PaddingTop(10).Text(_labels[ReportTextKeys.TabVehicleTypes]).FontSize(10f).Bold().FontColor("#1E3A8A");
                        col.Item().PaddingTop(3).Element(e => ComposeVehicleTypesTable(e, result.VehicleTypes));
                    }

                    // 4. Session Details table
                    if (sessions.Rows.Count > 0)
                    {
                        col.Item().PaddingTop(10).Row(r =>
                        {
                            r.RelativeItem().Text($"{_labels[ReportTextKeys.XlsxTitle]} - {_labels[ReportSheetNames.Sessions]}")
                                .FontSize(10f).Bold().FontColor("#1E3A8A");
                            r.AutoItem().Text($"Tổng cộng: {sessions.Rows.Count:N0} lượt").FontSize(8.5f).FontColor(Colors.Grey.Darken1);
                        });
                        col.Item().PaddingTop(3).Element(e => ComposeSessionsTable(e, sessions.Rows));
                    }
                });

                page.Footer().Column(col =>
                {
                    col.Item().PaddingTop(6).LineHorizontal(1).LineColor("#E2E8F0");
                    col.Item().PaddingTop(3).Row(row =>
                    {
                        row.RelativeItem().Text("Hệ thống quản lý bãi đỗ xe thông minh SmartPS").FontSize(8).FontColor(Colors.Grey.Medium);
                        row.RelativeItem().AlignRight().Text(x =>
                        {
                            x.Span("Trang ").FontSize(8).FontColor(Colors.Grey.Medium);
                            x.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Medium);
                            x.Span(" / ").FontSize(8).FontColor(Colors.Grey.Medium);
                            x.TotalPages().FontSize(8).FontColor(Colors.Grey.Medium);
                        });
                    });
                });
            });
        });
    }

    public ReportExportRowCounts Write(Stream output, ReportResult result, SessionDetailPage sessions)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(sessions);

        var doc = CreateDocument(result, sessions);
        doc.GeneratePdf(output);

        return new ReportExportRowCounts(
            Daily: result.Daily.Count,
            Hourly: result.Hourly.Count,
            Shifts: result.Shifts.Count,
            VehicleTypes: result.VehicleTypes.Count,
            Sessions: sessions.Rows.Count);
    }

    private static void ComposeKpiCard(IContainer container, string title, string value)
    {
        container.Border(1).BorderColor("#E2E8F0").Background("#F8FAFC").Padding(6).Column(col =>
        {
            col.Item().Text(title).FontSize(7.5f).FontColor(Colors.Grey.Darken1);
            col.Item().PaddingTop(2).Text(value).FontSize(11).Bold().FontColor("#0F172A");
        });
    }

    private void ComposeDailyTable(IContainer container, IReadOnlyList<DailyReportRow> daily)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(1.2f); // Ngày
                columns.RelativeColumn(1f);   // Vào
                columns.RelativeColumn(1f);   // Ra
                columns.RelativeColumn(1.3f); // Tiền mặt
                columns.RelativeColumn(1.3f); // VietQR
                columns.RelativeColumn(1.2f); // Thẻ
                columns.RelativeColumn(1.2f); // Hoàn tiền
                columns.RelativeColumn(1.4f); // Doanh thu thuần
                columns.RelativeColumn(1.1f); // TG trung bình
            });

            table.Header(header =>
            {
                header.Cell().Element(HeaderCellStyle).Text(_labels[ReportTextKeys.ColDate]);
                header.Cell().Element(HeaderCellStyle).AlignRight().Text(_labels[ReportTextKeys.ColCheckIns]);
                header.Cell().Element(HeaderCellStyle).AlignRight().Text(_labels[ReportTextKeys.ColCheckOuts]);
                header.Cell().Element(HeaderCellStyle).AlignRight().Text(_labels[ReportTextKeys.ColCash]);
                header.Cell().Element(HeaderCellStyle).AlignRight().Text(_labels[ReportTextKeys.ColVietQr]);
                header.Cell().Element(HeaderCellStyle).AlignRight().Text(_labels[ReportTextKeys.ColCard]);
                header.Cell().Element(HeaderCellStyle).AlignRight().Text(_labels[ReportTextKeys.ColRefund]);
                header.Cell().Element(HeaderCellStyle).AlignRight().Text(_labels[ReportTextKeys.ColNetRevenue]);
                header.Cell().Element(HeaderCellStyle).AlignRight().Text(_labels[ReportTextKeys.ColAvgDuration]);
            });

            int idx = 0;
            foreach (var d in daily)
            {
                var bg = idx++ % 2 == 1 ? "#F8FAFC" : "#FFFFFF";
                table.Cell().Element(c => DataCellStyle(c, bg)).Text(d.DayVn.ToString(DateFormat, CultureInfo.InvariantCulture));
                table.Cell().Element(c => DataCellStyle(c, bg)).AlignRight().Text(d.CheckIns.ToString("N0", CultureInfo.InvariantCulture));
                table.Cell().Element(c => DataCellStyle(c, bg)).AlignRight().Text(d.CheckOuts.ToString("N0", CultureInfo.InvariantCulture));
                table.Cell().Element(c => DataCellStyle(c, bg)).AlignRight().Text($"{d.CashRevenue:N0} đ");
                table.Cell().Element(c => DataCellStyle(c, bg)).AlignRight().Text($"{d.VietQrRevenue:N0} đ");
                table.Cell().Element(c => DataCellStyle(c, bg)).AlignRight().Text($"{d.CardRevenue:N0} đ");
                table.Cell().Element(c => DataCellStyle(c, bg)).AlignRight().Text($"{d.RefundTotal:N0} đ");
                table.Cell().Element(c => DataCellStyle(c, bg)).AlignRight().Text($"{d.NetRevenue:N0} đ");
                table.Cell().Element(c => DataCellStyle(c, bg)).AlignRight().Text(d.AverageDurationMinutes.HasValue ? $"{d.AverageDurationMinutes.Value:F1} p" : "-");
            }
        });
    }

    private void ComposeVehicleTypesTable(IContainer container, IReadOnlyList<VehicleTypeReportRow> vehicleTypes)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(2f);   // Loại xe
                columns.RelativeColumn(1.2f); // Lượt vào
                columns.RelativeColumn(1.2f); // Lượt ra
                columns.RelativeColumn(1.8f); // Doanh thu thuần
                columns.RelativeColumn(1.5f); // TG trung bình
            });

            table.Header(header =>
            {
                header.Cell().Element(HeaderCellStyle).Text(_labels[ReportTextKeys.ColVehicleType]);
                header.Cell().Element(HeaderCellStyle).AlignRight().Text(_labels[ReportTextKeys.ColCheckIns]);
                header.Cell().Element(HeaderCellStyle).AlignRight().Text(_labels[ReportTextKeys.ColCheckOuts]);
                header.Cell().Element(HeaderCellStyle).AlignRight().Text(_labels[ReportTextKeys.ColNetRevenue]);
                header.Cell().Element(HeaderCellStyle).AlignRight().Text(_labels[ReportTextKeys.ColAvgDuration]);
            });

            int idx = 0;
            foreach (var v in vehicleTypes)
            {
                var bg = idx++ % 2 == 1 ? "#F8FAFC" : "#FFFFFF";
                table.Cell().Element(c => DataCellStyle(c, bg)).Text(v.VehicleTypeName);
                table.Cell().Element(c => DataCellStyle(c, bg)).AlignRight().Text(v.CheckIns.ToString("N0", CultureInfo.InvariantCulture));
                table.Cell().Element(c => DataCellStyle(c, bg)).AlignRight().Text(v.CheckOuts.ToString("N0", CultureInfo.InvariantCulture));
                table.Cell().Element(c => DataCellStyle(c, bg)).AlignRight().Text($"{v.NetRevenue:N0} đ");
                table.Cell().Element(c => DataCellStyle(c, bg)).AlignRight().Text(v.AverageDurationMinutes.HasValue ? $"{v.AverageDurationMinutes.Value:F1} phút" : "-");
            }
        });
    }

    private void ComposeSessionsTable(IContainer container, IReadOnlyList<SessionDetailRow> rows)
    {
        var groupLabels = new Dictionary<ReportCustomerGroup, string>
        {
            [ReportCustomerGroup.Resident] = _labels[ReportTextKeys.GroupResident],
            [ReportCustomerGroup.MonthlyPass] = _labels[ReportTextKeys.GroupMonthlyPass],
            [ReportCustomerGroup.Visitor] = _labels[ReportTextKeys.GroupVisitor],
            [ReportCustomerGroup.All] = _labels[ReportTextKeys.GroupAll]
        };

        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(36);   // ID
                columns.RelativeColumn(1.1f); // Mã vé
                columns.RelativeColumn(1.2f); // Biển số
                columns.RelativeColumn(1.0f); // Loại xe
                columns.RelativeColumn(1.1f); // Nhóm khách
                columns.RelativeColumn(1.3f); // TG Vào
                columns.RelativeColumn(1.3f); // TG Ra
                columns.RelativeColumn(0.9f); // Số phút
                columns.RelativeColumn(1.1f); // Tiền phí
                columns.RelativeColumn(1.0f); // Phương thức
                columns.RelativeColumn(1.0f); // Trạng thái
            });

            table.Header(header =>
            {
                header.Cell().Element(HeaderCellStyle).Text(_labels[ReportTextKeys.ColSessionId]);
                header.Cell().Element(HeaderCellStyle).Text(_labels[ReportTextKeys.ColTicketCode]);
                header.Cell().Element(HeaderCellStyle).Text(_labels[ReportTextKeys.ColLicensePlate]);
                header.Cell().Element(HeaderCellStyle).Text(_labels[ReportTextKeys.ColVehicleType]);
                header.Cell().Element(HeaderCellStyle).Text(_labels[ReportTextKeys.ColCustomerGroup]);
                header.Cell().Element(HeaderCellStyle).Text(_labels[ReportTextKeys.ColCheckInTime]);
                header.Cell().Element(HeaderCellStyle).Text(_labels[ReportTextKeys.ColCheckOutTime]);
                header.Cell().Element(HeaderCellStyle).AlignRight().Text(_labels[ReportTextKeys.ColDurationMinutes]);
                header.Cell().Element(HeaderCellStyle).AlignRight().Text(_labels[ReportTextKeys.ColFee]);
                header.Cell().Element(HeaderCellStyle).AlignCenter().Text(_labels[ReportTextKeys.ColPaymentMethod]);
                header.Cell().Element(HeaderCellStyle).AlignCenter().Text(_labels[ReportTextKeys.ColStatus]);
            });

            int idx = 0;
            foreach (var r in rows)
            {
                var bg = idx++ % 2 == 1 ? "#F8FAFC" : "#FFFFFF";
                var grp = groupLabels.TryGetValue(r.CustomerGroup, out var gl) ? gl : r.CustomerGroup.ToString();
                var checkIn = AuditTime.ToVietnamTime(r.CheckInUtc).ToString(DateTimeFormat, CultureInfo.InvariantCulture);
                var checkOut = r.CheckOutUtc.HasValue ? AuditTime.ToVietnamTime(r.CheckOutUtc.Value).ToString(DateTimeFormat, CultureInfo.InvariantCulture) : "-";
                var dur = r.DurationMinutes.HasValue ? $"{r.DurationMinutes.Value:F0} p" : "-";
                var fee = $"{r.TotalFee:N0} đ";
                var method = PaymentLabel(r.PaymentMethod);
                var status = SessionStatusLabel(r.Status);

                table.Cell().Element(c => DataCellStyle(c, bg)).Text(r.SessionId.ToString(CultureInfo.InvariantCulture));
                table.Cell().Element(c => DataCellStyle(c, bg)).Text(r.TicketCode);
                table.Cell().Element(c => DataCellStyle(c, bg)).Text(r.LicensePlate);
                table.Cell().Element(c => DataCellStyle(c, bg)).Text(r.VehicleTypeName);
                table.Cell().Element(c => DataCellStyle(c, bg)).Text(grp);
                table.Cell().Element(c => DataCellStyle(c, bg)).Text(checkIn);
                table.Cell().Element(c => DataCellStyle(c, bg)).Text(checkOut);
                table.Cell().Element(c => DataCellStyle(c, bg)).AlignRight().Text(dur);
                table.Cell().Element(c => DataCellStyle(c, bg)).AlignRight().Text(fee);
                table.Cell().Element(c => DataCellStyle(c, bg)).AlignCenter().Text(method);
                table.Cell().Element(c => DataCellStyle(c, bg)).AlignCenter().Text(status);
            }
        });
    }

    private static IContainer HeaderCellStyle(IContainer container)
    {
        return container
            .Border(1).BorderColor("#CBD5E1")
            .Background("#F1F5F9")
            .PaddingVertical(4)
            .PaddingHorizontal(4)
            .AlignMiddle();
    }

    private static IContainer DataCellStyle(IContainer container, string bgColor)
    {
        return container
            .Border(1).BorderColor("#E2E8F0")
            .Background(bgColor)
            .PaddingVertical(3)
            .PaddingHorizontal(4)
            .AlignMiddle();
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

        return $"{_labels[ReportTextKeys.FilterVehicleType]}: {vehicle} | {_labels[ReportTextKeys.FilterCustomerGroup]}: {group} | {_labels[ReportTextKeys.FilterZone]}: {zone} | {_labels[ReportTextKeys.FilterStaff]}: {staff} | {_labels[ReportTextKeys.FilterShift]}: {shift} | {_labels[ReportTextKeys.FilterPaymentMethod]}: {method}";
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
}
