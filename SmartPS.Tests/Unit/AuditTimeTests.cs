namespace SmartPS.Tests.Unit;

/// <summary>R16 / AC-14: date filters are expressed in Vietnam time (UTC+7) regardless of the machine time zone.</summary>
public class AuditTimeTests
{
    [Fact]
    public void Vietnam_offset_is_plus_seven_hours()
    {
        Assert.Equal(TimeSpan.FromHours(7), AuditTime.VietnamOffset);
    }

    [Fact]
    public void VietnamDateStartUtc_is_previous_day_17_00_utc()
    {
        var start = AuditTime.VietnamDateStartUtc(new DateOnly(2026, 10, 7));

        Assert.Equal(new DateTime(2026, 10, 6, 17, 0, 0, DateTimeKind.Utc), start);
        Assert.Equal(DateTimeKind.Utc, start.Kind);
    }

    [Fact]
    public void VietnamDateStartUtc_crosses_year_boundary()
    {
        Assert.Equal(new DateTime(2025, 12, 31, 17, 0, 0, DateTimeKind.Utc),
            AuditTime.VietnamDateStartUtc(new DateOnly(2026, 1, 1)));
    }

    [Theory]
    [InlineData("2026-10-06T16:59:59Z", "2026-10-06T23:59:59")]
    [InlineData("2026-10-06T17:00:00Z", "2026-10-07T00:00:00")]
    [InlineData("2026-10-07T03:30:00Z", "2026-10-07T10:30:00")]
    public void ToVietnamTime_adds_seven_hours(string utcText, string expectedVnText)
    {
        var utc = DateTime.Parse(utcText, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);

        var vn = AuditTime.ToVietnamTime(utc);

        Assert.Equal(DateTime.Parse(expectedVnText, System.Globalization.CultureInfo.InvariantCulture), new DateTime(vn.Ticks));
    }

    [Fact]
    public void A_vietnam_day_spans_exactly_24_hours_in_utc()
    {
        var day = new DateOnly(2026, 10, 7);

        Assert.Equal(TimeSpan.FromDays(1), AuditTime.VietnamDateStartUtc(day.AddDays(1)) - AuditTime.VietnamDateStartUtc(day));
    }
}

/// <summary>R16 paging contract (pure parts of AuditQueryFilter / AuditPage).</summary>
public class AuditQueryContractTests
{
    [Fact]
    public void Default_page_size_is_50()
    {
        Assert.Equal(50, AuditQueryFilter.DefaultPageSize);
        Assert.Equal(50, new AuditQueryFilter().PageSize);
        Assert.Equal(0, new AuditQueryFilter().PageIndex);
    }

    [Fact]
    public void AuditPage_TotalPages_rounds_up()
    {
        Assert.Equal(2, new AuditPage(Array.Empty<AuditLog>(), 55, 0, 50).TotalPages);
        Assert.Equal(1, new AuditPage(Array.Empty<AuditLog>(), 50, 0, 50).TotalPages);
    }
}
