using SmartPS.Models.Parking;
using SmartPS.Models.Shifts;

namespace SmartPS.Tests.Unit;

/// <summary>
/// N2 / R2 / R4 metric rules over aggregate rows: VN day bucketing (AC-1, E2), revenue from FinancialTransaction (AC-3),
/// zero-filled days and hours, resident share, KPI comparison (AC-5, E5) and monthly-ticket sales (spec §6.1).
/// </summary>
public class ReportAggregatorTests
{
    private static readonly DateOnly D1 = new(2026, 10, 1);
    private static readonly DateTime D1StartUtc = new(2026, 9, 30, 17, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc);

    private static ReportDateRange Days(int count) => new(D1, D1.AddDays(count - 1));

    private static PeriodRawData Raw(
        ReportDateRange range,
        IReadOnlyList<HourBucket>? hours = null,
        IReadOnlyList<FinancialDayBucket>? financials = null,
        IReadOnlyList<GroupVehicleCount>? groups = null,
        int initialOccupancy = 0,
        int capacity = 10,
        MonthlyTicketSales? tickets = null)
        => new(range, hours ?? Array.Empty<HourBucket>(), financials ?? Array.Empty<FinancialDayBucket>(),
            groups ?? Array.Empty<GroupVehicleCount>(), initialOccupancy, capacity, tickets);

    private static HourBucket Hour(DateTime hourUtc, int checkIns = 0, int checkOuts = 0, int free = 0, double durationSeconds = 0)
        => new(hourUtc, checkIns, checkOuts, free, durationSeconds);

    private static FinancialDayBucket Ft(DateOnly day, FinancialTransactionType type, PaymentMethod method, decimal amount)
        => new(day, type, method, amount);

    private static PeriodSummary Summary(int checkIns = 0, decimal net = 0m, MonthlyTicketSales? tickets = null, int? peakHour = null)
        => new(checkIns, 0, 0, null, net, 0m, 0m, 0m, 0m, 0m, peakHour,
            new OccupancyStats(null, null, 0, null, 0), new CustomerGroupBreakdown(0, 0, 0), null, tickets);

    [Fact]
    public void AC1_hour_16Z_is_VN_hour_23_of_the_same_VN_day()
    {
        var data = Raw(Days(2), hours: new[] { Hour(new DateTime(2026, 10, 1, 16, 0, 0, DateTimeKind.Utc), checkIns: 1) });

        var daily = ReportAggregator.BuildDaily(data);
        var hourly = ReportAggregator.BuildHourly(data);

        Assert.Equal(1, daily.Single(d => d.DayVn == D1).CheckIns);
        Assert.Equal(0, daily.Single(d => d.DayVn == D1.AddDays(1)).CheckIns);
        Assert.Equal(1, hourly.Single(h => h.HourVn == 23).CheckIns);
        Assert.Equal(1, hourly.Sum(h => h.CheckIns));
    }

    [Fact]
    public void Hour_17Z_starts_the_next_VN_day_at_hour_0()
    {
        var data = Raw(Days(2), hours: new[] { Hour(new DateTime(2026, 10, 1, 17, 0, 0, DateTimeKind.Utc), checkIns: 2) });

        Assert.Equal(2, ReportAggregator.BuildDaily(data).Single(d => d.DayVn == D1.AddDays(1)).CheckIns);
        Assert.Equal(2, ReportAggregator.BuildHourly(data).Single(h => h.HourVn == 0).CheckIns);
    }

    [Fact]
    public void E2_overnight_session_counts_check_in_on_day_d_and_check_out_on_day_d_plus_1()
    {
        // check-in 23:00 VN on 01/10 (16:00Z), check-out 01:00 VN on 02/10 (18:00Z)
        var data = Raw(Days(2), hours: new[]
        {
            Hour(new DateTime(2026, 10, 1, 16, 0, 0, DateTimeKind.Utc), checkIns: 1),
            Hour(new DateTime(2026, 10, 1, 18, 0, 0, DateTimeKind.Utc), checkOuts: 1, durationSeconds: 7_200)
        });

        var daily = ReportAggregator.BuildDaily(data);

        Assert.Equal((1, 0), (daily[0].CheckIns, daily[0].CheckOuts));
        Assert.Equal((0, 1), (daily[1].CheckIns, daily[1].CheckOuts));
        Assert.Equal(120.0, daily[1].AverageDurationMinutes!.Value, 3);
        Assert.Null(daily[0].AverageDurationMinutes);
    }

