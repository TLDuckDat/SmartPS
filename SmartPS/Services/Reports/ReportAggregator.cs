using SmartPS.Models.Parking;
using SmartPS.Models.Reports;
using SmartPS.Models.Shifts;

namespace SmartPS.Services.Reports;

/// <summary>
/// Pure metric rules over the small aggregate sets that PostgreSQL returns. SQL only filters and groups;
/// Vietnam day/hour bucketing, peak hour, occupancy and comparisons happen here (spec N2).
/// </summary>
public static class ReportAggregator
{
    public static PeriodSummary Summarize(PeriodRawData data, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(data);

        var checkIns = 0;
        var checkOuts = 0;
        var free = 0;
        var seconds = 0.0;
        var byHour = new int[24];
        foreach (var hour in data.Hours)
        {
            checkIns += hour.CheckIns;
            checkOuts += hour.CheckOuts;
            free += hour.FreeCheckOuts;
            seconds += hour.CheckOutDurationSeconds;
            byHour[ReportPeriodCalculator.ToVietnamHour(hour.HourUtc)] += hour.CheckIns;
        }

        var money = SumMoney(data.Financials);

        var groups = new CustomerGroupBreakdown(
            data.GroupVehicleCounts.Where(g => g.Group == ReportCustomerGroup.Resident).Sum(g => g.CheckIns),
            data.GroupVehicleCounts.Where(g => g.Group == ReportCustomerGroup.MonthlyPass).Sum(g => g.CheckIns),
            data.GroupVehicleCounts.Where(g => g.Group == ReportCustomerGroup.Visitor).Sum(g => g.CheckIns));

        var share = ReportMath.SharePercent(groups.Resident, groups.Total);
        var (start, end) = ReportPeriodCalculator.ToUtcRange(data.Range);

        return new PeriodSummary(
            checkIns,
            checkOuts,
            free,
            AverageMinutes(seconds, checkOuts),
            money.Net,
            money.Cash,
            money.VietQr,
            money.Card,
            money.Refund,
            money.Adjustment,
            PeakHourCalculator.FindPeakHour(byHour),
            OccupancyCalculator.Compute(data.InitialOccupancy, data.Hours, start, end, nowUtc, data.Capacity),
            groups,
            share is null ? null : Math.Round(share.Value, 1, MidpointRounding.AwayFromZero),
            data.MonthlyTickets);
    }

    public static IReadOnlyList<DailyReportRow> BuildDaily(PeriodRawData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var hoursByDay = data.Hours
            .GroupBy(h => ReportPeriodCalculator.ToVietnamDate(h.HourUtc))
            .ToDictionary(g => g.Key);
        var moneyByDay = data.Financials
            .GroupBy(f => f.DayVn)
            .ToDictionary(g => g.Key, g => SumMoney(g));

        var rows = new List<DailyReportRow>(data.Range.DayCount);
        foreach (var day in data.Range.Days())
        {
            var checkIns = 0;
            var checkOuts = 0;
            var free = 0;
            var seconds = 0.0;
            if (hoursByDay.TryGetValue(day, out var hours))
            {
                foreach (var hour in hours)
                {
                    checkIns += hour.CheckIns;
                    checkOuts += hour.CheckOuts;
                    free += hour.FreeCheckOuts;
                    seconds += hour.CheckOutDurationSeconds;
                }
            }

            var money = moneyByDay.TryGetValue(day, out var m) ? m : Money.Zero;
            rows.Add(new DailyReportRow(
                day, checkIns, checkOuts, money.Cash, money.VietQr, money.Card, money.Refund, money.Adjustment, money.Net,
                free, AverageMinutes(seconds, checkOuts)));
        }

        return rows;
    }

    public static IReadOnlyList<HourlyReportRow> BuildHourly(PeriodRawData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var ins = new int[24];
        var outs = new int[24];
        foreach (var hour in data.Hours)
        {
            var vnHour = ReportPeriodCalculator.ToVietnamHour(hour.HourUtc);
            ins[vnHour] += hour.CheckIns;
            outs[vnHour] += hour.CheckOuts;
        }

        var days = Math.Max(data.Range.DayCount, 1);
        return Enumerable.Range(0, 24)
            .Select(h => new HourlyReportRow(h, ins[h], outs[h], (double)ins[h] / days, (double)outs[h] / days))
            .ToList();
    }

    public static ReportKpis BuildKpis(PeriodSummary current, PeriodSummary previous, int vehiclesInLotNow)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(previous);

        var residentShare = ReportMath.Compare(current.ResidentSharePercent, previous.ResidentSharePercent);
        var currentTickets = current.MonthlyTickets;

