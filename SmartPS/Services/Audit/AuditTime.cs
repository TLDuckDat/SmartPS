namespace SmartPS.Services.Audit;

/// <summary>Vietnam-time (UTC+7) helpers for audit filtering and display.</summary>
public static class AuditTime
{
    public static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public static DateTime VietnamDateStartUtc(DateOnly vietnamDate)
    {
        var localMidnight = new DateTime(vietnamDate.Year, vietnamDate.Month, vietnamDate.Day, 0, 0, 0, DateTimeKind.Utc);
        return localMidnight - VietnamOffset;
    }

    public static DateTime ToVietnamTime(DateTime utc)
    {
        var value = utc.Kind == DateTimeKind.Local ? utc.ToUniversalTime() : utc;
        return DateTime.SpecifyKind(value + VietnamOffset, DateTimeKind.Unspecified);
    }
}
