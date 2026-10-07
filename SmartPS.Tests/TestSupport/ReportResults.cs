using SmartPS.Models.Parking;
using SmartPS.Models.Shifts;

namespace SmartPS.Tests.TestSupport;

/// <summary>
/// Synthetic, internally consistent <see cref="ReportResult"/> instances for unit tests (chart mapper, workbook writer,
/// view model). Range of <see cref="Sample"/>: 01/10/2026 – 03/10/2026 (VN), 15 check-ins, net revenue 105,000.
/// </summary>
public static class ReportResults
{
    public static readonly DateOnly Day1 = new(2026, 10, 1);
    public static readonly ReportDateRange SampleRange = new(Day1, Day1.AddDays(2));

    /// <summary>01/10/2026 16:30Z = 23:30 VN on 01/10 (AC-1 / E4).</summary>
    public static readonly DateTime LateEveningUtc = new(2026, 10, 1, 16, 30, 0, DateTimeKind.Utc);

    public const decimal SampleNetRevenue = 105_000m;
    public const int SampleCheckIns = 15;
    public const int SampleCheckOuts = 14;

    public static KpiComparison Cmp(decimal current, decimal? previous, decimal? change, KpiTrend trend)
        => new(current, previous, change, trend);

    public static ReportKpis SampleKpis(KpiComparison? checkIns = null, decimal netRevenue = SampleNetRevenue, bool zoneFiltered = false)
        => new(
            CheckIns: checkIns ?? Cmp(SampleCheckIns, 12, 25m, KpiTrend.Up),
            CheckOuts: Cmp(SampleCheckOuts, 14, 0m, KpiTrend.Flat),
            VehiclesInLotNow: 4,
            NetRevenue: Cmp(netRevenue, 84_000m, 25m, KpiTrend.Up),
            CashRevenue: Cmp(70_000m, 80_000m, -12.5m, KpiTrend.Down),
            VietQrRevenue: Cmp(30_000m, 0m, null, KpiTrend.None),
            CardRevenue: Cmp(10_000m, 4_000m, 150m, KpiTrend.Up),
            RefundTotal: -10_000m,
            AdjustmentTotal: 5_000m,
            FreeCheckOuts: Cmp(1, 1, 0m, KpiTrend.Flat),
            AverageDurationMinutes: Cmp(38.6m, 40m, -3.5m, KpiTrend.Down),
            PeakHour: 8,
            PreviousPeakHour: 17,
            AverageOccupancyPercent: Cmp(22.5m, 20m, 12.5m, KpiTrend.Up),
            PeakOccupancyPercent: Cmp(60m, 50m, 20m, KpiTrend.Up),
            ResidentSharePercent: Cmp(26.7m, 25m, 6.8m, KpiTrend.Up),
            VisitorSharePercent: 73.3m,
            MonthlyTicketRevenue: zoneFiltered ? null : Cmp(420_000m, 300_000m, 40m, KpiTrend.Up),
            MonthlyTicketSales: zoneFiltered ? null : new MonthlyTicketSales(120_000m, 1, 300_000m, 1));

    public static IReadOnlyList<DailyReportRow> SampleDaily() => new[]
    {
        new DailyReportRow(Day1, 10, 8, 50_000m, 30_000m, 0m, -10_000m, 5_000m, 75_000m, 1, 45.0),
        new DailyReportRow(Day1.AddDays(1), 5, 6, 20_000m, 0m, 10_000m, 0m, 0m, 30_000m, 0, 30.0),
        new DailyReportRow(Day1.AddDays(2), 0, 0, 0m, 0m, 0m, 0m, 0m, 0m, 0, null)
    };

    /// <summary>24 rows: check-ins at 08h (6), 17h (5), 23h (4); check-outs at 09h (7), 18h (7). Averages over 3 days.</summary>
    public static IReadOnlyList<HourlyReportRow> SampleHourly()
    {
        var rows = new List<HourlyReportRow>();
        for (var h = 0; h < 24; h++)
        {
            var ins = h switch { 8 => 6, 17 => 5, 23 => 4, _ => 0 };
            var outs = h switch { 9 => 7, 18 => 7, _ => 0 };
            rows.Add(new HourlyReportRow(h, ins, outs, ins / 3.0, outs / 3.0));
        }

        return rows;
    }

    public static IReadOnlyList<ShiftReportRow> SampleShifts() => new[]
    {
        new ShiftReportRow(7, "Nguyễn Văn An", new DateTime(2026, 10, 1, 1, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc),
            100_000m, 150_000m, 149_000m, -1_000m, ShiftStatus.Locked),
        new ShiftReportRow(8, "Trần Thị Bình", new DateTime(2026, 10, 2, 1, 0, 0, DateTimeKind.Utc), null,
            100_000m, null, null, null, ShiftStatus.Active)
    };

    public static IReadOnlyList<VehicleTypeReportRow> SampleVehicleTypes() => new[]
    {
        new VehicleTypeReportRow(1, "Xe máy", 10, 9, 80_000m, 40.0),
        new VehicleTypeReportRow(2, "Xe ô tô", 5, 5, 25_000m, 35.0),
        new VehicleTypeReportRow(3, "Xe đạp / Xe điện", 0, 0, 0m, null)
    };

