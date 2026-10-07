using SmartPS.Models.Parking;
using SmartPS.Services.Customers;

namespace SmartPS.Tests.Unit;

/// <summary>R4 / R7: display status of a ticket (active, expiring soon ≤ 7 days, expired by VN day, suspended, not started).</summary>
public class TicketStatusEvaluatorTests
{
    private static readonly DateTime Now = new(2026, 10, 8, 3, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Window_is_7_days()
    {
        Assert.Equal(TimeSpan.FromDays(7), TicketStatusEvaluator.ExpiringSoonWindow);
    }

    [Fact]
    public void Active_far_from_expiry_is_active()
    {
        Assert.Equal(TicketDisplayStatus.Active, TicketStatusEvaluator.Evaluate(MonthlyTicketStatus.Active, Now.AddDays(-5), Now.AddDays(30), Now));
    }

    [Fact]
    public void Expiring_soon_boundary_is_inclusive_at_7_days()
    {
        Assert.Equal(TicketDisplayStatus.ExpiringSoon, TicketStatusEvaluator.Evaluate(MonthlyTicketStatus.Active, Now.AddDays(-5), Now.AddDays(7), Now));
        Assert.Equal(TicketDisplayStatus.Active, TicketStatusEvaluator.Evaluate(MonthlyTicketStatus.Active, Now.AddDays(-5), Now.AddDays(7).AddSeconds(1), Now));
        Assert.Equal(TicketDisplayStatus.ExpiringSoon, TicketStatusEvaluator.Evaluate(MonthlyTicketStatus.Active, Now.AddDays(-5), Now.AddHours(1), Now));
    }

    [Fact]
    public void End_reached_is_expired()
    {
        Assert.Equal(TicketDisplayStatus.Expired, TicketStatusEvaluator.Evaluate(MonthlyTicketStatus.Active, Now.AddDays(-30), Now, Now));
        Assert.Equal(TicketDisplayStatus.Expired, TicketStatusEvaluator.Evaluate(MonthlyTicketStatus.Active, Now.AddDays(-30), Now.AddDays(-1), Now));
    }

    [Fact]
    public void Expired_status_is_expired_even_with_future_end()
    {
        Assert.Equal(TicketDisplayStatus.Expired, TicketStatusEvaluator.Evaluate(MonthlyTicketStatus.Expired, Now.AddDays(-5), Now.AddDays(20), Now));
    }

    [Fact]
    public void Suspended_is_suspended()
    {
        Assert.Equal(TicketDisplayStatus.Suspended, TicketStatusEvaluator.Evaluate(MonthlyTicketStatus.Suspended, Now.AddDays(-5), Now.AddDays(20), Now));
    }

    [Fact]
    public void Future_start_is_not_started()
    {
        Assert.Equal(TicketDisplayStatus.NotStarted, TicketStatusEvaluator.Evaluate(MonthlyTicketStatus.Active, Now.AddDays(1), Now.AddDays(31), Now));
    }

    private static MonthlyTicketDto Dto(int id, MonthlyTicketStatus status, TicketDisplayStatus display, DateTime start, DateTime end)
        => new(id, $"MT-{id}", 1, "51F12345", 1, "Plan", 2, start, end, 120000m, status, display, null);

    [Fact]
    public void PickPrimary_of_nothing_is_null()
    {
        Assert.Null(TicketStatusEvaluator.PickPrimary(Array.Empty<MonthlyTicketDto>()));
    }

    [Fact]
    public void PickPrimary_of_one_ticket_is_that_ticket()
    {
        var only = Dto(1, MonthlyTicketStatus.Active, TicketDisplayStatus.Expired, Now.AddDays(-40), Now.AddDays(-10));

        Assert.Equal(1, TicketStatusEvaluator.PickPrimary(new[] { only })!.TicketId);
    }

    [Fact]
    public void PickPrimary_prefers_a_currently_valid_ticket_over_an_expired_one()
    {
        var expired = Dto(1, MonthlyTicketStatus.Active, TicketDisplayStatus.Expired, Now.AddDays(-40), Now.AddDays(-10));
        var current = Dto(2, MonthlyTicketStatus.Active, TicketDisplayStatus.Active, Now.AddDays(-5), Now.AddDays(25));

        Assert.Equal(2, TicketStatusEvaluator.PickPrimary(new[] { expired, current })!.TicketId);
        Assert.Equal(2, TicketStatusEvaluator.PickPrimary(new[] { current, expired })!.TicketId);
    }
}
