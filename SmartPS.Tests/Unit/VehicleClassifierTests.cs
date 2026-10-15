using SmartPS.Models.GateControl;
using SmartPS.Services.GateControl;

namespace SmartPS.Tests.Unit;

/// <summary>
/// Pure gate classification on the household model (spec B1, D1; plan §2.3):
/// Blacklisted &gt; Resident (owner active AND in an active household) &gt; MonthlyPass (valid ticket of the requested
/// vehicle type) &gt; Visitor. Residency never comes from a ticket or from Customer.Type.
/// </summary>
public class VehicleClassifierTests
{
    private const string Plate = "51F12345";
    private const int Moto = 1;
    private const int Car = 2;
    private static readonly DateTime Now = new(2026, 10, 8, 3, 0, 0, DateTimeKind.Utc);

    private static readonly BlacklistMatch Black = new(Plate, "Nợ phí");

    private static PlateTicket Ticket(int id = 1, int customerId = 10, int vehicleTypeId = Moto, DateTime? start = null, DateTime? end = null)
        => new(id, $"MT-{id}", customerId, vehicleTypeId, start ?? Now.AddDays(-10), end ?? Now.AddDays(20));

    private static PlateOwnership Owner(
        bool ownerActive = true,
        int? householdId = 7,
        bool householdActive = true,
        string? apartment = "A-1205",
        int vehicleTypeId = Moto,
        params PlateTicket[] tickets)
        => new(Plate, 100, vehicleTypeId, 10, "Nguyễn Văn Hùng", ownerActive, householdId, householdActive,
               householdId is null ? null : apartment, tickets);

    // ---- PlateOwnership.IsResident (D1) ---------------------------------------------------------------------------

    [Fact]
    public void Ownership_IsResident_requires_active_owner_and_active_household()
    {
        Assert.True(Owner().IsResident);
        Assert.False(Owner(ownerActive: false).IsResident);
        Assert.False(Owner(householdId: null).IsResident);
        Assert.False(Owner(householdActive: false).IsResident);
        Assert.False(PlateOwnership.None(Plate).IsResident);
        Assert.False(PlateOwnership.None(Plate).HasOwner);
        Assert.Empty(PlateOwnership.None(Plate).ValidTickets);
        Assert.Equal(Plate, PlateOwnership.None(Plate).NormalizedPlate);
    }

    [Fact]
    public void Ownership_with_household_id_but_inactive_household_is_not_resident_even_with_a_ticket()
    {
        var owner = Owner(householdActive: false, tickets: Ticket());

        Assert.False(owner.IsResident);
        Assert.True(owner.HasOwner);
    }

    // ---- precedence -----------------------------------------------------------------------------------------------

    [Fact]
    public void Unknown_plate_is_visitor()
    {
        var c = VehicleClassifier.Classify(Plate, null, PlateOwnership.None(Plate), Moto, Now);

        Assert.Equal(VehicleCategory.Visitor, c.Category);
        Assert.Equal(Plate, c.NormalizedPlate);
        Assert.Null(c.Ticket);
        Assert.Null(c.Blacklist);
        Assert.Equal(ClassificationWarning.None, c.Warning);
        Assert.False(c.IsMonthlyPass);
        Assert.False(c.IsBlacklisted);
        Assert.False(c.IsResident);
    }

    [Fact]
    public void Null_ownership_is_visitor()
    {
        var c = VehicleClassifier.Classify(Plate, null, null, Moto, Now);

        Assert.Equal(VehicleCategory.Visitor, c.Category);
        Assert.Null(c.Owner);
        Assert.False(c.IsMonthlyPass);
    }

    [Fact]
    public void AC_R1_household_member_without_ticket_is_resident_without_monthly_pass()
    {
        var owner = Owner();

        var c = VehicleClassifier.Classify(Plate, null, owner, Moto, Now);

        Assert.Equal(VehicleCategory.Resident, c.Category);
        Assert.True(c.IsResident);
        Assert.False(c.IsMonthlyPass);
        Assert.Null(c.Ticket);
        Assert.Same(owner, c.Owner);
        Assert.Equal(ClassificationWarning.None, c.Warning);
    }