    [Fact]
    public void AC3_net_revenue_is_fee_plus_refund_plus_adjustment_and_methods_are_parking_fees_only()
    {
        var data = Raw(Days(1), financials: new[]
        {
            Ft(D1, FinancialTransactionType.ParkingFee, PaymentMethod.Cash, 50_000m),
            Ft(D1, FinancialTransactionType.ParkingFee, PaymentMethod.VietQR, 30_000m),
            Ft(D1, FinancialTransactionType.ParkingFee, PaymentMethod.Free, 0m),
            Ft(D1, FinancialTransactionType.Refund, PaymentMethod.Cash, -10_000m),
            Ft(D1, FinancialTransactionType.Adjustment, PaymentMethod.Cash, 5_000m),
            Ft(D1, FinancialTransactionType.Incident, PaymentMethod.Cash, 99_000m)
        });

        var summary = ReportAggregator.Summarize(data, Now);
        var daily = ReportAggregator.BuildDaily(data);

        Assert.Equal(75_000m, summary.Net);
        Assert.Equal(50_000m, summary.Cash);
        Assert.Equal(30_000m, summary.VietQr);
        Assert.Equal(0m, summary.Card);
        Assert.Equal(-10_000m, summary.Refund);
        Assert.Equal(5_000m, summary.Adjustment);

        var day = Assert.Single(daily);
        Assert.Equal(75_000m, day.NetRevenue);
        Assert.Equal(50_000m, day.CashRevenue);
        Assert.Equal(30_000m, day.VietQrRevenue);
        Assert.Equal(0m, day.CardRevenue);
        Assert.Equal(-10_000m, day.RefundTotal);
        Assert.Equal(5_000m, day.AdjustmentTotal);
    }

    [Fact]
    public void Card_parking_fees_are_counted_as_card_revenue()
    {
        var data = Raw(Days(1), financials: new[] { Ft(D1, FinancialTransactionType.ParkingFee, PaymentMethod.Card, 12_000m) });

        var summary = ReportAggregator.Summarize(data, Now);

        Assert.Equal(12_000m, summary.Card);
        Assert.Equal(12_000m, summary.Net);
    }

    [Fact]
    public void E3_refund_alone_in_a_period_makes_net_negative()
    {
        var data = Raw(Days(1), financials: new[] { Ft(D1, FinancialTransactionType.Refund, PaymentMethod.VietQR, -40_000m) });

        var summary = ReportAggregator.Summarize(data, Now);

        Assert.Equal(-40_000m, summary.Net);
        Assert.Equal(-40_000m, summary.Refund);
        Assert.Equal(0m, summary.VietQr);
    }

    [Fact]
    public void Daily_rows_are_zero_filled_and_ordered_and_sum_to_the_summary()
    {
        var data = Raw(Days(3),
            hours: new[] { Hour(D1StartUtc.AddDays(2).AddHours(3), checkIns: 4) },
            financials: new[] { Ft(D1.AddDays(2), FinancialTransactionType.ParkingFee, PaymentMethod.Cash, 8_000m) });

        var daily = ReportAggregator.BuildDaily(data);
        var summary = ReportAggregator.Summarize(data, Now);

        Assert.Equal(new[] { D1, D1.AddDays(1), D1.AddDays(2) }, daily.Select(d => d.DayVn).ToArray());
        Assert.Equal(new[] { 0, 0, 4 }, daily.Select(d => d.CheckIns).ToArray());
        Assert.Equal(new[] { 0m, 0m, 8_000m }, daily.Select(d => d.NetRevenue).ToArray());
        Assert.Equal(summary.Net, daily.Sum(d => d.NetRevenue));
        Assert.Equal(summary.CheckIns, daily.Sum(d => d.CheckIns));
    }

    [Fact]
    public void Hourly_has_24_rows_with_averages_per_day_of_the_period()
    {
        // 3 check-ins at 08:00 VN (01:00Z) spread over a 2-day period, 1 check-out at 09:00 VN
        var data = Raw(Days(2), hours: new[]
        {
            Hour(D1StartUtc.AddHours(8), checkIns: 2),
            Hour(D1StartUtc.AddDays(1).AddHours(8), checkIns: 1),
            Hour(D1StartUtc.AddHours(9), checkOuts: 1, durationSeconds: 600)
        });

        var hourly = ReportAggregator.BuildHourly(data);

        Assert.Equal(Enumerable.Range(0, 24), hourly.Select(h => h.HourVn));
        var eight = hourly.Single(h => h.HourVn == 8);
        Assert.Equal(3, eight.CheckIns);
        Assert.Equal(1.5, eight.AverageCheckInsPerDay, 6);
        var nine = hourly.Single(h => h.HourVn == 9);
        Assert.Equal(1, nine.CheckOuts);
        Assert.Equal(0.5, nine.AverageCheckOutsPerDay, 6);
        Assert.All(hourly.Where(h => h.HourVn is not 8 and not 9), h => Assert.Equal(0, h.CheckIns + h.CheckOuts));
    }

