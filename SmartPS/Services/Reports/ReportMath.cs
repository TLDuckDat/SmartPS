using SmartPS.Models.Reports;

namespace SmartPS.Services.Reports;

public static class ReportMath
{
    /// <summary>Relative change in percent rounded to one decimal (away from zero); null when the previous value is zero.</summary>
    public static decimal? ChangePercent(decimal current, decimal previous)
    {
        if (previous == 0m)
        {
            return null;
        }

        return Math.Round((current - previous) / previous * 100m, 1, MidpointRounding.AwayFromZero);
    }

    public static KpiComparison Compare(decimal current, decimal previous)
    {
        var change = ChangePercent(current, previous);
        return new KpiComparison(current, previous, change, TrendOf(change));
    }

    public static KpiComparison? Compare(decimal? current, decimal? previous)
    {
        if (current is null)
        {
            return null;
        }

        if (previous is null)
        {
            return new KpiComparison(current.Value, null, null, KpiTrend.None);
        }

        return Compare(current.Value, previous.Value);
    }

    public static decimal? SharePercent(decimal part, decimal whole)
        => whole == 0m ? null : part * 100m / whole;

    private static KpiTrend TrendOf(decimal? change) => change switch
    {
        null => KpiTrend.None,
        > 0m => KpiTrend.Up,
        < 0m => KpiTrend.Down,
        _ => KpiTrend.Flat
    };
}
