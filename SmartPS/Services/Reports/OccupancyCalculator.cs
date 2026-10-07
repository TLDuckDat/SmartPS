using SmartPS.Models.Reports;

namespace SmartPS.Services.Reports;

public static class OccupancyCalculator
{
    /// <summary>
    /// Starts from <paramref name="initialOccupancy"/> at <paramref name="startUtc"/> and sweeps each UTC hour in
    /// [start, min(end, now)): occupancy += check-ins - check-outs, sampled at the end of the hour.
    /// Returns null percentages when capacity is zero or no hour has elapsed.
    /// </summary>
    public static OccupancyStats Compute(
        int initialOccupancy,
        IReadOnlyList<HourBucket> buckets,
        DateTime startUtc,
        DateTime endUtcExclusive,
        DateTime nowUtc,
        int capacity)
    {
        var limit = endUtcExclusive < nowUtc ? endUtcExclusive : nowUtc;
        if (capacity <= 0 || startUtc >= limit)
        {
            return new OccupancyStats(null, null, 0, null, Math.Max(capacity, 0));
        }

        var deltaByHour = new Dictionary<DateTime, int>();
        foreach (var bucket in buckets)
        {
            var key = HourStart(bucket.HourUtc);
            deltaByHour[key] = deltaByHour.GetValueOrDefault(key) + bucket.CheckIns - bucket.CheckOuts;
        }

        var occupancy = initialOccupancy;
        var samples = 0;
        var sum = 0.0;
        var peakVehicles = int.MinValue;
        DateTime? peakAt = null;

        for (var hour = HourStart(startUtc); hour < limit; hour = hour.AddHours(1))
        {
            occupancy += deltaByHour.GetValueOrDefault(hour);
            var shown = Math.Max(occupancy, 0);
            sum += shown * 100.0 / capacity;
            samples++;
            if (shown > peakVehicles)
            {
                peakVehicles = shown;
                peakAt = hour.AddHours(1);
            }
        }

        if (samples == 0)
        {
            return new OccupancyStats(null, null, 0, null, capacity);
        }

        return new OccupancyStats(
            Math.Round(sum / samples, 1),
            Math.Round(peakVehicles * 100.0 / capacity, 1),
            peakVehicles,
            peakAt,
            capacity);
    }

    private static DateTime HourStart(DateTime value)
        => new(value.Year, value.Month, value.Day, value.Hour, 0, 0, DateTimeKind.Utc);
}
