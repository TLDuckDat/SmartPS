using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartPS.Models.Parking;
using SmartPS.Services.Audit;
using SmartPS.Services.Common;
using SmartPS.Services.Customers;

namespace SmartPS.Tests.Integration;

/// <summary>AC-9, R3, R7, E5, M2, n6, ADDENDUM A (Renew purchases): ticket create / renew / suspend / resume rules.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class MonthlyTicketServiceTests : IClassFixture<PostgresDatabaseFixture>
{
    private const string Moto1Month = "Gói Xe Máy 1 Tháng";
    private const string Car1Month = "Gói Ô Tô 1 Tháng";

    private readonly PostgresDatabaseFixture _db;
    private readonly ResidentVisitorData _data;

    public MonthlyTicketServiceTests(PostgresDatabaseFixture db)
    {
        _db = db;
        _data = new ResidentVisitorData(db);
    }

    private async Task<(ServiceProvider Sp, int UserId)> ManagerAsync()
    {
        var manager = await TestUsers.CreateAsync(_db.Factory, "Manager");
        var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(manager.Username);
        return (sp, manager.UserId);
    }

    private static IMonthlyTicketService Tickets(IServiceProvider sp) => sp.GetRequiredService<IMonthlyTicketService>();

    private async Task<MonthlyTicket> TicketAsync(int ticketId)
    {
        await using var ctx = _db.CreateContext();
        return await ctx.MonthlyTickets.AsNoTracking().SingleAsync(t => t.TicketId == ticketId);
    }

    private async Task<List<MonthlyTicketPurchase>> PurchasesAsync(int ticketId)
    {
        await using var ctx = _db.CreateContext();
        return await ctx.MonthlyTicketPurchases.AsNoTracking().Where(p => p.TicketId == ticketId).OrderBy(p => p.MonthlyTicketPurchaseId).ToListAsync();
    }

    private async Task<decimal> PlanPriceAsync(int planId)
    {
        await using var ctx = _db.CreateContext();
        return await ctx.MonthlyTicketPlans.Where(p => p.PlanId == planId).Select(p => p.TotalPrice).SingleAsync();
    }

    private static DateTime VnMidnight(DateOnly date) => AuditTime.VietnamDateStartUtc(date);

    private static DateOnly TodayVn() => TicketDates.TodayVn(DateTime.UtcNow);

    /// <summary>Motorbike subscriber (seed type) with a ticket [start, end) and its Create purchase.</summary>
    private async Task<TestSubscriber> SubscriberWithTicketAsync(DateTime startUtc, DateTime endUtc, MonthlyTicketStatus status = MonthlyTicketStatus.Active, bool resident = true)
    {
        var moto = await _data.MotorbikeTypeIdAsync();
        var s = await _data.CreateSubscriberAsync(moto, resident, withTicket: false);
        var ticketId = await _data.CreateTicketAsync(s.CustomerId, s.Plate, moto, startUtc, endUtc, status, planId: await _data.PlanIdAsync(Moto1Month));
        return s with { TicketId = ticketId };
    }

    [Fact]
    public async Task AC9_renewing_an_expired_ticket_restarts_today_VN_and_records_a_renew_purchase()
    {
        _db.RequireAvailable();
        var (sp, managerId) = await ManagerAsync();
        using var spScope = sp;
        var today = TodayVn();
        var s = await SubscriberWithTicketAsync(VnMidnight(today.AddDays(-40)), VnMidnight(today.AddDays(-10)));
        var planId = await _data.PlanIdAsync(Moto1Month);
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var result = await Tickets(sp).RenewTicketAsync(s.TicketId!.Value, planId);

        Assert.True(result.Success, $"{result.Error}: {result.Message}");
        var t = await TicketAsync(s.TicketId.Value);
        Assert.Equal(VnMidnight(today), t.StartDate);
        Assert.Equal(VnMidnight(today.AddMonths(1)), t.EndDate);
        Assert.Equal(MonthlyTicketStatus.Active, t.Status);
        Assert.Equal(planId, t.PlanId);
        Assert.Equal(await PlanPriceAsync(planId), t.MonthlyPrice);

        var purchases = await PurchasesAsync(s.TicketId.Value);
        Assert.Equal(2, purchases.Count);
        var renew = purchases[^1];
        Assert.Equal(TicketPurchaseKind.Renew, renew.Kind);
        Assert.Equal(VnMidnight(today), renew.PeriodStartUtc);
        Assert.Equal(t.EndDate, renew.PeriodEndUtc);
        Assert.Equal(await PlanPriceAsync(planId), renew.Price);
        Assert.Equal(planId, renew.PlanId);
        Assert.Equal(managerId, renew.CreatedByUserId);

        var row = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.TicketRenew));
        Assert.Equal("MonthlyTicket", row.EntityType);
        Assert.Equal(s.TicketId.Value.ToString(), row.EntityId);
        AuditDb.HasKeys(row, "ticketCode", "planId", "price", "purchaseId", "before", "after");
        var d = AuditDb.Details(row);
        Assert.Equal(renew.MonthlyTicketPurchaseId, d.GetProperty("purchaseId").GetInt64());
        Assert.True(d.GetProperty("before").TryGetProperty("endDate", out _));
        Assert.True(d.GetProperty("before").TryGetProperty("status", out _));
        Assert.True(d.GetProperty("after").TryGetProperty("startDate", out _));
    }

    [Fact]
    public async Task AC9_n6_renewing_an_active_ticket_extends_from_the_old_end()
    {
        _db.RequireAvailable();
        var (sp, _) = await ManagerAsync();
        using var spScope = sp;
        var today = TodayVn();
        var start = VnMidnight(today.AddDays(-10));
        var oldEndDate = today.AddDays(12);
        var s = await SubscriberWithTicketAsync(start, VnMidnight(oldEndDate));
        var planId = await _data.PlanIdAsync(Moto1Month);

        var result = await Tickets(sp).RenewTicketAsync(s.TicketId!.Value, planId);

        Assert.True(result.Success, $"{result.Error}: {result.Message}"); // the ticket does not overlap itself (n6)
        var t = await TicketAsync(s.TicketId.Value);
        Assert.Equal(start, t.StartDate);
        Assert.Equal(VnMidnight(oldEndDate.AddMonths(1)), t.EndDate);
        var renew = (await PurchasesAsync(s.TicketId.Value))[^1];
        Assert.Equal(TicketPurchaseKind.Renew, renew.Kind);
        Assert.Equal(VnMidnight(oldEndDate), renew.PeriodStartUtc);
        Assert.Equal(t.EndDate, renew.PeriodEndUtc);

        // Renewing twice keeps chaining
        Assert.True((await Tickets(sp).RenewTicketAsync(s.TicketId.Value, planId)).Success);
        Assert.Equal(VnMidnight(oldEndDate.AddMonths(2)), (await TicketAsync(s.TicketId.Value)).EndDate);
        Assert.Equal(3, (await PurchasesAsync(s.TicketId.Value)).Count);
    }

    [Fact]
    public async Task AC9_E5_suspended_ticket_cannot_be_renewed()
    {
        _db.RequireAvailable();
        var (sp, _) = await ManagerAsync();
        using var spScope = sp;
        var today = TodayVn();
        var s = await SubscriberWithTicketAsync(VnMidnight(today.AddDays(-5)), VnMidnight(today.AddDays(25)), MonthlyTicketStatus.Suspended);
        var before = await TicketAsync(s.TicketId!.Value);
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var result = await Tickets(sp).RenewTicketAsync(s.TicketId.Value, await _data.PlanIdAsync(Moto1Month));

        Assert.False(result.Success);
        Assert.Equal(OperationError.TicketSuspended, result.Error);
        var after = await TicketAsync(s.TicketId.Value);
        Assert.Equal(before.EndDate, after.EndDate);
        Assert.Equal(MonthlyTicketStatus.Suspended, after.Status);
        Assert.Single(await PurchasesAsync(s.TicketId.Value));
        Assert.Equal(idBefore, await AuditDb.MaxIdAsync(_db.Factory));
    }

    [Fact]
    public async Task Renew_checks_plan_and_vehicle_type()
    {
        _db.RequireAvailable();
        var (sp, _) = await ManagerAsync();
        using var spScope = sp;
        var today = TodayVn();
        var s = await SubscriberWithTicketAsync(VnMidnight(today.AddDays(-5)), VnMidnight(today.AddDays(25)));

        Assert.Equal(OperationError.PlanNotFound, (await Tickets(sp).RenewTicketAsync(s.TicketId!.Value, int.MaxValue)).Error);
        Assert.Equal(OperationError.PlanVehicleTypeMismatch, (await Tickets(sp).RenewTicketAsync(s.TicketId.Value, await _data.PlanIdAsync(Car1Month))).Error);
        Assert.Equal(OperationError.NotFound, (await Tickets(sp).RenewTicketAsync(int.MaxValue, await _data.PlanIdAsync(Moto1Month))).Error);
        Assert.Single(await PurchasesAsync(s.TicketId.Value));
    }

    [Fact]
    public async Task R3_create_rejects_overlap_foreign_plate_and_wrong_plan_type()
    {
        _db.RequireAvailable();
        var (sp, _) = await ManagerAsync();
        using var spScope = sp;
        var today = TodayVn();
        var moto1 = await _data.PlanIdAsync(Moto1Month);
        var s = await SubscriberWithTicketAsync(VnMidnight(today.AddDays(-5)), VnMidnight(today.AddDays(25)));
        var other = await _data.CreateCustomerAsync(isResident: true);
        var ticketsBefore = await _data.CountAsync("SELECT count(*) FROM \"MonthlyTickets\"");
        var auditBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var overlap = await Tickets(sp).CreateTicketAsync(new CreateTicketRequest(s.CustomerId, s.Plate, moto1));
        var foreign = await Tickets(sp).CreateTicketAsync(new CreateTicketRequest(other, s.Plate, moto1));
        var wrongType = await Tickets(sp).CreateTicketAsync(new CreateTicketRequest(s.CustomerId, s.Plate, await _data.PlanIdAsync(Car1Month), today.AddMonths(2)));
        var noPlan = await Tickets(sp).CreateTicketAsync(new CreateTicketRequest(s.CustomerId, s.Plate, int.MaxValue));
        var noCustomer = await Tickets(sp).CreateTicketAsync(new CreateTicketRequest(int.MaxValue, s.Plate, moto1));

        Assert.Equal(OperationError.TicketOverlap, overlap.Error);
        Assert.Equal(OperationError.PlateNotOwnedByCustomer, foreign.Error);
        Assert.Equal(OperationError.PlanVehicleTypeMismatch, wrongType.Error);
        Assert.Equal(OperationError.PlanNotFound, noPlan.Error);
        Assert.Equal(OperationError.NotFound, noCustomer.Error);
        Assert.Equal(ticketsBefore, await _data.CountAsync("SELECT count(*) FROM \"MonthlyTickets\""));
        Assert.Equal(auditBefore, await AuditDb.MaxIdAsync(_db.Factory));

        // A ticket starting when the current one ends does not overlap ([Start, End) is half-open)
        var next = await Tickets(sp).CreateTicketAsync(new CreateTicketRequest(s.CustomerId, s.Plate, moto1, today.AddDays(25)));
        Assert.True(next.Success, $"{next.Error}: {next.Message}");
        Assert.Equal(VnMidnight(today.AddDays(25)), (await TicketAsync(next.Value)).StartDate);
    }

    [Fact]
    public async Task Create_for_a_locked_customer_is_rejected()
    {
        _db.RequireAvailable();
        var (sp, _) = await ManagerAsync();
        using var spScope = sp;
        var moto = await _data.MotorbikeTypeIdAsync();
        var locked = await _data.CreateSubscriberAsync(moto, isResident: true, withTicket: false, customerActive: false);

        var result = await Tickets(sp).CreateTicketAsync(new CreateTicketRequest(locked.CustomerId, locked.Plate, await _data.PlanIdAsync(Moto1Month)));

        Assert.Equal(OperationError.CustomerInactive, result.Error);
    }

    [Fact]
    public async Task Create_with_a_chosen_start_date_uses_VN_midnights()
    {
        _db.RequireAvailable();
        var (sp, _) = await ManagerAsync();
        using var spScope = sp;
        var moto = await _data.MotorbikeTypeIdAsync();
        var s = await _data.CreateSubscriberAsync(moto, isResident: false, withTicket: false);
        var start = TodayVn().AddDays(3);

        var result = await Tickets(sp).CreateTicketAsync(new CreateTicketRequest(s.CustomerId, ResidentVisitorData.Decorate(s.Plate), await _data.PlanIdAsync("Gói Xe Máy 3 Tháng (Tiết kiệm)"), start, "ghi chú"));

        Assert.True(result.Success, $"{result.Error}: {result.Message}");
        var t = await TicketAsync(result.Value);
        Assert.Equal(VnMidnight(start), t.StartDate);
        Assert.Equal(VnMidnight(start.AddMonths(3)), t.EndDate);
        Assert.Equal(s.Plate, t.RegisteredLicensePlate);
        var purchase = Assert.Single(await PurchasesAsync(result.Value));
        Assert.Equal(TicketPurchaseKind.Create, purchase.Kind);
        Assert.Equal(t.StartDate, purchase.PeriodStartUtc);
        Assert.Equal(t.EndDate, purchase.PeriodEndUtc);
    }

    [Fact]
    public async Task Suspend_and_resume_change_state_and_are_audited()
    {
        _db.RequireAvailable();
        var (sp, _) = await ManagerAsync();
        using var spScope = sp;
        var today = TodayVn();
        var s = await SubscriberWithTicketAsync(VnMidnight(today.AddDays(-5)), VnMidnight(today.AddDays(25)));
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var suspended = await Tickets(sp).SuspendTicketAsync(s.TicketId!.Value, "Khách đi công tác");
        Assert.True(suspended.Success, suspended.Message);
        Assert.Equal(MonthlyTicketStatus.Suspended, (await TicketAsync(s.TicketId.Value)).Status);
        Assert.Equal(OperationError.InvalidTicketState, (await Tickets(sp).SuspendTicketAsync(s.TicketId.Value)).Error);

        var resumed = await Tickets(sp).ResumeTicketAsync(s.TicketId.Value);
        Assert.True(resumed.Success, resumed.Message);
        Assert.Equal(MonthlyTicketStatus.Active, (await TicketAsync(s.TicketId.Value)).Status);
        Assert.Equal(OperationError.InvalidTicketState, (await Tickets(sp).ResumeTicketAsync(s.TicketId.Value)).Error);
        Assert.Equal(OperationError.NotFound, (await Tickets(sp).SuspendTicketAsync(int.MaxValue)).Error);

        var suspendRow = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.TicketSuspend));
        Assert.Equal("MonthlyTicket", suspendRow.EntityType);
        Assert.Equal(s.TicketId.Value.ToString(), suspendRow.EntityId);
        Assert.Equal("Khách đi công tác", AuditDb.Details(suspendRow).GetProperty("reason").GetString());
        AuditDb.HasKeys(suspendRow, "ticketCode", "reason");
        var resumeRow = Assert.Single(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.TicketResume));
        AuditDb.HasKeys(resumeRow, "ticketCode");

        // Suspend / resume never add purchases
        Assert.Single(await PurchasesAsync(s.TicketId.Value));
    }

    [Fact]
    public async Task Resume_over_an_overlapping_active_ticket_is_rejected()
    {
        _db.RequireAvailable();
        var (sp, _) = await ManagerAsync();
        using var spScope = sp;
        var today = TodayVn();
        var s = await SubscriberWithTicketAsync(VnMidnight(today.AddDays(-5)), VnMidnight(today.AddDays(25)));
        Assert.True((await Tickets(sp).SuspendTicketAsync(s.TicketId!.Value)).Success);
        var replacement = await Tickets(sp).CreateTicketAsync(new CreateTicketRequest(s.CustomerId, s.Plate, await _data.PlanIdAsync(Moto1Month)));
        Assert.True(replacement.Success, $"{replacement.Error}: {replacement.Message}");

        var resumed = await Tickets(sp).ResumeTicketAsync(s.TicketId.Value);

        Assert.Equal(OperationError.TicketOverlap, resumed.Error);
        Assert.Equal(MonthlyTicketStatus.Suspended, (await TicketAsync(s.TicketId.Value)).Status);
    }

    [Fact]
    public async Task M2_renew_after_the_vehicle_was_removed_from_the_customer_is_rejected()
    {
        _db.RequireAvailable();
        var (sp, _) = await ManagerAsync();
        using var spScope = sp;
        var today = TodayVn();
        var s = await SubscriberWithTicketAsync(VnMidnight(today.AddDays(-5)), VnMidnight(today.AddDays(25)));
        Assert.True((await Tickets(sp).SuspendTicketAsync(s.TicketId!.Value)).Success);
        var removed = await sp.GetRequiredService<ICustomerService>().RemoveVehicleAsync(s.CustomerVehicleId);
        Assert.True(removed.Success, removed.Message);
        await _data.SetTicketStatusAsync(s.TicketId.Value, MonthlyTicketStatus.Active); // bypass to reach the ownership check

        var result = await Tickets(sp).RenewTicketAsync(s.TicketId.Value, await _data.PlanIdAsync(Moto1Month));

        Assert.Equal(OperationError.PlateNotOwnedByCustomer, result.Error);
        Assert.Single(await PurchasesAsync(s.TicketId.Value));
    }

    [Fact]
    public async Task M2_resume_after_the_plate_moved_to_another_customer_is_rejected()
    {
        _db.RequireAvailable();
        var (sp, _) = await ManagerAsync();
        using var spScope = sp;
        var today = TodayVn();
        var s = await SubscriberWithTicketAsync(VnMidnight(today.AddDays(-5)), VnMidnight(today.AddDays(25)));
        Assert.True((await Tickets(sp).SuspendTicketAsync(s.TicketId!.Value)).Success);
        await _data.DeactivateVehicleAsync(s.CustomerVehicleId);
        var b = await _data.CreateCustomerAsync(isResident: false);
        await _data.AddVehicleAsync(b, s.Plate, await _data.MotorbikeTypeIdAsync());

        var result = await Tickets(sp).ResumeTicketAsync(s.TicketId.Value);

        Assert.Equal(OperationError.PlateNotOwnedByCustomer, result.Error);
        Assert.Equal(MonthlyTicketStatus.Suspended, (await TicketAsync(s.TicketId.Value)).Status);
    }

    [Fact]
    public async Task Resume_for_a_locked_customer_is_rejected()
    {
        _db.RequireAvailable();
        var (sp, _) = await ManagerAsync();
        using var spScope = sp;
        var today = TodayVn();
        var s = await SubscriberWithTicketAsync(VnMidnight(today.AddDays(-5)), VnMidnight(today.AddDays(25)), MonthlyTicketStatus.Suspended);
        await _db.ExecuteAsync("UPDATE \"Customers\" SET \"IsActive\" = false WHERE \"CustomerId\" = @c", ("c", s.CustomerId));

        Assert.Equal(OperationError.CustomerInactive, (await Tickets(sp).ResumeTicketAsync(s.TicketId!.Value)).Error);
    }

    [Fact]
    public async Task Active_plans_are_listed()
    {
        _db.RequireAvailable();
        var (sp, _) = await ManagerAsync();
        using var spScope = sp;

        var plans = await Tickets(sp).GetActivePlansAsync();

        Assert.Contains(plans, p => p.PlanName == Moto1Month);
        Assert.All(plans, p => Assert.True(p.IsActive));
    }
}
