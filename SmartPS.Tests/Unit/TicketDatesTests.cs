using SmartPS.Models.Parking;
using SmartPS.Services.Customers;

namespace SmartPS.Tests.Unit;

/// <summary>R7 / A1: ticket boundaries are VN midnights stored as UTC, validity is [Start, End), renewal rules (AC-9 dates).</summary>
public class TicketDatesTests
{
    private static DateTime U(int y, int m, int d, int h = 0, int min = 0, int s = 0) => new(y, m, d, h, min, s, DateTimeKind.Utc);

    [Theory]
    [InlineData("2026-10-07T17:00:00Z", 2026, 10, 8)]
    [InlineData("2026-10-07T16:59:59Z", 2026, 10, 7)]
    [InlineData("2026-12-31T17:00:00Z", 2027, 1, 1)]
    public void TodayVn_uses_UTC_plus_7(string utc, int y, int m, int d)
    {
        var now = DateTime.Parse(utc, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal);
        Assert.Equal(new DateOnly(y, m, d), TicketDates.TodayVn(now));
    }

    [Fact]
    public void StartOfVnDayUtc_is_17_00_UTC_of_the_previous_day()
    {
        Assert.Equal(U(2026, 10, 7, 17), TicketDates.StartOfVnDayUtc(new DateOnly(2026, 10, 8)));
    }

    [Fact]
    public void ForNewTicket_3_months_from_2026_10_08()
    {
        var (start, end) = TicketDates.ForNewTicket(new DateOnly(2026, 10, 8), 3);

        Assert.Equal(U(2026, 10, 7, 17), start);
        Assert.Equal(U(2027, 1, 7, 17), end);
    }

    [Fact]
    public void ForNewTicket_from_Jan_31_plus_1_month_clamps_to_end_of_February()
    {
        var (start, end) = TicketDates.ForNewTicket(new DateOnly(2027, 1, 31), 1);

        Assert.Equal(U(2027, 1, 30, 17), start);
        Assert.Equal(U(2027, 2, 27, 17), end); // VN 2027-02-28 00:00
    }

    [Fact]
    public void ForRenewal_of_active_ticket_extends_from_current_end_and_keeps_start()
    {
        var start = U(2026, 9, 30, 17);   // VN 2026-10-01
        var end = U(2026, 10, 20, 17);    // VN 2026-10-21
        var now = U(2026, 10, 8, 3);

        var (newStart, newEnd) = TicketDates.ForRenewal(start, end, MonthlyTicketStatus.Active, 1, now);

        Assert.Equal(start, newStart);
        Assert.Equal(U(2026, 11, 20, 17), newEnd); // VN 2026-11-21
    }

    [Fact]
    public void ForRenewal_of_active_ticket_ending_Jan_31_VN()
    {
        var start = U(2026, 12, 31, 17);  // VN 2027-01-01
        var end = U(2027, 1, 30, 17);     // VN 2027-01-31
        var now = U(2027, 1, 10, 3);

        var (_, newEnd) = TicketDates.ForRenewal(start, end, MonthlyTicketStatus.Active, 1, now);

        Assert.Equal(U(2027, 2, 27, 17), newEnd); // VN 2027-02-28
    }

    [Fact]
    public void ForRenewal_of_ticket_expired_by_date_restarts_today_VN()
    {
        var start = U(2026, 8, 31, 17);
        var end = U(2026, 9, 30, 17);
        var now = U(2026, 10, 8, 3);      // VN 2026-10-08 10:00

        var (newStart, newEnd) = TicketDates.ForRenewal(start, end, MonthlyTicketStatus.Active, 3, now);

        Assert.Equal(U(2026, 10, 7, 17), newStart);
        Assert.Equal(U(2027, 1, 7, 17), newEnd);
    }

    [Fact]
    public void ForRenewal_when_end_equals_now_counts_as_expired()
    {
        var now = U(2026, 10, 7, 17);
        var (newStart, newEnd) = TicketDates.ForRenewal(U(2026, 9, 6, 17), now, MonthlyTicketStatus.Active, 1, now);

        Assert.Equal(U(2026, 10, 7, 17), newStart);
        Assert.Equal(U(2026, 11, 7, 17), newEnd);
    }

    [Fact]
    public void ForRenewal_with_Expired_status_restarts_today_even_if_end_is_in_the_future()
    {
        var now = U(2026, 10, 8, 3);
        var (newStart, newEnd) = TicketDates.ForRenewal(U(2026, 9, 30, 17), U(2026, 10, 30, 17), MonthlyTicketStatus.Expired, 1, now);

        Assert.Equal(U(2026, 10, 7, 17), newStart);
        Assert.Equal(U(2026, 11, 7, 17), newEnd);
    }

    [Fact]
    public void RenewalPurchasePeriod_active_is_old_end_to_new_end()
    {
        var oldEnd = U(2026, 10, 20, 17);
        var (s, e) = TicketDates.RenewalPurchasePeriod(oldEnd, wasExpired: false, newStartUtc: U(2026, 9, 30, 17), newEndUtc: U(2026, 11, 20, 17));

        Assert.Equal(oldEnd, s);
        Assert.Equal(U(2026, 11, 20, 17), e);
    }

    [Fact]
    public void RenewalPurchasePeriod_expired_is_new_start_to_new_end()
    {
        var (s, e) = TicketDates.RenewalPurchasePeriod(U(2026, 9, 30, 17), wasExpired: true, newStartUtc: U(2026, 10, 7, 17), newEndUtc: U(2026, 11, 7, 17));

        Assert.Equal(U(2026, 10, 7, 17), s);
        Assert.Equal(U(2026, 11, 7, 17), e);
    }

    [Fact]
    public void LastValidDateVn_is_the_VN_day_before_the_exclusive_end()
    {
        Assert.Equal(new DateOnly(2027, 1, 7), TicketDates.LastValidDateVn(U(2027, 1, 7, 17)));
        Assert.Equal(new DateOnly(2026, 10, 31), TicketDates.LastValidDateVn(U(2026, 10, 31, 17)));
    }

    [Fact]
    public void Overlaps_is_half_open()
    {
        var a = U(2026, 10, 1);
        var b = U(2026, 11, 1);
        var c = U(2026, 12, 1);

        Assert.False(TicketDates.Overlaps(a, b, b, c)); // adjacent
        Assert.False(TicketDates.Overlaps(b, c, a, b));
        Assert.True(TicketDates.Overlaps(a, c, b, c));
        Assert.True(TicketDates.Overlaps(a, b, a, b));
        Assert.True(TicketDates.Overlaps(a, c, a.AddDays(3), a.AddDays(4)));
        Assert.True(TicketDates.Overlaps(a, b.AddTicks(1), b, c));
        Assert.False(TicketDates.Overlaps(a, a.AddDays(1), c, c.AddDays(1)));
    }
}
