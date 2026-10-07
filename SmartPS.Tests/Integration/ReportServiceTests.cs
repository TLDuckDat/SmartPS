using Microsoft.Extensions.DependencyInjection;
using SmartPS.Models.Parking;
using SmartPS.Models.Shifts;
using SmartPS.Services.Customers;
using SmartPS.Services.RolePermissions;

namespace SmartPS.Tests.Integration;

/// <summary>
/// R1 / R2 / R4 / R6 against PostgreSQL: AC-1, AC-2, AC-3, AC-4, AC-5, AC-6, E1, E2, E3, E4, spec §6.1 (monthly-ticket
/// revenue from MonthlyTicketPurchases, M2 / M2b), occupancy, Report.View / Report.Export guards. Every test uses its own
/// 2031 date window so tests in this class never see each other's sessions or transactions.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class ReportServiceTests : IClassFixture<PostgresDatabaseFixture>
{
    /// <summary>"Now" for the service under test: after every 2031 window, so occupancy sweeps whole periods.</summary>
    private static readonly DateTime ServiceNowUtc = new(2032, 1, 15, 0, 0, 0, DateTimeKind.Utc);

    private readonly PostgresDatabaseFixture _db;
    private readonly ReportSeed _seed;
    private readonly ResidentVisitorData _data;

    public ReportServiceTests(PostgresDatabaseFixture db)
    {
        _db = db;
        _seed = new ReportSeed(db);
        _data = new ResidentVisitorData(db);
    }

    private static DateTime Utc(int y, int mo, int d, int h, int mi = 0) => new(y, mo, d, h, mi, 0, DateTimeKind.Utc);

    /// <summary>UTC instant of a VN wall-clock time.</summary>
    private static DateTime Vn(int y, int mo, int d, int h, int mi = 0) => new DateTime(y, mo, d, h, mi, 0, DateTimeKind.Utc) - AuditTime.VietnamOffset;

    private static ReportFilter Filter(DateOnly from, DateOnly to, ReportCustomerGroup group = ReportCustomerGroup.All, int? vehicleTypeId = null, int? zoneId = null)
        => new(new ReportDateRange(from, to), vehicleTypeId, group, zoneId, ReportPeriodPreset.Custom);

    private static ReportFilter Day(int y, int mo, int d, ReportCustomerGroup group = ReportCustomerGroup.All)
        => Filter(new DateOnly(y, mo, d), new DateOnly(y, mo, d), group);

    private async Task<(ServiceProvider Sp, ReportService Service)> AdminAsync(DateTime? nowUtc = null)
    {
        var sp = IntegrationServices.Create(_db);
        await sp.LoginAdminAsync();
        return (sp, new ReportService(sp.DbFactory(), sp.GetRequiredService<IAuthorizationGuard>(), new FixedTimeProvider(nowUtc ?? ServiceNowUtc)));
    }

    private async Task<(ServiceProvider Sp, ReportService Service)> LoggedInAsync(string username)
    {
        var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(username);
        return (sp, new ReportService(sp.DbFactory(), sp.GetRequiredService<IAuthorizationGuard>(), new FixedTimeProvider(ServiceNowUtc)));
    }

    // ---- AC-1 / E4 -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AC1_E4_session_and_payment_at_23_30_VN_count_on_that_VN_day()
    {
        _db.RequireAvailable();
        var (sp, service) = await AdminAsync();
        using var spScope = sp;
        // 2031-01-01T16:30Z = 23:30 VN on 01/01; leaves 23:45 VN the same day
        await _seed.PaidSessionAsync(Utc(2031, 1, 1, 16, 30), Utc(2031, 1, 1, 16, 45), 20_000m);

        var day = await service.GetReportAsync(Day(2031, 1, 1));
        var next = await service.GetReportAsync(Day(2031, 1, 2));
        var before = await service.GetReportAsync(Day(2030, 12, 31));

        Assert.Equal(1m, day.Kpis.CheckIns.Current);
        Assert.Equal(1m, day.Kpis.CheckOuts.Current);
        var row = Assert.Single(day.Daily);
        Assert.Equal(new DateOnly(2031, 1, 1), row.DayVn);
        Assert.Equal(1, row.CheckIns);
        Assert.Equal(20_000m, row.NetRevenue);
        Assert.Equal(1, day.Hourly.Single(h => h.HourVn == 23).CheckIns);
        Assert.Equal(20_000m, day.Kpis.NetRevenue.Current);
        Assert.Equal(23, day.Kpis.PeakHour);

        Assert.Equal(0m, next.Kpis.CheckIns.Current);
        Assert.Equal(0m, next.Kpis.NetRevenue.Current);
        Assert.Equal(0m, before.Kpis.CheckIns.Current);
        Assert.Equal(0m, before.Kpis.NetRevenue.Current);
    }

    // ---- AC-2 ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AC2_150_sessions_are_all_counted()
    {
        _db.RequireAvailable();
        var (sp, service) = await AdminAsync();
        using var spScope = sp;
        await _seed.SessionsAsync(150, i => Vn(2031, 2, 10, 6).AddMinutes(i * 3), i => Vn(2031, 2, 10, 6).AddMinutes(i * 3 + 30));

        var report = await service.GetReportAsync(Day(2031, 2, 10));

        Assert.Equal(150m, report.Kpis.CheckIns.Current);
        Assert.Equal(150, report.Daily.Sum(d => d.CheckIns));
        Assert.Equal(150, report.Hourly.Sum(h => h.CheckIns));
        Assert.Equal(150, report.CustomerGroups.Total);
    }

    [Fact]
    public async Task Session_details_cap_rows_and_report_truncation()
    {
        _db.RequireAvailable();
        var (sp, service) = await AdminAsync();
        using var spScope = sp;
        await _seed.SessionsAsync(150, i => Vn(2031, 2, 20, 6).AddMinutes(i * 3), i => Vn(2031, 2, 20, 6).AddMinutes(i * 3 + 30));

        var capped = await service.GetSessionDetailsAsync(Day(2031, 2, 20), maxRows: 100);
        var all = await service.GetSessionDetailsAsync(Day(2031, 2, 20));

        Assert.Equal(100, capped.Rows.Count);
        Assert.Equal(150, capped.TotalCount);
        Assert.True(capped.IsTruncated);
        Assert.Equal(150, all.Rows.Count);
        Assert.False(all.IsTruncated);
        Assert.Equal(all.Rows.OrderBy(r => r.CheckInUtc).ThenBy(r => r.SessionId).Select(r => r.SessionId), all.Rows.Select(r => r.SessionId));
        Assert.All(all.Rows, r => Assert.Equal(DateTimeKind.Utc, r.CheckInUtc.Kind));
    }

    // ---- AC-3 ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AC3_net_revenue_and_method_split_come_from_financial_transactions()
    {
        _db.RequireAvailable();
        var (sp, service) = await AdminAsync();
        using var spScope = sp;
        var a = await _seed.PaidSessionAsync(Vn(2031, 3, 5, 8), Vn(2031, 3, 5, 9), 50_000m, PaymentMethod.Cash);
        await _seed.PaidSessionAsync(Vn(2031, 3, 5, 10), Vn(2031, 3, 5, 11), 30_000m, PaymentMethod.VietQR);
        await _seed.FinancialAsync(FinancialTransactionType.Refund, PaymentMethod.Cash, -10_000m, Vn(2031, 3, 5, 12), a.SessionId);
        await _seed.FinancialAsync(FinancialTransactionType.Adjustment, PaymentMethod.Cash, 5_000m, Vn(2031, 3, 5, 13));
        await _seed.FinancialAsync(FinancialTransactionType.Incident, PaymentMethod.Cash, 77_000m, Vn(2031, 3, 5, 14));

        var report = await service.GetReportAsync(Day(2031, 3, 5));

        Assert.Equal(75_000m, report.Kpis.NetRevenue.Current);
        Assert.Equal(50_000m, report.Kpis.CashRevenue.Current);
        Assert.Equal(30_000m, report.Kpis.VietQrRevenue.Current);
        Assert.Equal(0m, report.Kpis.CardRevenue.Current);
        Assert.Equal(-10_000m, report.Kpis.RefundTotal);
        Assert.Equal(5_000m, report.Kpis.AdjustmentTotal);
        Assert.Equal(75_000m, report.Daily.Sum(d => d.NetRevenue));
    }

    [Fact]
    public async Task Revenue_ignores_session_TotalFee_without_a_financial_transaction()
    {
        _db.RequireAvailable();
        var (sp, service) = await AdminAsync();
        using var spScope = sp;
        await _seed.SessionAsync(Vn(2031, 3, 20, 8), Vn(2031, 3, 20, 9), totalFee: 99_000m);

        var report = await service.GetReportAsync(Day(2031, 3, 20));

        Assert.Equal(1m, report.Kpis.CheckOuts.Current);
        Assert.Equal(0m, report.Kpis.NetRevenue.Current);
    }

    // ---- AC-4 ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AC4_resident_filter_restricts_every_figure_to_resident_sessions()
    {
        _db.RequireAvailable();
        var (sp, service) = await AdminAsync();
        using var spScope = sp;
        var moto = await _seed.MotorbikeTypeIdAsync();
        var zone = await _data.CreateZoneAsync(ZoneAudience.Mixed, moto, 6);
        DateTime In(int h) => Vn(2031, 4, 10, h);
        var residentPlates = new[] { ReportSeed.UniquePlate("RA"), ReportSeed.UniquePlate("RB"), ReportSeed.UniquePlate("RC") };

        await _seed.PaidSessionAsync(In(7), In(8), 10_000m, customerType: CustomerType.Resident, isMonthlyPass: true, plate: residentPlates[0]);
        await _seed.PaidSessionAsync(In(9), In(10), 10_000m, customerType: CustomerType.Resident, isMonthlyPass: false, plate: residentPlates[1]);
        await _seed.SessionAsync(In(11), customerType: CustomerType.Resident, isMonthlyPass: true, slotId: zone.SlotIds[0], plate: residentPlates[2]);
        await _seed.PaidSessionAsync(In(7), In(8), 10_000m, customerType: CustomerType.Regular, isMonthlyPass: true);
        await _seed.PaidSessionAsync(In(8), In(9), 10_000m, customerType: CustomerType.Loyal, isMonthlyPass: true);
        await _seed.PaidSessionAsync(In(7), In(9), 10_000m);
        await _seed.PaidSessionAsync(In(8), In(10), 10_000m, customerType: CustomerType.VIP);
        await _seed.PaidSessionAsync(In(9), In(12), 10_000m, customerType: CustomerType.Loyal);
        await _seed.SessionAsync(In(12), slotId: zone.SlotIds[1]);
        await _seed.FinancialAsync(FinancialTransactionType.Adjustment, PaymentMethod.Cash, 7_000m, In(15));

        var residents = await service.GetReportAsync(Day(2031, 4, 10, ReportCustomerGroup.Resident));
        var all = await service.GetReportAsync(Day(2031, 4, 10));
        var monthly = await service.GetReportAsync(Day(2031, 4, 10, ReportCustomerGroup.MonthlyPass));
        var visitors = await service.GetReportAsync(Day(2031, 4, 10, ReportCustomerGroup.Visitor));

        Assert.Equal(3m, residents.Kpis.CheckIns.Current);
        Assert.Equal(2m, residents.Kpis.CheckOuts.Current);
        Assert.Equal(20_000m, residents.Kpis.NetRevenue.Current); // resident fees only; the session-less adjustment is excluded (A6)
        Assert.Equal(20_000m, residents.Daily.Sum(d => d.NetRevenue));
        Assert.Equal(new CustomerGroupBreakdown(3, 0, 0), residents.CustomerGroups);
        Assert.Equal(3, residents.VehicleTypes.Sum(v => v.CheckIns));
        Assert.Equal(3, residents.Hourly.Sum(h => h.CheckIns));
        Assert.Equal(1, residents.Kpis.VehiclesInLotNow);
        Assert.Equal(3, residents.TopPlates.Count);
        Assert.All(residents.TopPlates, p => Assert.Contains(p.LicensePlate, residentPlates));
        Assert.Equal(1, residents.Zones.Single(z => z.ZoneId == zone.ZoneId).OccupiedSlots);
        Assert.Single(ReportChartMapper.CustomerGroups(residents).Slices, s => s.Value > 0);
        Assert.Equal(100m, residents.Kpis.ResidentSharePercent!.Current);

        var details = await service.GetSessionDetailsAsync(Day(2031, 4, 10, ReportCustomerGroup.Resident));
        Assert.Equal(3, details.TotalCount);
        Assert.Equal(3, details.Rows.Count);
        Assert.All(details.Rows, r => Assert.Equal(ReportCustomerGroup.Resident, r.CustomerGroup));

        Assert.Equal(9m, all.Kpis.CheckIns.Current);
        Assert.Equal(new CustomerGroupBreakdown(3, 2, 4), all.CustomerGroups);
        Assert.Equal(77_000m, all.Kpis.NetRevenue.Current); // 7 fees + the session-less adjustment
        Assert.Equal(2, all.Zones.Single(z => z.ZoneId == zone.ZoneId).OccupiedSlots);
        Assert.Equal(6, all.Zones.Single(z => z.ZoneId == zone.ZoneId).TotalSlots);
        Assert.Equal(2m, monthly.Kpis.CheckIns.Current);
        Assert.Equal(20_000m, monthly.Kpis.NetRevenue.Current);
        Assert.Equal(4m, visitors.Kpis.CheckIns.Current);
        Assert.Equal(30_000m, visitors.Kpis.NetRevenue.Current);
        Assert.Equal(1, visitors.Zones.Single(z => z.ZoneId == zone.ZoneId).OccupiedSlots); // the active visitor in slot 2
    }

    [Fact]
    public async Task Vehicle_type_and_zone_filters_apply_to_sessions_and_linked_revenue()
    {
        _db.RequireAvailable();
        var (sp, service) = await AdminAsync();
        using var spScope = sp;
        var isolated = await _data.CreateIsolatedVehicleTypeAsync();
        var zone = await _data.CreateZoneAsync(ZoneAudience.Mixed, isolated, 3);
        DateTime In(int h) => Vn(2031, 4, 20, h);
        await _seed.PaidSessionAsync(In(8), In(9), 12_000m, vehicleTypeId: isolated, slotId: zone.SlotIds[0]);
        await _seed.PaidSessionAsync(In(8), In(9), 3_000m, vehicleTypeId: isolated);
        await _seed.PaidSessionAsync(In(8), In(9), 50_000m);

        var byType = await service.GetReportAsync(Filter(new DateOnly(2031, 4, 20), new DateOnly(2031, 4, 20), vehicleTypeId: isolated));
        var byZone = await service.GetReportAsync(Filter(new DateOnly(2031, 4, 20), new DateOnly(2031, 4, 20), zoneId: zone.ZoneId));

        Assert.Equal(2m, byType.Kpis.CheckIns.Current);
        Assert.Equal(15_000m, byType.Kpis.NetRevenue.Current);
        Assert.Equal(2, byType.VehicleTypes.Single(v => v.VehicleTypeId == isolated).CheckIns);
        Assert.Equal(0, byType.VehicleTypes.Where(v => v.VehicleTypeId != isolated).Sum(v => v.CheckIns));
        Assert.Equal(1m, byZone.Kpis.CheckIns.Current);
        Assert.Equal(12_000m, byZone.Kpis.NetRevenue.Current);
        Assert.Null(byZone.Kpis.MonthlyTicketRevenue);
        Assert.Null(byZone.Kpis.MonthlyTicketSales);
    }

    // ---- AC-5 ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AC5_120_vs_100_is_up_20_percent_and_an_empty_previous_period_has_no_change()
    {
        _db.RequireAvailable();
        var (sp, service) = await AdminAsync();
        using var spScope = sp;
        // current period 11–20/05 (120 check-ins), previous period 01–10/05 (100 check-ins)
        await _seed.SessionsAsync(120, i => Vn(2031, 5, 11 + i % 10, 9).AddMinutes(i));
        await _seed.SessionsAsync(100, i => Vn(2031, 5, 1 + i % 10, 9).AddMinutes(i));

        var current = await service.GetReportAsync(Filter(new DateOnly(2031, 5, 11), new DateOnly(2031, 5, 20)));
        var first = await service.GetReportAsync(Filter(new DateOnly(2031, 5, 1), new DateOnly(2031, 5, 10)));

        Assert.Equal(new ReportDateRange(new DateOnly(2031, 5, 1), new DateOnly(2031, 5, 10)), current.PreviousRange);
        Assert.Equal(120m, current.Kpis.CheckIns.Current);
        Assert.Equal(100m, current.Kpis.CheckIns.Previous);
        Assert.Equal(20m, current.Kpis.CheckIns.ChangePercent);
        Assert.Equal(KpiTrend.Up, current.Kpis.CheckIns.Trend);
        Assert.Equal("▲20%", ReportFormat.Change(current.Kpis.CheckIns));

        Assert.Equal(100m, first.Kpis.CheckIns.Current);
        Assert.Null(first.Kpis.CheckIns.ChangePercent);
        Assert.Equal("—", ReportFormat.Change(first.Kpis.CheckIns));
    }

    // ---- AC-6 ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AC6_peak_hour_is_the_VN_hour_with_most_check_ins_and_ties_go_earlier()
    {
        _db.RequireAvailable();
        var (sp, service) = await AdminAsync();
        using var spScope = sp;
        await _seed.SessionsAsync(3, i => Vn(2031, 6, 10, 8, i * 10));
        await _seed.SessionsAsync(3, i => Vn(2031, 6, 10, 17, i * 10));
        await _seed.SessionsAsync(1, _ => Vn(2031, 6, 10, 9, 5));

        var report = await service.GetReportAsync(Day(2031, 6, 10));

        Assert.Equal(8, report.Kpis.PeakHour);
        Assert.Equal(3, report.Hourly.Single(h => h.HourVn == 8).CheckIns);
        Assert.Equal(3, report.Hourly.Single(h => h.HourVn == 17).CheckIns);
        Assert.Equal(1, report.Hourly.Single(h => h.HourVn == 9).CheckIns);
    }

    // ---- E2 / E3 ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task E2_overnight_session_counts_entry_on_its_day_and_exit_on_the_next()
    {
        _db.RequireAvailable();
        var (sp, service) = await AdminAsync();
        using var spScope = sp;
        await _seed.SessionAsync(Vn(2031, 8, 10, 23), Vn(2031, 8, 11, 1));

        var both = await service.GetReportAsync(Filter(new DateOnly(2031, 8, 10), new DateOnly(2031, 8, 11)));
        var first = await service.GetReportAsync(Day(2031, 8, 10));
        var second = await service.GetReportAsync(Day(2031, 8, 11));

        Assert.Equal((1, 0), (both.Daily[0].CheckIns, both.Daily[0].CheckOuts));
        Assert.Equal((0, 1), (both.Daily[1].CheckIns, both.Daily[1].CheckOuts));
        Assert.Equal(120.0, both.Daily[1].AverageDurationMinutes!.Value, 3);
        Assert.Equal(0m, first.Kpis.CheckOuts.Current);
        Assert.Equal(1m, first.Kpis.CheckIns.Current);
        Assert.Equal(0m, second.Kpis.CheckIns.Current);
        Assert.Equal(1m, second.Kpis.CheckOuts.Current);
    }

    [Fact]
    public async Task E3_refund_in_a_later_period_reduces_that_period_only()
    {
        _db.RequireAvailable();
        var (sp, service) = await AdminAsync();
        using var spScope = sp;
        var session = await _seed.PaidSessionAsync(Vn(2031, 9, 5, 8), Vn(2031, 9, 5, 10), 40_000m);
        await _seed.FinancialAsync(FinancialTransactionType.Refund, PaymentMethod.Cash, -40_000m, Vn(2031, 9, 20, 10), session.SessionId);

        var original = await service.GetReportAsync(Filter(new DateOnly(2031, 9, 1), new DateOnly(2031, 9, 10)));
        var refundPeriod = await service.GetReportAsync(Filter(new DateOnly(2031, 9, 15), new DateOnly(2031, 9, 25)));

        Assert.Equal(40_000m, original.Kpis.NetRevenue.Current);
        Assert.Equal(0m, original.Kpis.RefundTotal);
        Assert.Equal(-40_000m, refundPeriod.Kpis.NetRevenue.Current);
        Assert.Equal(-40_000m, refundPeriod.Kpis.RefundTotal);
        Assert.Equal(0m, refundPeriod.Kpis.CashRevenue.Current);
        Assert.Equal(-40_000m, refundPeriod.Daily.Single(d => d.DayVn == new DateOnly(2031, 9, 20)).NetRevenue);
    }

    // ---- spec §6.1: monthly-ticket revenue (M2 / M2b) --------------------------------------------------------------

    [Fact]
    public async Task M2_MonthlyTickets_create_and_renew_by_purchase_time()
    {
        _db.RequireAvailable();
        var (sp, service) = await AdminAsync();
        using var spScope = sp;
        var moto = await _seed.MotorbikeTypeIdAsync();
        // Resident T1: Create in June (previous window), Renew in July (current window)
        await _seed.CreateTicketWithPurchasesAsync(isResident: true, moto,
            (TicketPurchaseKind.Create, 300_000m, Utc(2031, 6, 15, 3)),
            (TicketPurchaseKind.Renew, 300_000m, Utc(2031, 7, 10, 3)));
        // Non-resident T2: Create at 2031-06-30T17:30Z = 00:30 VN on 01/07 (inside July by the VN boundary)
        await _seed.CreateTicketWithPurchasesAsync(isResident: false, moto,
            (TicketPurchaseKind.Create, 120_000m, Utc(2031, 6, 30, 17, 30)));
        var july = (From: new DateOnly(2031, 7, 1), To: new DateOnly(2031, 7, 31));

        var all = await service.GetReportAsync(Filter(july.From, july.To));
        var residents = await service.GetReportAsync(Filter(july.From, july.To, ReportCustomerGroup.Resident));
        var monthly = await service.GetReportAsync(Filter(july.From, july.To, ReportCustomerGroup.MonthlyPass));
        var visitors = await service.GetReportAsync(Filter(july.From, july.To, ReportCustomerGroup.Visitor));
        var cars = await service.GetReportAsync(Filter(july.From, july.To, vehicleTypeId: await _seed.CarTypeIdAsync()));
        var zoned = await service.GetReportAsync(Filter(july.From, july.To, zoneId: await _data.ZoneIdAsync("ZONE_A")));

        var sales = all.Kpis.MonthlyTicketSales!;
        Assert.Equal(120_000m, sales.NewRevenue);
        Assert.Equal(1, sales.NewCount);
        Assert.Equal(300_000m, sales.RenewRevenue);
        Assert.Equal(1, sales.RenewCount);
        Assert.Equal(420_000m, all.Kpis.MonthlyTicketRevenue!.Current);
        Assert.Equal(300_000m, all.Kpis.MonthlyTicketRevenue.Previous);
        Assert.Equal(40m, all.Kpis.MonthlyTicketRevenue.ChangePercent);
        Assert.Equal(0m, all.Kpis.NetRevenue.Current); // ticket purchases are not FinancialTransactions

        Assert.Equal(300_000m, residents.Kpis.MonthlyTicketRevenue!.Current);
        Assert.Equal(0, residents.Kpis.MonthlyTicketSales!.NewCount);
        Assert.Equal(1, residents.Kpis.MonthlyTicketSales.RenewCount);
        Assert.Equal(120_000m, monthly.Kpis.MonthlyTicketRevenue!.Current);
        Assert.Equal(1, monthly.Kpis.MonthlyTicketSales!.NewCount);
        Assert.Equal(0m, visitors.Kpis.MonthlyTicketRevenue!.Current);
        Assert.Equal(0, visitors.Kpis.MonthlyTicketSales!.TotalCount);
        Assert.Equal(0m, cars.Kpis.MonthlyTicketRevenue!.Current);
        Assert.Null(zoned.Kpis.MonthlyTicketRevenue);
        Assert.Null(zoned.Kpis.MonthlyTicketSales);
    }

    [Fact]
    public async Task M2b_MonthlyTickets_through_Task1_service()
    {
        _db.RequireAvailable();
        var manager = await TestUsers.CreateAsync(_db.Factory, "Manager"); // Customer.Manage + Report.View
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(manager.Username);
        var moto = await _seed.MotorbikeTypeIdAsync();
        var planId = await _data.PlanIdAsync("Gói Xe Máy 1 Tháng");
        var subscriber = await _data.CreateSubscriberAsync(moto, isResident: true, withTicket: false);
        var tickets = sp.GetRequiredService<IMonthlyTicketService>();

        var created = await tickets.CreateTicketAsync(new CreateTicketRequest(subscriber.CustomerId, subscriber.Plate, planId));
        Assert.True(created.Success, $"{created.Error}: {created.Message}");
        var renewed = await tickets.RenewTicketAsync(created.Value, planId);
        Assert.True(renewed.Success, $"{renewed.Error}: {renewed.Message}");

        var planPrice = await _db.ScalarAsync<decimal>("SELECT \"TotalPrice\" FROM \"MonthlyTicketPlans\" WHERE \"PlanId\" = @p", ("p", planId));
        var today = ReportPeriodCalculator.TodayVn(DateTime.UtcNow);
        var service = sp.GetRequiredService<IReportService>();

        var report = await service.GetReportAsync(new ReportFilter(new ReportDateRange(today, today), Preset: ReportPeriodPreset.Today));

        var sales = report.Kpis.MonthlyTicketSales!;
        Assert.True(sales.NewCount >= 1, $"NewCount = {sales.NewCount}");
        Assert.True(sales.RenewCount >= 1, $"RenewCount = {sales.RenewCount}");
        Assert.True(sales.RenewRevenue >= planPrice, $"RenewRevenue {sales.RenewRevenue} < plan price {planPrice}");
    }

    // ---- occupancy -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Occupancy_average_and_peak_over_a_full_VN_day()
    {
        _db.RequireAvailable();
        var (sp, service) = await AdminAsync();
        using var spScope = sp;
        var isolated = await _data.CreateIsolatedVehicleTypeAsync();
        await _data.CreateZoneAsync(ZoneAudience.Mixed, isolated, 4);
        // One vehicle already parked before the day starts and still active; a second one from 00:30 to 02:30 VN.
        await _seed.SessionAsync(Vn(2031, 10, 9, 17), vehicleTypeId: isolated);
        await _seed.SessionAsync(Vn(2031, 10, 10, 0, 30), Vn(2031, 10, 10, 2, 30), vehicleTypeId: isolated);

        var report = await service.GetReportAsync(Filter(new DateOnly(2031, 10, 10), new DateOnly(2031, 10, 10), vehicleTypeId: isolated));

        // hourly samples: 2, 2, 1 × 22 → average 26/24 = 1.083 vehicles of 4 = 27.08 %, peak 2 of 4 = 50 %
        var average = report.Kpis.AverageOccupancyPercent!.Current;
        var peak = report.Kpis.PeakOccupancyPercent!.Current;
        Assert.InRange(average, 27.0m, 27.2m);
        Assert.InRange(peak, 49.9m, 50.1m);
        Assert.Equal(1, report.Kpis.VehiclesInLotNow);
        Assert.Equal(1m, report.Kpis.CheckIns.Current);
    }

    // ---- guards ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Report_View_denied_is_audited_with_the_required_permission()
    {
        _db.RequireAvailable();
        using var admin = IntegrationServices.Create(_db);
        await admin.LoginAdminAsync();
        var rolePermissions = admin.GetRequiredService<IRolePermissionService>();
        var operatorRoleId = await TestUsers.RoleIdAsync(_db.Factory, "Operator");
        var originalGrants = await TestUsers.GrantsAsync(_db.Factory, "Operator");
        await rolePermissions.SaveAsync(new Dictionary<int, IReadOnlyCollection<string>>
        {
            [operatorRoleId] = originalGrants.Where(p => p != Permissions.ReportView).ToArray()
        });
        try
        {
            var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
            var (sp, service) = await LoggedInAsync(op.Username);
            using var spScope = sp;
            var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

            var ex = await Assert.ThrowsAsync<PermissionDeniedException>(() => service.GetReportAsync(Day(2031, 1, 1)));

            Assert.Equal(new[] { Permissions.ReportView }, ex.RequiredPermissions);
            var denied = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.AccessDenied));
            Assert.Equal(AuditOutcome.Denied, denied.Outcome);
            Assert.Equal(new[] { "Report.View" }, AuditDb.StringArray(AuditDb.Details(denied), "requiredPermissions"));
        }
        finally
        {
            await rolePermissions.SaveAsync(new Dictionary<int, IReadOnlyCollection<string>> { [operatorRoleId] = originalGrants.ToArray() });
        }
    }

    [Fact]
    public async Task Session_details_require_Report_Export()
    {
        _db.RequireAvailable();
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        var (sp, service) = await LoggedInAsync(op.Username);
        using var spScope = sp;
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        await Assert.ThrowsAsync<PermissionDeniedException>(() => service.GetSessionDetailsAsync(Day(2031, 2, 10)));

        var denied = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.AccessDenied));
        Assert.Equal(new[] { "Report.Export" }, AuditDb.StringArray(AuditDb.Details(denied), "requiredPermissions"));
    }

    [Fact]
    public async Task Operator_with_Report_View_can_read_reports()
    {
        _db.RequireAvailable();
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        var (sp, service) = await LoggedInAsync(op.Username);
        using var spScope = sp;

        var report = await service.GetReportAsync(Day(2031, 12, 20));

        Assert.Equal(0m, report.Kpis.CheckIns.Current);
    }

    [Fact]
    public async Task Filter_options_list_vehicle_types_and_zones_from_the_database()
    {
        _db.RequireAvailable();
        var (sp, service) = await AdminAsync();
        using var spScope = sp;

        var options = await service.GetFilterOptionsAsync();

        Assert.Contains(options.VehicleTypes, v => v.Name == "Xe máy");
        Assert.Contains(options.VehicleTypes, v => v.Name == "Xe ô tô");
        Assert.Equal(await _db.ScalarAsync<long>("SELECT count(*) FROM \"VehicleTypes\""), options.VehicleTypes.Count);
        Assert.Equal(await _db.ScalarAsync<long>("SELECT count(*) FROM \"ParkingZones\""), options.Zones.Count);
    }

    // ---- shifts tab ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Shifts_opened_in_the_period_are_listed_with_the_opener_name()
    {
        _db.RequireAvailable();
        var (sp, service) = await AdminAsync();
        using var spScope = sp;
        var shift = await _seed.CreateShiftAsync(Vn(2031, 11, 5, 7), Vn(2031, 11, 5, 15), ShiftStatus.Locked, 100_000m, 180_000m, 175_000m);

        var report = await service.GetReportAsync(Day(2031, 11, 5));
        var other = await service.GetReportAsync(Day(2031, 11, 6));

        var row = Assert.Single(report.Shifts);
        Assert.Equal(shift.ShiftId, row.ShiftId);
        Assert.Equal(await _seed.AdminFullNameAsync(), row.OpenedByName);
        Assert.Equal(180_000m, row.ExpectedCash);
        Assert.Equal(175_000m, row.ActualCash);
        Assert.Equal(-5_000m, row.Difference);
        Assert.Equal(ShiftStatus.Locked, row.Status);
        Assert.Equal(DateTimeKind.Utc, row.OpenedAtUtc.Kind);
        Assert.Empty(other.Shifts);
    }

    [Fact]
    public async Task Top_plates_group_by_normalized_plate()
    {
        _db.RequireAvailable();
        var (sp, service) = await AdminAsync();
        using var spScope = sp;
        var plate = ReportSeed.UniquePlate("TP");
        var decorated = plate[..3].ToLowerInvariant() + "-" + plate[3..];
        await _seed.SessionAsync(Vn(2031, 11, 20, 8), Vn(2031, 11, 20, 9), plate: plate);
        await _seed.SessionAsync(Vn(2031, 11, 20, 10), Vn(2031, 11, 20, 11), plate: decorated);
        await _seed.SessionAsync(Vn(2031, 11, 20, 12), Vn(2031, 11, 20, 13));

        var report = await service.GetReportAsync(Day(2031, 11, 20));

        var top = report.TopPlates[0];
        Assert.Equal(1, top.Rank);
        Assert.Equal(2, top.Visits);
        Assert.Equal(2, report.TopPlates.Count);
        Assert.True(report.TopPlates.Count <= ReportLimits.TopPlateCount);
    }

    // ---- E1 --------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task E1_empty_window_is_all_zero_with_null_changes_and_empty_charts()
    {
        _db.RequireAvailable();
        var (sp, service) = await AdminAsync();
        using var spScope = sp;

        var report = await service.GetReportAsync(Filter(new DateOnly(2031, 12, 1), new DateOnly(2031, 12, 7)));

        Assert.Equal(0m, report.Kpis.CheckIns.Current);
        Assert.Null(report.Kpis.CheckIns.ChangePercent);
        Assert.Equal(0m, report.Kpis.NetRevenue.Current);
        Assert.Null(report.Kpis.NetRevenue.ChangePercent);
        Assert.Null(report.Kpis.PeakHour);
        Assert.Equal(7, report.Daily.Count);
        Assert.All(report.Daily, d => Assert.Equal(0, d.CheckIns + d.CheckOuts));
        Assert.Equal(24, report.Hourly.Count);
        Assert.Empty(report.TopPlates);
        Assert.Empty(report.Shifts);
        Assert.Equal(0, report.CustomerGroups.Total);
        Assert.False(report.HasActivity);
        Assert.False(ReportChartMapper.RevenueByDay(report).HasData);
        Assert.False(ReportChartMapper.HourlyTraffic(report).HasData);
        Assert.False(ReportChartMapper.DailyTraffic(report).HasData);
        Assert.False(ReportChartMapper.CustomerGroups(report).HasData);
        Assert.False(ReportChartMapper.VehicleTypeMix(report).HasData);
        Assert.Equal(await _db.ScalarAsync<long>("SELECT count(*) FROM \"VehicleTypes\""), report.VehicleTypes.Count);
        Assert.Equal(0m, report.Kpis.MonthlyTicketRevenue!.Current);
    }
}
