namespace SmartPS.Tests.TestSupport;

/// <summary>TimeProvider frozen at a given UTC instant (settable), for "today (VN)" and "now" dependent report logic.</summary>
public sealed class FixedTimeProvider : TimeProvider
{
    public FixedTimeProvider(DateTime utcNow)
    {
        Now = new DateTimeOffset(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc));
    }

    public DateTimeOffset Now { get; set; }

    public override DateTimeOffset GetUtcNow() => Now;
}