    [Fact]
    public void Summary_counts_check_ins_outs_free_duration_and_peak_hour()
    {
        var data = Raw(Days(1), hours: new[]
        {
            Hour(D1StartUtc.AddHours(8), checkIns: 3),
            Hour(D1StartUtc.AddHours(17), checkIns: 3),
            Hour(D1StartUtc.AddHours(9), checkIns: 1, checkOuts: 2, free: 1, durationSeconds: 7_200)
        });

        var summary = ReportAggregator.Summarize(data, Now);

        Assert.Equal(7, summary.CheckIns);
        Assert.Equal(2, summary.CheckOuts);
        Assert.Equal(1, summary.FreeCheckOuts);
        Assert.Equal(60.0, summary.AverageDurationMinutes!.Value, 6);
        Assert.Equal(8, summary.PeakHour);
    }

    [Fact]
    public void E1_empty_period_summarizes_to_zero_with_null_averages()
    {
        var data = Raw(Days(7));

        var summary = ReportAggregator.Summarize(data, Now);

        Assert.Equal(0, summary.CheckIns);
        Assert.Equal(0, summary.CheckOuts);
        Assert.Equal(0m, summary.Net);
        Assert.Null(summary.AverageDurationMinutes);
        Assert.Null(summary.PeakHour);
        Assert.Equal(0, summary.Groups.Total);
        Assert.Null(summary.ResidentSharePercent);
        Assert.Equal(7, ReportAggregator.BuildDaily(data).Count);
        Assert.Equal(24, ReportAggregator.BuildHourly(data).Count);
    }

    [Fact]
    public void Resident_share_comes_from_group_counts()
    {
        var data = Raw(Days(1), groups: new[]
        {
            new GroupVehicleCount(1, ReportCustomerGroup.Resident, 3),
            new GroupVehicleCount(1, ReportCustomerGroup.MonthlyPass, 2),
            new GroupVehicleCount(2, ReportCustomerGroup.Visitor, 4),
            new GroupVehicleCount(1, ReportCustomerGroup.Visitor, 1)
        });

        var summary = ReportAggregator.Summarize(data, Now);

        Assert.Equal(new CustomerGroupBreakdown(3, 2, 5), summary.Groups);
        Assert.Equal(10, summary.Groups.Total);
        Assert.Equal(30m, summary.ResidentSharePercent);
    }

    [Fact]
    public void Occupancy_uses_initial_occupancy_capacity_and_hours()
    {
        var data = Raw(Days(1), hours: new[] { Hour(D1StartUtc, checkIns: 3), Hour(D1StartUtc.AddHours(1), checkOuts: 1) },
            initialOccupancy: 2, capacity: 10);

        var summary = ReportAggregator.Summarize(data, D1StartUtc.AddHours(3));

        Assert.Equal(10, summary.Occupancy.Capacity);
        Assert.Equal(43.3, summary.Occupancy.AveragePercent!.Value, 1);
        Assert.Equal(50.0, summary.Occupancy.PeakPercent!.Value, 1);
    }

    [Fact]
    public void BuildKpis_compares_with_the_previous_period()
    {
        var kpis = ReportAggregator.BuildKpis(Summary(checkIns: 120, net: 50_000m, peakHour: 8), Summary(checkIns: 100, net: 40_000m, peakHour: 17), vehiclesInLotNow: 7);

        Assert.Equal(120m, kpis.CheckIns.Current);
        Assert.Equal(100m, kpis.CheckIns.Previous);
        Assert.Equal(20m, kpis.CheckIns.ChangePercent);
        Assert.Equal(KpiTrend.Up, kpis.CheckIns.Trend);
        Assert.Equal(25m, kpis.NetRevenue.ChangePercent);
        Assert.Equal(7, kpis.VehiclesInLotNow);
        Assert.Equal(8, kpis.PeakHour);
        Assert.Equal(17, kpis.PreviousPeakHour);
    }

