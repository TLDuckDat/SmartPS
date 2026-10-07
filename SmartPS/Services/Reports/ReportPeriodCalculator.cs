using SmartPS.Models.Reports;
using SmartPS.Services.Audit;

namespace SmartPS.Services.Reports;

/// <summary>Pure Vietnam-day (fixed UTC+7) boundary, preset and validation rules.</summary>
public static class ReportPeriodCalculator
{
    public static DateOnly ToVietnamDate(DateTime utc) => DateOnly.FromDateTime(AuditTime.ToVietnamTime(utc));

    public static int ToVietnamHour(DateTime utc) => AuditTime.ToVietnamTime(utc).Hour;

    public static DateOnly TodayVn(DateTime utcNow) => ToVietnamDate(utcNow);

    public static ReportRangeValidation Validate(DateOnly? from, DateOnly? to)
    {
        if (from is null || to is null)
        {
            return ReportRangeValidation.Missing;
        }

        if (from.Value > to.Value)
        {
            return ReportRangeValidation.FromAfterTo;
        }

        return to.Value.DayNumber - from.Value.DayNumber + 1 > ReportLimits.MaxRangeDays
            ? ReportRangeValidation.TooLong
            : ReportRangeValidation.Valid;
    }

    /// <exception cref="ArgumentException">A Custom range that fails <see cref="Validate"/>.</exception>
    public static ReportDateRange Resolve(ReportPeriodPreset preset, DateOnly todayVn, DateOnly? customFrom = null, DateOnly? customTo = null)
    {
        switch (preset)
        {
            case ReportPeriodPreset.Today:
                return new ReportDateRange(todayVn, todayVn);
            case ReportPeriodPreset.Yesterday:
                return new ReportDateRange(todayVn.AddDays(-1), todayVn.AddDays(-1));
            case ReportPeriodPreset.Last7Days:
                return new ReportDateRange(todayVn.AddDays(-6), todayVn);
            case ReportPeriodPreset.Last30Days:
                return new ReportDateRange(todayVn.AddDays(-29), todayVn);
            case ReportPeriodPreset.ThisMonth:
                return new ReportDateRange(new DateOnly(todayVn.Year, todayVn.Month, 1), todayVn);
            case ReportPeriodPreset.LastMonth:
            {
                var firstOfThisMonth = new DateOnly(todayVn.Year, todayVn.Month, 1);
                return new ReportDateRange(firstOfThisMonth.AddMonths(-1), firstOfThisMonth.AddDays(-1));
            }

            case ReportPeriodPreset.Custom:
            {
                var validation = Validate(customFrom, customTo);
                if (validation != ReportRangeValidation.Valid)
                {
                    throw new ArgumentException($"Invalid custom report range: {validation}.", nameof(customFrom));
                }

                return new ReportDateRange(customFrom!.Value, customTo!.Value);
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(preset), preset, null);
        }
    }

    /// <summary>The range of the same length that ends the day before <paramref name="range"/> starts.</summary>
    public static ReportDateRange PreviousOf(ReportDateRange range)
        => new(range.FromVn.AddDays(-range.DayCount), range.FromVn.AddDays(-1));

    /// <summary>Half-open UTC interval [start, end) covering the whole Vietnam range (both Kind = Utc).</summary>
    public static (DateTime StartUtc, DateTime EndUtcExclusive) ToUtcRange(ReportDateRange range)
        => (AuditTime.VietnamDateStartUtc(range.FromVn), AuditTime.VietnamDateStartUtc(range.ToVn.AddDays(1)));
}
