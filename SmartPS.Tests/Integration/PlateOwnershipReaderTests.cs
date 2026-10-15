using SmartPS.Models.Parking;
using SmartPS.Services.GateControl;

namespace SmartPS.Tests.Integration;

/// <summary>
/// Plan §2.1/§2.2, spec B1/D1/D4, AC-R4, addendum F4: the read-only owner/ticket lookup over the household/vehicle tables.
/// Plates are stored RAW; the reader matches them after normalizing the stored column (upper, letters and digits only).
/// Uses a fixed "now" so ticket validity edges are deterministic.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class PlateOwnershipReaderTests : IClassFixture<PostgresDatabaseFixture>
{
    private static readonly DateTime Now = new(2026, 10, 13, 5, 0, 0, DateTimeKind.Utc);

    private readonly PostgresDatabaseFixture _db;
    private readonly GateTestData _data;

    public PlateOwnershipReaderTests(PostgresDatabaseFixture db)
    {
        _db = db;
        _data = new GateTestData(db);
    }

    private async Task<PlateOwnership> ReadAsync(string normalizedPlate, DateTime? nowUtc = null)
    {
        await using var ctx = _db.CreateContext();
        return await new PlateOwnershipReader().ReadAsync(ctx, normalizedPlate, nowUtc ?? Now);
    }

    private Task<int> TicketAsync(int customerId, int vehicleId, string rawPlate, int vt, DateTime start, DateTime end,
        MonthlyTicketStatus status = MonthlyTicketStatus.Active)
        => _data.CreateTicketAsync(customerId, vehicleId, rawPlate, vt, start, end, status);

    // ---- AC-R4 / D4: raw stored plates ------------------------------------------------------------------------------

    [Fact]
    public async Task AC_R4_raw_plate_51A_123_45_in_Vehicles_matches_gate_plate_51A12345()
    {
        _db.RequireAvailable();
        var vt = await _data.MotorbikeTypeIdAsync();
        var householdId = await _data.CreateHouseholdAsync("R4-0101");
        var customerId = await _data.CreateCustomerAsync(householdId, fullName: "Chủ xe R4");
        var vehicleId = await _data.AddVehicleAsync(customerId, "51A-123.45", vt);

        var owner = await ReadAsync("51A12345");

        Assert.Equal("51A12345", owner.NormalizedPlate);
        Assert.True(owner.HasOwner);
        Assert.Equal(vehicleId, owner.VehicleId);
        Assert.Equal(vt, owner.VehicleTypeId);
        Assert.Equal(customerId, owner.OwnerCustomerId);
        Assert.Equal("Chủ xe R4", owner.OwnerName);
        Assert.True(owner.OwnerIsActive);
        Assert.Equal(householdId, owner.HouseholdId);
        Assert.True(owner.HouseholdIsActive);
        Assert.Equal("R4-0101", owner.ApartmentCode);
        Assert.True(owner.IsResident);
        Assert.Empty(owner.ValidTickets);
    }

    public static TheoryData<int> StoredVariants() => new() { 0, 1, 2, 3, 4 };

    /// <summary>The reader's SQL normalization equals <see cref="LicensePlateNormalizer.Normalize"/> for '-', '.', spaces and case.</summary>
    [Theory]
    [MemberData(nameof(StoredVariants))]
    public async Task Stored_plate_with_any_separator_or_case_matches_the_normalized_plate(int variant)
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync(withPricingRule: false);
        var norm = GateTestData.UniqueNormalizedPlate();
        var stored = variant switch
        {
            0 => norm[..3] + "-" + norm[3..6] + "." + norm[6..],
            1 => (norm[..3] + " " + norm[3..]).ToLowerInvariant(),
            2 => norm[..2] + "." + norm[2..5] + "-" + norm[5..],
            3 => " " + norm.ToLowerInvariant() + " ",
            _ => norm
        };
        Assert.Equal(norm, LicensePlateNormalizer.Normalize(stored));
        var customerId = await _data.CreateCustomerAsync(null);
        var vehicleId = await _data.AddVehicleAsync(customerId, stored, vt);

        var owner = await ReadAsync(norm);

        Assert.Equal(vehicleId, owner.VehicleId);
        Assert.Equal(customerId, owner.OwnerCustomerId);
    }

    [Fact]
    public async Task Registered_ticket_plate_with_other_separators_matches_when_no_active_vehicle_has_the_plate()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync(withPricingRule: false);
        var norm = GateTestData.UniqueNormalizedPlate();
        var customerId = await _data.CreateCustomerAsync(null, fullName: "Chủ vé theo biển");
        // The ticket's vehicle row is ACTIVE but carries another plate; only RegisteredLicensePlate matches the gate plate.
        // (Fix round 2, H5a: a ticket bound to an inactive vehicle is no longer honoured, so the vehicle must be active.)
        var otherVehicle = await _data.AddVehicleAsync(customerId, GateTestData.Raw(GateTestData.UniqueNormalizedPlate()), vt);
        var ticketId = await TicketAsync(customerId, otherVehicle, (norm[..3] + " " + norm[3..6] + "-" + norm[6..]).ToLowerInvariant(), vt, Now.AddDays(-5), Now.AddDays(25));

        var owner = await ReadAsync(norm);

        Assert.Null(owner.VehicleId);
        Assert.Null(owner.HouseholdId);
        Assert.False(owner.HouseholdIsActive);
        Assert.Equal(customerId, owner.OwnerCustomerId);
        Assert.Equal("Chủ vé theo biển", owner.OwnerName);
        Assert.True(owner.OwnerIsActive);
        var ticket = Assert.Single(owner.ValidTickets);
        Assert.Equal(ticketId, ticket.TicketId);
        Assert.Equal(customerId, ticket.CustomerId);
        Assert.Equal(vt, ticket.VehicleTypeId);
    }

    [Fact]
    public async Task Ticket_is_matched_by_VehicleId_when_its_registered_plate_differs()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync(withPricingRule: false);
        var owner0 = await _data.CreateExternalAsync(vt, withTicket: false);
        var ticketId = await TicketAsync(owner0.CustomerId, owner0.VehicleId, "OLD-999.99", vt, Now.AddDays(-5), Now.AddDays(25));

        var owner = await ReadAsync(owner0.NormalizedPlate);

        Assert.Equal(owner0.VehicleId, owner.VehicleId);
        Assert.Equal(ticketId, Assert.Single(owner.ValidTickets).TicketId);
    }

    [Fact]
    public async Task Ticket_fields_are_projected()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync(withPricingRule: false);
        var o = await _data.CreateExternalAsync(vt, withTicket: false);
        var start = Now.AddDays(-3);
        var end = Now.AddDays(27);
        var ticketId = await _data.CreateTicketAsync(o.CustomerId, o.VehicleId, o.RawPlate, vt, start, end, ticketCode: "MT-PROJ-" + o.NormalizedPlate);

        var t = Assert.Single((await ReadAsync(o.NormalizedPlate)).ValidTickets);

        Assert.Equal(ticketId, t.TicketId);
        Assert.Equal("MT-PROJ-" + o.NormalizedPlate, t.TicketCode);
        Assert.Equal(o.CustomerId, t.CustomerId);
        Assert.Equal(vt, t.VehicleTypeId);
        Assert.Equal(start, t.StartDateUtc);
        Assert.Equal(end, t.EndDateUtc);
    }

    // ---- ticket validity --------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(MonthlyTicketStatus.Expired)]
    [InlineData(MonthlyTicketStatus.Suspended)]
    [InlineData(MonthlyTicketStatus.Cancelled)]
    public async Task Ticket_that_is_not_Active_is_excluded(MonthlyTicketStatus status)
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync(withPricingRule: false);
        var o = await _data.CreateExternalAsync(vt, withTicket: false);
        await TicketAsync(o.CustomerId, o.VehicleId, o.RawPlate, vt, Now.AddDays(-5), Now.AddDays(25), status);

        var owner = await ReadAsync(o.NormalizedPlate);

        Assert.Equal(o.CustomerId, owner.OwnerCustomerId);
        Assert.Empty(owner.ValidTickets);
    }

    [Fact]
    public async Task Ticket_outside_its_dates_is_excluded_and_EndDate_equal_now_is_included()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync(withPricingRule: false);
        var future = await _data.CreateExternalAsync(vt, withTicket: false);
        var past = await _data.CreateExternalAsync(vt, withTicket: false);
        var endsNow = await _data.CreateExternalAsync(vt, withTicket: false);
        var startsNow = await _data.CreateExternalAsync(vt, withTicket: false);
        await TicketAsync(future.CustomerId, future.VehicleId, future.RawPlate, vt, Now.AddSeconds(1), Now.AddDays(30));
        await TicketAsync(past.CustomerId, past.VehicleId, past.RawPlate, vt, Now.AddDays(-30), Now.AddSeconds(-1));
        var endsNowId = await TicketAsync(endsNow.CustomerId, endsNow.VehicleId, endsNow.RawPlate, vt, Now.AddDays(-30), Now);
        var startsNowId = await TicketAsync(startsNow.CustomerId, startsNow.VehicleId, startsNow.RawPlate, vt, Now, Now.AddDays(30));

        Assert.Empty((await ReadAsync(future.NormalizedPlate)).ValidTickets);
        Assert.Empty((await ReadAsync(past.NormalizedPlate)).ValidTickets);
        Assert.Equal(endsNowId, Assert.Single((await ReadAsync(endsNow.NormalizedPlate)).ValidTickets).TicketId);
        Assert.Equal(startsNowId, Assert.Single((await ReadAsync(startsNow.NormalizedPlate)).ValidTickets).TicketId);
    }

    [Fact]
    public async Task Valid_tickets_are_ordered_by_EndDate_desc_then_TicketId_desc()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync(withPricingRule: false);
        var o = await _data.CreateExternalAsync(vt, withTicket: false);
        var shortest = await TicketAsync(o.CustomerId, o.VehicleId, o.RawPlate, vt, Now.AddDays(-5), Now.AddDays(5));
        var longestA = await TicketAsync(o.CustomerId, o.VehicleId, o.RawPlate, vt, Now.AddDays(-5), Now.AddDays(40));
        var longestB = await TicketAsync(o.CustomerId, o.VehicleId, o.RawPlate, vt, Now.AddDays(-5), Now.AddDays(40));

        var tickets = (await ReadAsync(o.NormalizedPlate)).ValidTickets;

        Assert.Equal(new[] { longestB, longestA, shortest }, tickets.Select(t => t.TicketId).ToArray());
    }

    [Fact]
    public async Task Ticket_of_an_inactive_customer_is_excluded()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync(withPricingRule: false);
        var o = await _data.CreateResidentAsync(vt, withTicket: true, customerActive: false);

        var owner = await ReadAsync(o.NormalizedPlate);

        Assert.Equal(o.CustomerId, owner.OwnerCustomerId);
        Assert.False(owner.OwnerIsActive);
        Assert.False(owner.IsResident);
        Assert.Empty(owner.ValidTickets);
    }

    [Fact]
    public async Task Plate_only_ticket_of_an_inactive_customer_gives_no_owner_ticket()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync(withPricingRule: false);
        var norm = GateTestData.UniqueNormalizedPlate();
        var customerId = await _data.CreateCustomerAsync(null, isActive: false);
        // Active vehicle with another plate, so only the customer's inactivity can exclude the ticket (H5a).
        var otherVehicle = await _data.AddVehicleAsync(customerId, GateTestData.Raw(GateTestData.UniqueNormalizedPlate()), vt);
        await TicketAsync(customerId, otherVehicle, GateTestData.Raw(norm), vt, Now.AddDays(-5), Now.AddDays(25));

        var owner = await ReadAsync(norm);

        Assert.Empty(owner.ValidTickets);
        Assert.False(owner.IsResident);
    }

    // ---- Fix round 2, H5: residency only through an active Vehicle; tickets of inactive vehicles are not honoured ------

    [Fact]
    public async Task H5a_ticket_bound_to_an_inactive_vehicle_is_not_honoured_even_if_its_registered_plate_matches()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync(withPricingRule: false);
        var norm = GateTestData.UniqueNormalizedPlate();
        var customerId = await _data.CreateCustomerAsync(null);
        var removedVehicle = await _data.AddVehicleAsync(customerId, GateTestData.Raw(norm), vt, isActive: false);
        await TicketAsync(customerId, removedVehicle, GateTestData.Raw(norm), vt, Now.AddDays(-5), Now.AddDays(25));

        var owner = await ReadAsync(norm);

        Assert.Empty(owner.ValidTickets);
        Assert.False(owner.IsResident);
    }

    [Fact]
    public async Task H5a_ticket_of_the_removed_vehicle_is_not_honoured_for_the_re_registered_active_vehicle()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync(withPricingRule: false);
        var norm = GateTestData.UniqueNormalizedPlate();
        var householdId = await _data.CreateHouseholdAsync();
        var customerId = await _data.CreateCustomerAsync(householdId);
        var removedVehicle = await _data.AddVehicleAsync(customerId, GateTestData.Raw(norm), vt, isActive: false, createdAtUtc: Now.AddDays(-90));
        var activeVehicle = await _data.AddVehicleAsync(customerId, norm, vt, createdAtUtc: Now.AddDays(-1));
        await TicketAsync(customerId, removedVehicle, GateTestData.Raw(norm), vt, Now.AddDays(-5), Now.AddDays(25));

        var owner = await ReadAsync(norm);

        Assert.Equal(activeVehicle, owner.VehicleId);
        Assert.True(owner.IsResident);   // D1: household through the active vehicle's owner
        Assert.Empty(owner.ValidTickets); // the ticket's vehicle is inactive
    }

    [Fact]
    public async Task H5b_ticket_only_match_of_a_household_member_has_no_household_and_is_not_resident()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync(withPricingRule: false);
        var norm = GateTestData.UniqueNormalizedPlate();
        var householdId = await _data.CreateHouseholdAsync("H5B-0101");
        var holder = await _data.CreateCustomerAsync(householdId, CustomerType.Resident, fullName: "Chủ vé trong hộ");
        // The holder's active vehicle carries another plate; the gate plate matches only the ticket's registered plate.
        var holderVehicle = await _data.AddVehicleAsync(holder, GateTestData.Raw(GateTestData.UniqueNormalizedPlate()), vt);
        var ticketId = await TicketAsync(holder, holderVehicle, GateTestData.Raw(norm), vt, Now.AddDays(-5), Now.AddDays(25));

        var owner = await ReadAsync(norm);

        Assert.Null(owner.VehicleId);
        Assert.Equal(holder, owner.OwnerCustomerId);
        Assert.True(owner.OwnerIsActive);
        Assert.Null(owner.HouseholdId);
        Assert.False(owner.HouseholdIsActive);
        Assert.Null(owner.ApartmentCode);
        Assert.False(owner.IsResident);
        Assert.Equal(ticketId, Assert.Single(owner.ValidTickets).TicketId);
    }

    // ---- vehicles and owners ----------------------------------------------------------------------------------------

    [Fact]
    public async Task Unknown_plate_returns_None()
    {
        _db.RequireAvailable();
        var norm = GateTestData.UniqueNormalizedPlate();

        var owner = await ReadAsync(norm);

        Assert.Equal(norm, owner.NormalizedPlate);
        Assert.False(owner.HasOwner);
        Assert.Null(owner.VehicleId);
        Assert.Null(owner.OwnerCustomerId);
        Assert.Null(owner.HouseholdId);
        Assert.False(owner.IsResident);
        Assert.Empty(owner.ValidTickets);
    }

    [Fact]
    public async Task Inactive_vehicle_is_ignored()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync(withPricingRule: false);
        var o = await _data.CreateResidentAsync(vt);
        await _data.SetVehicleActiveAsync(o.VehicleId, false);

        var owner = await ReadAsync(o.NormalizedPlate);

        Assert.False(owner.HasOwner);
        Assert.False(owner.IsResident);
    }

    [Fact]
    public async Task Two_active_variants_of_one_plate_the_latest_vehicle_defines_the_owner()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync(withPricingRule: false);
        var norm = GateTestData.UniqueNormalizedPlate();
        var olderOwner = await _data.CreateCustomerAsync(await _data.CreateHouseholdAsync());
        var latestOwner = await _data.CreateCustomerAsync(null);
        await _data.AddVehicleAsync(olderOwner, GateTestData.Raw(norm), vt, createdAtUtc: Now.AddDays(-60));
        var latestVehicle = await _data.AddVehicleAsync(latestOwner, norm.ToLowerInvariant(), vt, createdAtUtc: Now.AddDays(-2));

        var owner = await ReadAsync(norm);

        Assert.Equal(latestVehicle, owner.VehicleId);
        Assert.Equal(latestOwner, owner.OwnerCustomerId);
        Assert.False(owner.IsResident);
    }

    [Fact]
    public async Task F4_ticket_of_the_older_variants_owner_still_counts_when_the_latest_variant_has_another_owner()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync(withPricingRule: false);
        var norm = GateTestData.UniqueNormalizedPlate();
        var ticketHolder = await _data.CreateCustomerAsync(null, fullName: "Chủ vé cũ");
        var latestOwner = await _data.CreateCustomerAsync(null, fullName: "Chủ xe mới");
        var olderVehicle = await _data.AddVehicleAsync(ticketHolder, GateTestData.Raw(norm), vt, createdAtUtc: Now.AddDays(-60));
        var latestVehicle = await _data.AddVehicleAsync(latestOwner, norm, vt, createdAtUtc: Now.AddDays(-2));
        var ticketId = await TicketAsync(ticketHolder, olderVehicle, GateTestData.Raw(norm), vt, Now.AddDays(-5), Now.AddDays(25));

        var owner = await ReadAsync(norm);

        Assert.Equal(latestVehicle, owner.VehicleId);
        Assert.Equal(latestOwner, owner.OwnerCustomerId);
        var ticket = Assert.Single(owner.ValidTickets);
        Assert.Equal(ticketId, ticket.TicketId);
        Assert.Equal(ticketHolder, ticket.CustomerId);
    }

    [Fact]
    public async Task Household_inactive_means_not_resident()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync(withPricingRule: false);
        var o = await _data.CreateResidentAsync(vt, householdActive: false);

        var owner = await ReadAsync(o.NormalizedPlate);

        Assert.Equal(o.HouseholdId, owner.HouseholdId);
        Assert.False(owner.HouseholdIsActive);
        Assert.True(owner.OwnerIsActive);
        Assert.False(owner.IsResident);
    }

    [Fact]
    public async Task D1_customer_type_Resident_without_household_is_not_resident()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync(withPricingRule: false);
        var o = await _data.CreateExternalAsync(vt, withTicket: false, type: CustomerType.Resident);

        var owner = await ReadAsync(o.NormalizedPlate);

        Assert.True(owner.HasOwner);
        Assert.Null(owner.HouseholdId);
        Assert.False(owner.IsResident);
    }

    [Fact]
    public async Task D1_customer_type_External_in_an_active_household_is_resident()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync(withPricingRule: false);
        var o = await _data.CreateResidentAsync(vt, type: CustomerType.External);

        var owner = await ReadAsync(o.NormalizedPlate);

        Assert.True(owner.IsResident);
        Assert.Equal(o.ApartmentCode, owner.ApartmentCode);
    }

    [Fact]
    public async Task D1_a_valid_ticket_never_makes_a_non_household_customer_resident()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync(withPricingRule: false);
        var o = await _data.CreateExternalAsync(vt, withTicket: true, type: CustomerType.Resident);

        var owner = await ReadAsync(o.NormalizedPlate, DateTime.UtcNow);

        Assert.Single(owner.ValidTickets);
        Assert.False(owner.IsResident);
    }

    [Fact]
    public async Task Reader_does_not_write()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync(withPricingRule: false);
        var o = await _data.CreateResidentAsync(vt, withTicket: true);
        const string Snapshot = """
            SELECT (SELECT count(*) FROM "Vehicles") + (SELECT count(*) FROM "Customers") + (SELECT count(*) FROM "Households")
                 + (SELECT count(*) FROM "MonthlyTickets") + (SELECT count(*) FROM "AuditLogs")
            """;
        var before = await _data.CountAsync(Snapshot);

        await ReadAsync(o.NormalizedPlate, DateTime.UtcNow);

        Assert.Equal(before, await _data.CountAsync(Snapshot));
    }
}
