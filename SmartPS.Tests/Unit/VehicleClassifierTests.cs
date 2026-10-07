using SmartPS.Models.GateControl;
using SmartPS.Models.Parking;
using SmartPS.Services.GateControl;

namespace SmartPS.Tests.Unit;

/// <summary>R10 classification precedence, E2 (blacklist wins), E3 (locked customer ⇒ visitor + warning), half-open validity.</summary>
public class VehicleClassifierTests
{
    private const string Plate = "51F12345";
    private static readonly DateTime Now = new(2026, 10, 8, 3, 0, 0, DateTimeKind.Utc);

    private static TicketCandidate Ticket(
        int id = 1,
        bool resident = true,
        bool customerActive = true,
        MonthlyTicketStatus status = MonthlyTicketStatus.Active,
        DateTime? start = null,
        DateTime? end = null,
        int customerId = 10,
        string? apartment = "A-1205")
        => new(id, $"MT-{id}", customerId, "Nguyễn Văn Hùng", customerActive, resident, resident ? apartment : null, resident ? "A" : null,
               2, status, start ?? Now.AddDays(-10), end ?? Now.AddDays(20));

    private static readonly BlacklistMatch Black = new(5, Plate, "Nợ phí");

    [Fact]
    public void No_blacklist_and_no_ticket_is_visitor()
    {
        var c = VehicleClassifier.Classify(Plate, null, Array.Empty<TicketCandidate>(), Now);

        Assert.Equal(VehicleCategory.Visitor, c.Category);
        Assert.Equal(Plate, c.NormalizedPlate);
        Assert.Null(c.Ticket);
        Assert.Null(c.Blacklist);
        Assert.Equal(ClassificationWarning.None, c.Warning);
        Assert.False(c.IsMonthlyPass);
        Assert.False(c.IsBlacklisted);
    }

    [Fact]
    public void Visitor_factory_matches_an_unmatched_classification()
    {
        var v = VehicleClassification.Visitor(Plate);

        Assert.Equal(VehicleCategory.Visitor, v.Category);
        Assert.Equal(Plate, v.NormalizedPlate);
        Assert.Null(v.Ticket);
        Assert.Null(v.Blacklist);
        Assert.Equal(ClassificationWarning.None, v.Warning);
    }

    [Fact]
    public void Valid_ticket_of_active_resident_is_resident()
    {
        var c = VehicleClassifier.Classify(Plate, null, new[] { Ticket(resident: true) }, Now);

        Assert.Equal(VehicleCategory.Resident, c.Category);
        Assert.True(c.IsMonthlyPass);
        Assert.NotNull(c.Ticket);
        Assert.Equal("A-1205", c.Ticket!.ApartmentCode);
    }

    [Fact]
    public void Valid_ticket_of_active_non_resident_is_monthly_pass()
    {
        var c = VehicleClassifier.Classify(Plate, null, new[] { Ticket(resident: false) }, Now);

        Assert.Equal(VehicleCategory.MonthlyPass, c.Category);
        Assert.True(c.IsMonthlyPass);
        Assert.Equal(ClassificationWarning.None, c.Warning);
    }

    [Fact]
    public void E2_blacklist_wins_over_a_valid_resident_ticket_and_keeps_the_ticket()
    {
        var c = VehicleClassifier.Classify(Plate, Black, new[] { Ticket(resident: true) }, Now);

        Assert.Equal(VehicleCategory.Blacklisted, c.Category);
        Assert.True(c.IsBlacklisted);
        Assert.False(c.IsMonthlyPass);
        Assert.Equal(Black, c.Blacklist);
        Assert.NotNull(c.Ticket); // feeds hadValidTicket
    }

    [Fact]
    public void Blacklisted_without_ticket_has_no_ticket()
    {
        var c = VehicleClassifier.Classify(Plate, Black, Array.Empty<TicketCandidate>(), Now);

        Assert.Equal(VehicleCategory.Blacklisted, c.Category);
        Assert.Null(c.Ticket);
    }

    [Fact]
    public void E3_valid_ticket_of_locked_customer_is_visitor_with_warning()
    {
        var c = VehicleClassifier.Classify(Plate, null, new[] { Ticket(resident: true, customerActive: false) }, Now);

        Assert.Equal(VehicleCategory.Visitor, c.Category);
        Assert.Equal(ClassificationWarning.CustomerLocked, c.Warning);
        Assert.False(c.IsMonthlyPass);
    }

