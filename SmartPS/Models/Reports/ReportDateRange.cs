namespace SmartPS.Models.Reports;

/// <summary>Inclusive range of Vietnam calendar days.</summary>
public readonly record struct ReportDateRange(DateOnly FromVn, DateOnly ToVn)
{
    public int DayCount => ToVn.DayNumber - FromVn.DayNumber + 1;

    public string Key => $"{FromVn:yyyyMMdd}-{ToVn:yyyyMMdd}";

    public IEnumerable<DateOnly> Days()
    {
        for (var day = FromVn; day <= ToVn; day = day.AddDays(1))
        {
            yield return day;
        }
    }
}
