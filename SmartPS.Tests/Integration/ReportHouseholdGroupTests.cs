using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartPS.Models.GateControl;
using SmartPS.Models.Parking;
using SmartPS.Services.GateControl;

namespace SmartPS.Tests.Integration;

/// <summary>
/// AC-R9 end to end on the household model: owners are seeded in the household/vehicle tables (GateTestData, SQL only),
/// they pass the real gate (check-in and check-out), and the report groups the sessions exactly by the values the gate
/// wrote: Resident ⇔ CustomerType = Resident (household member, with or without ticket); MonthlyPass ⇔ External with
/// IsMonthlyPass; Visitor ⇔ External without pass (also a registered customer whose Customer.Type says Resident but who has
/// no household, and an unknown plate). Revenue per group comes from the FinancialTransactions the checkout recorded.
/// The class works on its own isolated vehicle type, so other data in the database does not affect the figures.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class ReportHouseholdGroupTests : IClassFixture<PostgresDatabaseFixture>
{
    private const decimal Block4h = 6_000m;
    private const decimal Daily = 30_000m;

    private readonly PostgresDatabaseFixture _db;
    private readonly GateTestData _data;

    public ReportHouseholdGroupTests(PostgresDatabaseFixture db)
    {
        _db = db;
        _data = new GateTestData(db);
    }

    private static IGateControlService Gate(IServiceProvider sp) => sp.GetRequiredService<IGateControlService>();

    private async Task<int> CheckInAsync(IServiceProvider sp, string plate, int vt)
    {
        var result = await Gate(sp).ProcessCheckInAsync(new GateCheckInRequest { LicensePlate = plate, VehicleTypeId = vt });
        Assert.True(result.Success, result.Message);
        // one hour inside the stay keeps the fee at the 4h block price
        await _data.SetSessionCheckInAsync(result.Session!.SessionId, DateTime.UtcNow.AddHours(-1));
        return result.Session.SessionId;
    }

    private async Task CheckOutAsync(IServiceProvider sp, string plate, int sessionId, int userId, PaymentMethod method, decimal expectedFee)
    {
        var calc = await Gate(sp).CalculateCheckOutAsync(plate);
        Assert.True(calc.Success, calc.Message);
        Assert.Equal(sessionId, calc.ActiveSession!.SessionId);
        Assert.Equal(expectedFee, calc.TotalFee);
        var done = await Gate(sp).CompleteCheckOutAsync(new GateCheckOutRequest { SessionId = sessionId, ActorUserId = userId, PaymentMethod = method, TotalFee = calc.TotalFee });
        Assert.True(done.Success, done.Message);
    }

    [Fact]
    public async Task AC_R9_report_groups_follow_the_session_values_written_by_the_gate_on_the_household_model()
    {
        _db.RequireAvailable();
        var startUtc = DateTime.UtcNow;
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(op.Username);
        await ParkingFlows.OpenShiftAsync(sp, op.UserId);

        var vt = await _data.CreateIsolatedVehicleTypeAsync(block4hPrice: Block4h, dailyPrice: Daily);
        await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 6);

        var residentNoTicket = await _data.CreateResidentAsync(vt, withTicket: false);                          // AC-R1
        var residentWithTicket = await _data.CreateResidentAsync(vt, withTicket: true);                         // AC-R3
        var externalWithTicket = await _data.CreateExternalAsync(vt, withTicket: true);                         // AC-R2
        var typedResidentNoHousehold = await _data.CreateExternalAsync(vt, withTicket: false, type: CustomerType.Resident); // D1: Type is ignored
        var unknownPlate = GateTestData.UniqueNormalizedPlate();

        var s1 = await CheckInAsync(sp, residentNoTicket.NormalizedPlate, vt);
        var s2 = await CheckInAsync(sp, residentWithTicket.RawPlate, vt);
        var s3 = await CheckInAsync(sp, GateTestData.Decorate(externalWithTicket.NormalizedPlate), vt);
        var s4 = await CheckInAsync(sp, typedResidentNoHousehold.NormalizedPlate, vt);
        var s5 = await CheckInAsync(sp, unknownPlate, vt);

        // the session values the report groups by
        await using (var ctx = _db.CreateContext())
        {
            var stored = await ctx.ParkingSessions.AsNoTracking()
                .Where(s => s.VehicleTypeId == vt)
                .ToDictionaryAsync(s => s.SessionId, s => (s.CustomerType, s.IsMonthlyPass, s.CustomerId));
            Assert.Equal((CustomerType.Resident, false, (int?)residentNoTicket.CustomerId), stored[s1]);
            Assert.Equal((CustomerType.Resident, true, (int?)residentWithTicket.CustomerId), stored[s2]);
            Assert.Equal((CustomerType.External, true, (int?)externalWithTicket.CustomerId), stored[s3]);
            Assert.Equal(CustomerType.External, stored[s4].CustomerType);
            Assert.False(stored[s4].IsMonthlyPass);
            Assert.Equal((CustomerType.External, false, (int?)null), stored[s5]);
            Assert.Equal(5, stored.Count);
        }

        await CheckOutAsync(sp, residentNoTicket.NormalizedPlate, s1, op.UserId, PaymentMethod.Cash, Block4h);
        await CheckOutAsync(sp, residentWithTicket.NormalizedPlate, s2, op.UserId, PaymentMethod.Free, 0m);
        await CheckOutAsync(sp, externalWithTicket.NormalizedPlate, s3, op.UserId, PaymentMethod.Free, 0m);
        await CheckOutAsync(sp, typedResidentNoHousehold.NormalizedPlate, s4, op.UserId, PaymentMethod.Cash, Block4h);
        // s5 (unknown visitor) stays in the lot

        using var admin = IntegrationServices.Create(_db);
        await admin.LoginAdminAsync();
        var nowUtc = DateTime.UtcNow;
        var service = new ReportService(admin.DbFactory(), admin.GetRequiredService<IAuthorizationGuard>(), new FixedTimeProvider(nowUtc));
        var range = new ReportDateRange(ReportPeriodCalculator.TodayVn(startUtc.AddHours(-2)), ReportPeriodCalculator.TodayVn(nowUtc));
        ReportFilter Filter(ReportCustomerGroup group) => new(range, vt, group);

        var all = await service.GetReportAsync(Filter(ReportCustomerGroup.All));
        var residents = await service.GetReportAsync(Filter(ReportCustomerGroup.Resident));
        var monthly = await service.GetReportAsync(Filter(ReportCustomerGroup.MonthlyPass));
        var visitors = await service.GetReportAsync(Filter(ReportCustomerGroup.Visitor));

        Assert.Equal(new CustomerGroupBreakdown(2, 1, 2), all.CustomerGroups);
        Assert.Equal(5m, all.Kpis.CheckIns.Current);
        Assert.Equal(4m, all.Kpis.CheckOuts.Current);
        Assert.Equal(1, all.Kpis.VehiclesInLotNow);
        Assert.Equal(2 * Block4h, all.Kpis.NetRevenue.Current);
        Assert.Equal(2 * Block4h, all.Kpis.CashRevenue.Current);
        Assert.Equal(40m, all.Kpis.ResidentSharePercent!.Current);

        Assert.Equal(new CustomerGroupBreakdown(2, 0, 0), residents.CustomerGroups);
        Assert.Equal(2m, residents.Kpis.CheckIns.Current);
        Assert.Equal(Block4h, residents.Kpis.NetRevenue.Current); // only the resident without ticket paid

        Assert.Equal(new CustomerGroupBreakdown(0, 1, 0), monthly.CustomerGroups);
        Assert.Equal(1m, monthly.Kpis.CheckIns.Current);
        Assert.Equal(1m, monthly.Kpis.CheckOuts.Current);
        Assert.Equal(0m, monthly.Kpis.NetRevenue.Current);

        Assert.Equal(new CustomerGroupBreakdown(0, 0, 2), visitors.CustomerGroups);
        Assert.Equal(2m, visitors.Kpis.CheckIns.Current);
        Assert.Equal(1m, visitors.Kpis.CheckOuts.Current);
        Assert.Equal(1, visitors.Kpis.VehiclesInLotNow);
        Assert.Equal(Block4h, visitors.Kpis.NetRevenue.Current);

        var details = await service.GetSessionDetailsAsync(Filter(ReportCustomerGroup.All));
        var groupOf = details.Rows.ToDictionary(r => r.SessionId, r => r.CustomerGroup);
        Assert.Equal(5, details.TotalCount);
        Assert.Equal(ReportCustomerGroup.Resident, groupOf[s1]);
        Assert.Equal(ReportCustomerGroup.Resident, groupOf[s2]);
        Assert.Equal(ReportCustomerGroup.MonthlyPass, groupOf[s3]);
        Assert.Equal(ReportCustomerGroup.Visitor, groupOf[s4]);
        Assert.Equal(ReportCustomerGroup.Visitor, groupOf[s5]);

        // revenue is the FinancialTransactions the checkouts recorded, linked to these sessions
        var ftTotal = await _db.ScalarAsync<decimal>(
            "SELECT COALESCE(SUM(f.\"Amount\"), 0) FROM \"FinancialTransactions\" f JOIN \"ParkingSessions\" s ON s.\"SessionId\" = f.\"ParkingSessionId\" WHERE s.\"VehicleTypeId\" = @vt",
            ("vt", vt));
        Assert.Equal(ftTotal, all.Kpis.NetRevenue.Current);
    }
}
