using SmartPS.Models.Parking;
using SmartPS.Services.Pricing;

namespace SmartPS.Tests.Integration;

/// <summary>
/// Phiên vào bằng vé (IsMonthlyPass): vé không còn hợp lệ hoặc không tìm thấy vé thì phải tính như khách thường,
/// không được miễn phí vô hạn.
/// </summary>
[Collection(DatabaseCollection.Name)]
[Trait("Category", "Integration")]
public class PricingPassResolutionTests : IClassFixture<SqlServerDatabaseFixture>
{
    private readonly SqlServerDatabaseFixture _db;
    private readonly GateTestData _data;

    public PricingPassResolutionTests(SqlServerDatabaseFixture db)
    {
        _db = db;
        _data = new GateTestData(db);
    }

    private async Task<(PricingContext Context, ParkingSession Session)> ResolveAsync(MonthlyTicketStatus? status, DateTime? ticketEnd, bool withTicket = true)
    {
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var owner = await _data.CreateExternalAsync(vt, withTicket: false);
        var entry = DateTime.UtcNow.AddHours(-6);
        if (withTicket)
        {
            var ticketId = await _data.CreateTicketAsync(owner.CustomerId, owner.VehicleId, owner.RawPlate, vt,
                DateTime.UtcNow.AddDays(-10), ticketEnd ?? DateTime.UtcNow.AddDays(20), status ?? MonthlyTicketStatus.Active);
            Assert.True(ticketId > 0);
        }

        var session = new ParkingSession { LicensePlate = owner.NormalizedPlate, VehicleTypeId = vt, CheckInTime = entry, IsMonthlyPass = true };
        await using var ctx = _db.CreateContext();
        var context = await PricingContextResolver.ResolveAsync(ctx, session, DateTime.UtcNow);
        return (context, session);
    }

    private static decimal Fee(PricingContext c, ParkingSession s)
        => new PricingEngine().Calculate(new PricingRequest
        {
            LicensePlate = s.LicensePlate,
            VehicleTypeId = s.VehicleTypeId,
            EntryUtc = s.CheckInTime,
            ExitUtc = DateTime.UtcNow,
            MonthlyPass = c.MonthlyPass,
            IsOverQuota = c.IsOverQuota,
            Policy = c.Policy
        }).Total;

    [Fact]
    public async Task Active_ticket_covering_the_entry_is_free()
    {
        _db.RequireAvailable();
        var (c, s) = await ResolveAsync(MonthlyTicketStatus.Active, null);
        Assert.Equal(0m, Fee(c, s));
    }

    [Theory]
    [InlineData(MonthlyTicketStatus.Cancelled)]
    [InlineData(MonthlyTicketStatus.Suspended)]
    public async Task Ticket_that_is_no_longer_active_is_charged_as_a_regular_customer(MonthlyTicketStatus status)
    {
        _db.RequireAvailable();
        var (c, s) = await ResolveAsync(status, null);
        Assert.True(Fee(c, s) > 0m);
    }

    [Fact]
    public async Task Pass_flag_without_any_ticket_is_charged_as_a_regular_customer()
    {
        _db.RequireAvailable();
        var (c, s) = await ResolveAsync(null, null, withTicket: false);
        Assert.Null(c.MonthlyPass);
        Assert.True(Fee(c, s) > 0m);
    }

    [Fact]
    public async Task Ticket_expired_during_the_stay_is_charged_only_after_the_expiry()
    {
        _db.RequireAvailable();
        var (c, s) = await ResolveAsync(MonthlyTicketStatus.Active, DateTime.UtcNow.AddHours(-1));
        var fee = Fee(c, s);
        Assert.True(fee > 0m);
        Assert.True(fee < Fee(c with { MonthlyPass = null }, s));
    }
}