        return new ReportKpis(
            CheckIns: ReportMath.Compare(current.CheckIns, previous.CheckIns),
            CheckOuts: ReportMath.Compare(current.CheckOuts, previous.CheckOuts),
            VehiclesInLotNow: vehiclesInLotNow,
            NetRevenue: ReportMath.Compare(current.Net, previous.Net),
            CashRevenue: ReportMath.Compare(current.Cash, previous.Cash),
            VietQrRevenue: ReportMath.Compare(current.VietQr, previous.VietQr),
            CardRevenue: ReportMath.Compare(current.Card, previous.Card),
            RefundTotal: current.Refund,
            AdjustmentTotal: current.Adjustment,
            FreeCheckOuts: ReportMath.Compare(current.FreeCheckOuts, previous.FreeCheckOuts),
            AverageDurationMinutes: ReportMath.Compare(ToDecimal(current.AverageDurationMinutes), ToDecimal(previous.AverageDurationMinutes)),
            PeakHour: current.PeakHour,
            PreviousPeakHour: previous.PeakHour,
            AverageOccupancyPercent: ReportMath.Compare(ToDecimal(current.Occupancy.AveragePercent), ToDecimal(previous.Occupancy.AveragePercent)),
            PeakOccupancyPercent: ReportMath.Compare(ToDecimal(current.Occupancy.PeakPercent), ToDecimal(previous.Occupancy.PeakPercent)),
            ResidentSharePercent: residentShare,
            VisitorSharePercent: current.ResidentSharePercent is null ? null : 100m - current.ResidentSharePercent.Value,
            MonthlyTicketRevenue: currentTickets is null
                ? null
                : ReportMath.Compare(currentTickets.TotalRevenue, previous.MonthlyTickets?.TotalRevenue),
            MonthlyTicketSales: currentTickets);
    }

    public static IReadOnlyList<VehicleTypeReportRow> BuildVehicleTypes(
        IReadOnlyList<LookupItem> vehicleTypes,
        IReadOnlyList<GroupVehicleCount> checkIns,
        IReadOnlyList<VehicleCheckOutStat> checkOuts,
        IReadOnlyList<VehicleRevenue> revenue)
    {
        var rows = new List<VehicleTypeReportRow>(vehicleTypes.Count);
        foreach (var type in vehicleTypes)
        {
            var ins = checkIns.Where(c => c.VehicleTypeId == type.Id).Sum(c => c.CheckIns);
            var outs = checkOuts.Where(c => c.VehicleTypeId == type.Id).ToList();
            var outCount = outs.Sum(o => o.CheckOuts);
            var seconds = outs.Sum(o => o.DurationSeconds);
            var net = revenue.Where(r => r.VehicleTypeId == type.Id).Sum(r => r.NetRevenue);
            rows.Add(new VehicleTypeReportRow(type.Id, type.Name, ins, outCount, net, AverageMinutes(seconds, outCount)));
        }

        return rows;
    }

    private static double? AverageMinutes(double seconds, int checkOuts)
        => checkOuts <= 0 ? null : seconds / checkOuts / 60.0;

    private static decimal? ToDecimal(double? value)
        => value is null ? null : Math.Round((decimal)value.Value, 1, MidpointRounding.AwayFromZero);

    private static Money SumMoney(IEnumerable<FinancialDayBucket> buckets)
    {
        decimal net = 0m, cash = 0m, vietQr = 0m, card = 0m, refund = 0m, adjustment = 0m;
        foreach (var bucket in buckets)
        {
            switch (bucket.Type)
            {
                case FinancialTransactionType.ParkingFee:
                    net += bucket.Amount;
                    switch (bucket.Method)
                    {
                        case PaymentMethod.Cash:
                            cash += bucket.Amount;
                            break;
                        case PaymentMethod.VietQR:
                            vietQr += bucket.Amount;
                            break;
                        case PaymentMethod.Card:
                            card += bucket.Amount;
                            break;
                    }

                    break;
                case FinancialTransactionType.Refund:
                    net += bucket.Amount;
                    refund += bucket.Amount;
                    break;
                case FinancialTransactionType.Adjustment:
                    net += bucket.Amount;
                    adjustment += bucket.Amount;
                    break;
            }
        }

        return new Money(net, cash, vietQr, card, refund, adjustment);
    }

    private readonly record struct Money(decimal Net, decimal Cash, decimal VietQr, decimal Card, decimal Refund, decimal Adjustment)
    {
        public static Money Zero => default;
    }
}
