namespace SmartPS.Services.Reports;

public static class PeakHourCalculator
{
    /// <summary>Hour (0-23) with the most check-ins; ties go to the earlier hour; all zero gives null.</summary>
    public static int? FindPeakHour(IReadOnlyList<int> checkInsByVnHour)
    {
        ArgumentNullException.ThrowIfNull(checkInsByVnHour);
        if (checkInsByVnHour.Count != 24)
        {
            throw new ArgumentException("Exactly 24 hourly buckets are required.", nameof(checkInsByVnHour));
        }

        int? peak = null;
        var best = 0;
        for (var hour = 0; hour < 24; hour++)
        {
            if (checkInsByVnHour[hour] > best)
            {
                best = checkInsByVnHour[hour];
                peak = hour;
            }
        }

        return peak;
    }
}