    public static IReadOnlyList<TopPlateRow> SampleTopPlates() => new[]
    {
        new TopPlateRow(1, "29A-123.45", 3, new DateTime(2026, 10, 2, 2, 0, 0, DateTimeKind.Utc)),
        new TopPlateRow(2, "30F-999.99", 2, new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc))
    };

    public static IReadOnlyList<ZoneOccupancyRow> SampleZones() => new[]
    {
        new ZoneOccupancyRow(1, "Khu A", 3, 10),
        new ZoneOccupancyRow(2, "Khu B", 0, 5)
    };

    public static CustomerGroupBreakdown SampleGroups() => new(4, 3, 8);

    public static ReportResult Sample(KpiComparison? checkIns = null, ReportFilter? filter = null)
        => new(
            Filter: filter ?? new ReportFilter(SampleRange),
            PreviousRange: new ReportDateRange(Day1.AddDays(-3), Day1.AddDays(-1)),
            GeneratedAtUtc: new DateTime(2026, 10, 3, 5, 0, 0, DateTimeKind.Utc),
            Kpis: SampleKpis(checkIns),
            Daily: SampleDaily(),
            Hourly: SampleHourly(),
            Shifts: SampleShifts(),
            VehicleTypes: SampleVehicleTypes(),
            TopPlates: SampleTopPlates(),
            Zones: SampleZones(),
            CustomerGroups: SampleGroups());

    /// <summary>E1: a period without any activity (zero-filled days, 24 zero hours, no zones, no shifts).</summary>
    public static ReportResult Empty(ReportDateRange? range = null)
    {
        var r = range ?? SampleRange;
        var zero = Cmp(0, 0, null, KpiTrend.None);
        var kpis = new ReportKpis(
            CheckIns: zero, CheckOuts: zero, VehiclesInLotNow: 0,
            NetRevenue: zero, CashRevenue: zero, VietQrRevenue: zero, CardRevenue: zero,
            RefundTotal: 0m, AdjustmentTotal: 0m,
            FreeCheckOuts: zero, AverageDurationMinutes: null,
            PeakHour: null, PreviousPeakHour: null,
            AverageOccupancyPercent: null, PeakOccupancyPercent: null,
            ResidentSharePercent: null, VisitorSharePercent: null,
            MonthlyTicketRevenue: zero, MonthlyTicketSales: MonthlyTicketSales.Empty);
        return new ReportResult(
            Filter: new ReportFilter(r),
            PreviousRange: ReportPeriodCalculator.PreviousOf(r),
            GeneratedAtUtc: new DateTime(2026, 10, 3, 5, 0, 0, DateTimeKind.Utc),
            Kpis: kpis,
            Daily: r.Days().Select(d => new DailyReportRow(d, 0, 0, 0m, 0m, 0m, 0m, 0m, 0m, 0, null)).ToList(),
            Hourly: Enumerable.Range(0, 24).Select(h => new HourlyReportRow(h, 0, 0, 0, 0)).ToList(),
            Shifts: Array.Empty<ShiftReportRow>(),
            VehicleTypes: SampleVehicleTypes().Select(v => v with { CheckIns = 0, CheckOuts = 0, NetRevenue = 0m, AverageDurationMinutes = null }).ToList(),
            TopPlates: Array.Empty<TopPlateRow>(),
            Zones: Array.Empty<ZoneOccupancyRow>(),
            CustomerGroups: new CustomerGroupBreakdown(0, 0, 0));
    }

    /// <summary>Session detail rows; the first row checks in at <see cref="LateEveningUtc"/> (23:30 VN).</summary>
    public static SessionDetailPage Sessions(int count = 3, bool truncated = false, int? totalCount = null)
    {
        var rows = Enumerable.Range(0, count).Select(i => new SessionDetailRow(
            SessionId: 1000 + i,
            TicketCode: $"TK-{1000 + i}",
            LicensePlate: $"29A-{100 + i}.{10 + i}",
            VehicleTypeName: "Xe máy",
            CustomerGroup: (ReportCustomerGroup)(1 + i % 3),
            ZoneName: i % 2 == 0 ? "Khu A" : null,
            SlotCode: i % 2 == 0 ? $"A-{i + 1:D2}" : null,
            CheckInUtc: LateEveningUtc.AddMinutes(i * 7),
            CheckOutUtc: i == count - 1 && count > 1 ? null : LateEveningUtc.AddMinutes(i * 7 + 60),
            DurationMinutes: i == count - 1 && count > 1 ? null : 60,
            Status: i == count - 1 && count > 1 ? SessionStatus.Active : SessionStatus.Completed,
            TotalFee: i == count - 1 && count > 1 ? 0m : 5_000m,
            PaymentMethod: PaymentMethod.Cash)).ToList();
        return new SessionDetailPage(rows, totalCount ?? count, truncated);
    }
}