    [Fact]
    public void AC_R2_non_resident_with_valid_ticket_is_monthly_pass()
    {
        var ticket = Ticket();
        var c = VehicleClassifier.Classify(Plate, null, Owner(householdId: null, tickets: ticket), Moto, Now);

        Assert.Equal(VehicleCategory.MonthlyPass, c.Category);
        Assert.False(c.IsResident);
        Assert.True(c.IsMonthlyPass);
        Assert.Equal(ticket, c.Ticket);
    }

    [Fact]
    public void AC_R3_resident_with_valid_ticket_is_resident_with_monthly_pass()
    {
        var ticket = Ticket();
        var c = VehicleClassifier.Classify(Plate, null, Owner(tickets: ticket), Moto, Now);

        Assert.Equal(VehicleCategory.Resident, c.Category);
        Assert.True(c.IsResident);
        Assert.True(c.IsMonthlyPass);
        Assert.Equal(ticket, c.Ticket);
    }

    [Fact]
    public void Inactive_household_member_with_ticket_is_monthly_pass_not_resident()
    {
        var c = VehicleClassifier.Classify(Plate, null, Owner(householdActive: false, tickets: Ticket()), Moto, Now);

        Assert.Equal(VehicleCategory.MonthlyPass, c.Category);
        Assert.False(c.IsResident);
    }

    [Fact]
    public void Inactive_household_member_without_ticket_is_visitor()
    {
        var c = VehicleClassifier.Classify(Plate, null, Owner(householdActive: false), Moto, Now);

        Assert.Equal(VehicleCategory.Visitor, c.Category);
        Assert.False(c.IsResident);
        Assert.False(c.IsMonthlyPass);
    }

    [Fact]
    public void Locked_owner_in_active_household_with_ticket_is_visitor_with_CustomerLocked_warning()
    {
        // The reader returns no valid tickets for an inactive owner; the classifier must not honour any either.
        var c = VehicleClassifier.Classify(Plate, null, Owner(ownerActive: false, tickets: Ticket()), Moto, Now);

        Assert.Equal(VehicleCategory.Visitor, c.Category);
        Assert.Equal(ClassificationWarning.CustomerLocked, c.Warning);
        Assert.False(c.IsResident);
        Assert.False(c.IsMonthlyPass);
        Assert.Null(c.Ticket);
    }

    [Fact]
    public void Locked_owner_without_ticket_is_visitor_with_CustomerLocked_warning()
    {
        var c = VehicleClassifier.Classify(Plate, null, Owner(ownerActive: false), Moto, Now);

        Assert.Equal(VehicleCategory.Visitor, c.Category);
        Assert.Equal(ClassificationWarning.CustomerLocked, c.Warning);
    }

    [Fact]
    public void Blacklist_beats_resident_with_ticket_and_keeps_owner_and_ticket_for_audit()
    {
        var owner = Owner(tickets: Ticket());

        var c = VehicleClassifier.Classify(Plate, Black, owner, Moto, Now);

        Assert.Equal(VehicleCategory.Blacklisted, c.Category);
        Assert.True(c.IsBlacklisted);
        Assert.False(c.IsResident);
        Assert.Equal(Black, c.Blacklist);
        Assert.Same(owner, c.Owner);
        Assert.NotNull(c.Ticket);
    }

    [Fact]
    public void Blacklist_beats_monthly_pass_and_unknown_plates()
    {
        Assert.Equal(VehicleCategory.Blacklisted, VehicleClassifier.Classify(Plate, Black, Owner(householdId: null, tickets: Ticket()), Moto, Now).Category);
        Assert.Equal(VehicleCategory.Blacklisted, VehicleClassifier.Classify(Plate, Black, PlateOwnership.None(Plate), Moto, Now).Category);
        Assert.Equal(VehicleCategory.Blacklisted, VehicleClassifier.Classify(Plate, Black, null, Moto, Now).Category);
    }

    // ---- vehicle type of the ticket --------------------------------------------------------------------------------

    [Fact]
    public void Ticket_of_another_vehicle_type_is_ignored_with_warning_for_a_non_resident()
    {
        var c = VehicleClassifier.Classify(Plate, null, Owner(householdId: null, tickets: Ticket(vehicleTypeId: Moto)), Car, Now);

        Assert.Equal(VehicleCategory.Visitor, c.Category);
        Assert.Equal(ClassificationWarning.TicketVehicleTypeMismatch, c.Warning);
        Assert.Null(c.Ticket);
        Assert.False(c.IsMonthlyPass);
    }