    [Fact]
    public void E5_BuildKpis_with_an_empty_previous_period_has_null_changes()
    {
        var kpis = ReportAggregator.BuildKpis(Summary(checkIns: 120, net: 50_000m), Summary(), vehiclesInLotNow: 0);

        Assert.Null(kpis.CheckIns.ChangePercent);
        Assert.Equal(KpiTrend.None, kpis.CheckIns.Trend);
        Assert.Null(kpis.NetRevenue.ChangePercent);
        Assert.Equal("—", ReportFormat.Change(kpis.CheckIns));
    }

    [Fact]
    public void Monthly_ticket_sales_split_and_total_comparison()
    {
        var current = new MonthlyTicketSales(120_000m, 1, 300_000m, 1);
        var previous = new MonthlyTicketSales(300_000m, 1, 0m, 0);

        var kpis = ReportAggregator.BuildKpis(Summary(tickets: current), Summary(tickets: previous), 0);

        Assert.NotNull(kpis.MonthlyTicketRevenue);
        Assert.Equal(420_000m, kpis.MonthlyTicketRevenue!.Current);
        Assert.Equal(300_000m, kpis.MonthlyTicketRevenue.Previous);
        Assert.Equal(40m, kpis.MonthlyTicketRevenue.ChangePercent);
        Assert.Equal(current, kpis.MonthlyTicketSales);
    }

    [Fact]
    public void Monthly_ticket_sales_null_passes_through_as_null()
    {
        var kpis = ReportAggregator.BuildKpis(Summary(tickets: null), Summary(tickets: null), 0);

        Assert.Null(kpis.MonthlyTicketRevenue);
        Assert.Null(kpis.MonthlyTicketSales);
    }

    [Fact]
    public void Summarize_carries_monthly_ticket_sales_from_raw_data()
    {
        var sales = new MonthlyTicketSales(1m, 1, 2m, 2);

        Assert.Equal(sales, ReportAggregator.Summarize(Raw(Days(1), tickets: sales), Now).MonthlyTickets);
        Assert.Null(ReportAggregator.Summarize(Raw(Days(1), tickets: null), Now).MonthlyTickets);
    }

    [Fact]
    public void MonthlyTicketSales_totals_and_empty()
    {
        var sales = new MonthlyTicketSales(120_000m, 1, 300_000m, 2);

        Assert.Equal(420_000m, sales.TotalRevenue);
        Assert.Equal(3, sales.TotalCount);
        Assert.Equal(0m, MonthlyTicketSales.Empty.TotalRevenue);
        Assert.Equal(0, MonthlyTicketSales.Empty.TotalCount);
    }

    [Fact]
    public void Vehicle_type_rows_are_zero_filled_over_all_types()
    {
        var types = new[] { new LookupItem(1, "Xe máy"), new LookupItem(2, "Xe ô tô"), new LookupItem(3, "Xe đạp / Xe điện") };
        var checkIns = new[]
        {
            new GroupVehicleCount(1, ReportCustomerGroup.Resident, 2),
            new GroupVehicleCount(1, ReportCustomerGroup.Visitor, 1)
        };
        var checkOuts = new[] { new VehicleCheckOutStat(1, 2, 3_600) };
        var revenue = new[] { new VehicleRevenue(1, 10_000m) };

        var rows = ReportAggregator.BuildVehicleTypes(types, checkIns, checkOuts, revenue);

        Assert.Equal(3, rows.Count);
        var moto = rows.Single(r => r.VehicleTypeId == 1);
        Assert.Equal("Xe máy", moto.VehicleTypeName);
        Assert.Equal(3, moto.CheckIns);
        Assert.Equal(2, moto.CheckOuts);
        Assert.Equal(10_000m, moto.NetRevenue);
        Assert.Equal(30.0, moto.AverageDurationMinutes!.Value, 6);
        Assert.All(rows.Where(r => r.VehicleTypeId != 1), r =>
        {
            Assert.Equal(0, r.CheckIns);
            Assert.Equal(0, r.CheckOuts);
            Assert.Equal(0m, r.NetRevenue);
            Assert.Null(r.AverageDurationMinutes);
        });
    }

    [Fact]
    public void Customer_group_breakdown_total()
    {
        Assert.Equal(9, new CustomerGroupBreakdown(3, 2, 4).Total);
    }

    [Fact]
    public void Zone_occupancy_percent_is_null_without_slots()
    {
        Assert.Null(new ZoneOccupancyRow(1, "Z", 0, 0).OccupancyPercent);
        Assert.Equal(33.3, new ZoneOccupancyRow(1, "Z", 1, 3).OccupancyPercent);
    }
}