    [Fact]
    public void Active_customer_ticket_wins_over_locked_customer_ticket()
    {
        var c = VehicleClassifier.Classify(Plate, null,
            new[] { Ticket(id: 1, customerActive: false, customerId: 1), Ticket(id: 2, resident: false, customerActive: true, customerId: 2) }, Now);

        Assert.Equal(VehicleCategory.MonthlyPass, c.Category);
        Assert.Equal(2, c.Ticket!.TicketId);
        Assert.Equal(ClassificationWarning.None, c.Warning);
    }

    [Theory]
    [InlineData(MonthlyTicketStatus.Suspended)]
    [InlineData(MonthlyTicketStatus.Expired)]
    public void Suspended_or_expired_status_is_ignored(MonthlyTicketStatus status)
    {
        var c = VehicleClassifier.Classify(Plate, null, new[] { Ticket(status: status) }, Now);

        Assert.Equal(VehicleCategory.Visitor, c.Category);
        Assert.Equal(ClassificationWarning.None, c.Warning);
    }

    [Fact]
    public void Validity_is_half_open_start_inclusive_end_exclusive()
    {
        Assert.True(VehicleClassifier.IsTicketValidAt(Ticket(start: Now, end: Now.AddDays(1)), Now));
        Assert.False(VehicleClassifier.IsTicketValidAt(Ticket(start: Now.AddDays(-1), end: Now), Now));
        Assert.False(VehicleClassifier.IsTicketValidAt(Ticket(start: Now.AddSeconds(1), end: Now.AddDays(1)), Now));
        Assert.True(VehicleClassifier.IsTicketValidAt(Ticket(start: Now.AddDays(-1), end: Now.AddTicks(1)), Now));
        Assert.False(VehicleClassifier.IsTicketValidAt(Ticket(status: MonthlyTicketStatus.Suspended), Now));

        Assert.Equal(VehicleCategory.Visitor, VehicleClassifier.Classify(Plate, null, new[] { Ticket(start: Now.AddDays(-30), end: Now) }, Now).Category);
        Assert.Equal(VehicleCategory.Resident, VehicleClassifier.Classify(Plate, null, new[] { Ticket(start: Now, end: Now.AddDays(30)) }, Now).Category);
    }

    [Fact]
    public void Future_ticket_is_not_valid_yet()
    {
        var c = VehicleClassifier.Classify(Plate, null, new[] { Ticket(start: Now.AddDays(1), end: Now.AddDays(31)) }, Now);

        Assert.Equal(VehicleCategory.Visitor, c.Category);
    }

    [Fact]
    public void Tie_break_prefers_latest_end_then_highest_ticket_id()
    {
        var later = Ticket(id: 3, resident: true, end: Now.AddDays(40));
        var earlier = Ticket(id: 9, resident: false, end: Now.AddDays(10));
        Assert.Equal(3, VehicleClassifier.Classify(Plate, null, new[] { earlier, later }, Now).Ticket!.TicketId);

        var sameEndLow = Ticket(id: 4, end: Now.AddDays(10));
        var sameEndHigh = Ticket(id: 8, end: Now.AddDays(10));
        Assert.Equal(8, VehicleClassifier.Classify(Plate, null, new[] { sameEndHigh, sameEndLow }, Now).Ticket!.TicketId);
        Assert.Equal(8, VehicleClassifier.Classify(Plate, null, new[] { sameEndLow, sameEndHigh }, Now).Ticket!.TicketId);
    }

    [Fact]
    public void Invalid_tickets_never_win_the_tie_break()
    {
        var suspendedLate = Ticket(id: 20, status: MonthlyTicketStatus.Suspended, end: Now.AddDays(90));
        var valid = Ticket(id: 1, resident: false, end: Now.AddDays(5));

        var c = VehicleClassifier.Classify(Plate, null, new[] { suspendedLate, valid }, Now);

        Assert.Equal(VehicleCategory.MonthlyPass, c.Category);
        Assert.Equal(1, c.Ticket!.TicketId);
    }
}