    [Fact]
    public void Ticket_of_another_vehicle_type_keeps_a_household_member_resident_without_pass()
    {
        var c = VehicleClassifier.Classify(Plate, null, Owner(tickets: Ticket(vehicleTypeId: Moto)), Car, Now);

        Assert.Equal(VehicleCategory.Resident, c.Category);
        Assert.Equal(ClassificationWarning.TicketVehicleTypeMismatch, c.Warning);
        Assert.Null(c.Ticket);
        Assert.False(c.IsMonthlyPass);
    }

    [Fact]
    public void Matching_ticket_is_chosen_when_another_ticket_has_the_wrong_type()
    {
        var wrong = Ticket(id: 1, vehicleTypeId: Car, end: Now.AddDays(60));
        var right = Ticket(id: 2, vehicleTypeId: Moto, end: Now.AddDays(5));

        var c = VehicleClassifier.Classify(Plate, null, Owner(householdId: null, tickets: new[] { wrong, right }), Moto, Now);

        Assert.Equal(VehicleCategory.MonthlyPass, c.Category);
        Assert.Equal(right, c.Ticket);
        Assert.Equal(ClassificationWarning.None, c.Warning);
    }

    [Fact]
    public void Unspecified_vehicle_type_accepts_any_ticket_type()
    {
        var c = VehicleClassifier.Classify(Plate, null, Owner(householdId: null, tickets: Ticket(vehicleTypeId: Car)), 0, Now);

        Assert.Equal(VehicleCategory.MonthlyPass, c.Category);
        Assert.NotNull(c.Ticket);
    }

    [Fact]
    public void Best_ticket_is_the_first_valid_one_in_reader_order()
    {
        // Reader order is EndDate desc, TicketId desc; the classifier honours the first matching one.
        var later = Ticket(id: 3, end: Now.AddDays(40));
        var earlier = Ticket(id: 4, end: Now.AddDays(10));

        var c = VehicleClassifier.Classify(Plate, null, Owner(householdId: null, tickets: new[] { later, earlier }), Moto, Now);

        Assert.Equal(later, c.Ticket);
    }

    // ---- defensive validity re-check -------------------------------------------------------------------------------

    [Fact]
    public void Ticket_not_yet_started_or_already_ended_is_ignored()
    {
        var future = Ticket(id: 1, start: Now.AddMinutes(1), end: Now.AddDays(30));
        var past = Ticket(id: 2, start: Now.AddDays(-30), end: Now.AddTicks(-1));

        var c = VehicleClassifier.Classify(Plate, null, Owner(householdId: null, tickets: new[] { future, past }), Moto, Now);

        Assert.Equal(VehicleCategory.Visitor, c.Category);
        Assert.Null(c.Ticket);
    }

    [Fact]
    public void Ticket_is_valid_on_its_start_and_end_instants_inclusive()
    {
        var startsNow = Ticket(id: 1, start: Now, end: Now.AddDays(30));
        var endsNow = Ticket(id: 2, start: Now.AddDays(-30), end: Now);

        Assert.Equal(VehicleCategory.MonthlyPass, VehicleClassifier.Classify(Plate, null, Owner(householdId: null, tickets: startsNow), Moto, Now).Category);
        Assert.Equal(VehicleCategory.MonthlyPass, VehicleClassifier.Classify(Plate, null, Owner(householdId: null, tickets: endsNow), Moto, Now).Category);
    }

    // ---- factory ---------------------------------------------------------------------------------------------------

    [Fact]
    public void Visitor_factory_has_no_owner_ticket_or_warning()
    {
        var c = VehicleClassification.Visitor(Plate);

        Assert.Equal(VehicleCategory.Visitor, c.Category);
        Assert.Equal(Plate, c.NormalizedPlate);
        Assert.Null(c.Owner);
        Assert.Null(c.Ticket);
        Assert.Null(c.Blacklist);
        Assert.Equal(ClassificationWarning.None, c.Warning);
        Assert.False(c.IsMonthlyPass);
        Assert.False(c.IsResident);
        Assert.False(c.IsBlacklisted);
    }

    [Fact]
    public void ClassificationWarning_values_are_stable()
    {
        Assert.Equal(0, (int)ClassificationWarning.None);
        Assert.Equal(1, (int)ClassificationWarning.CustomerLocked);
        Assert.Equal(2, (int)ClassificationWarning.TicketVehicleTypeMismatch);
    }
}
