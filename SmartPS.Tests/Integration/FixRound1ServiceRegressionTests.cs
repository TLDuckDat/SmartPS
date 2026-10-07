using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartPS.Models.GateControl;
using SmartPS.Models.Parking;
using SmartPS.Services.Audit;
using SmartPS.Services.Common;
using SmartPS.Services.Customers;
using SmartPS.Services.GateControl;
using SmartPS.Services.ParkingZones;

namespace SmartPS.Tests.Integration;

/// <summary>
/// Fix round 1 regression tests for the services (G5, G6, G8, G12, G13), ported from the challenge repros
/// (Attacks.E3, F2; Attacks2.H2) and review findings F2, F3, F5.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class FixRound1ServiceRegressionTests : IClassFixture<PostgresDatabaseFixture>
{
    private readonly PostgresDatabaseFixture _db;
    private readonly ResidentVisitorData _data;

    public FixRound1ServiceRegressionTests(PostgresDatabaseFixture db)
    {
        _db = db;
        _data = new ResidentVisitorData(db);
    }

    private async Task<(ServiceProvider Sp, int UserId)> LoginAsync(string roleName, Action<IServiceCollection>? overrides = null)
    {
        var user = await TestUsers.CreateAsync(_db.Factory, roleName);
        var sp = IntegrationServices.Create(_db, overrides);
        await sp.LoginAsync(user.Username);
        return (sp, user.UserId);
    }

    private static DateTime VnMidnight(DateOnly date) => AuditTime.VietnamDateStartUtc(date);

    private Task<string?> TicketCodeAsync(int ticketId)
        => PostgresDatabaseFixture.ScalarAsync<string>(_db.ConnectionString, "SELECT \"TicketCode\" FROM \"MonthlyTickets\" WHERE \"TicketId\" = @id", ("id", ticketId));

    // ---------------------------------------------------------------- G5 (CH1-04, Attacks.E3) ----------------------

    [Fact]
    public async Task G5_renewals_stay_anchored_to_the_original_day_of_month()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var planId = await _data.CreateIsolatedPlanAsync(vt, durationMonths: 1);
        var customerId = await _data.CreateCustomerAsync(isResident: true);
        var plate = ResidentVisitorData.UniqueNormalizedPlate();
        await _data.AddVehicleAsync(customerId, plate, vt);
        var anchor = new DateOnly(TicketDates.TodayVn(DateTime.UtcNow).Year + 1, 1, 31); // future ticket starting on Jan 31
        var (sp, _) = await LoginAsync("Manager");
        using var scope = sp;
        var tickets = sp.GetRequiredService<IMonthlyTicketService>();

        var created = await tickets.CreateTicketAsync(new CreateTicketRequest(customerId, plate, planId, anchor));
        Assert.True(created.Success, $"{created.Error}: {created.Message}");
        Assert.Equal(VnMidnight(anchor.AddMonths(1)), await _data.TicketEndAsync(created.Value));   // Feb 28/29

        Assert.True((await tickets.RenewTicketAsync(created.Value, planId)).Success);
        Assert.Equal(VnMidnight(anchor.AddMonths(2)), await _data.TicketEndAsync(created.Value));   // Mar 31, not Mar 28

        Assert.True((await tickets.RenewTicketAsync(created.Value, planId)).Success);
        Assert.Equal(VnMidnight(anchor.AddMonths(3)), await _data.TicketEndAsync(created.Value));   // Apr 30

        // Purchase periods chain without gaps
        await using var ctx = _db.CreateContext();
        var purchases = await ctx.MonthlyTicketPurchases.AsNoTracking().Where(p => p.TicketId == created.Value)
            .OrderBy(p => p.PeriodStartUtc).ToListAsync();
        Assert.Equal(3, purchases.Count);
        Assert.Equal(purchases[0].PeriodEndUtc, purchases[1].PeriodStartUtc);
        Assert.Equal(purchases[1].PeriodEndUtc, purchases[2].PeriodStartUtc);
        Assert.Equal(VnMidnight(anchor.AddMonths(3)), purchases[2].PeriodEndUtc);
    }

    // ---------------------------------------------------------------- G6 (CH1-05, Attacks2.H2) ---------------------

    [Fact]
    public async Task G6_ticket_code_sequence_uses_the_numeric_maximum()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var planId = await _data.CreateIsolatedPlanAsync(vt);
        var prefix = $"MT-{TicketDates.TodayVn(DateTime.UtcNow):yyyyMM}-";
        var customerId = await _data.CreateCustomerAsync(isResident: true);
        var p1 = ResidentVisitorData.UniqueNormalizedPlate();
        var p2 = ResidentVisitorData.UniqueNormalizedPlate();
        await _data.AddVehicleAsync(customerId, p1, vt);
        await _data.AddVehicleAsync(customerId, p2, vt);
        await _db.ExecuteAsync(
            "INSERT INTO \"MonthlyTickets\" (\"TicketCode\",\"CustomerId\",\"RegisteredLicensePlate\",\"VehicleTypeId\",\"StartDate\",\"EndDate\",\"MonthlyPrice\",\"Status\",\"CreatedAt\") " +
            "VALUES (@c1,@c,'X9999A',@vt,now()-interval '400 day',now()-interval '370 day',1,0,now()),(@c2,@c,'X10000A',@vt,now()-interval '400 day',now()-interval '370 day',1,0,now())",
            ("c1", prefix + "9999"), ("c2", prefix + "10000"), ("c", customerId), ("vt", vt));
        var (sp, _) = await LoginAsync("Manager");
        using var scope = sp;
        var tickets = sp.GetRequiredService<IMonthlyTicketService>();

        var t1 = await tickets.CreateTicketAsync(new CreateTicketRequest(customerId, p1, planId));
        var t2 = await tickets.CreateTicketAsync(new CreateTicketRequest(customerId, p2, planId));

        Assert.True(t1.Success, $"{t1.Error}: {t1.Message}");
        Assert.True(t2.Success, $"{t2.Error}: {t2.Message}");
        Assert.Equal(prefix + "10001", await TicketCodeAsync(t1.Value));
        Assert.Equal(prefix + "10002", await TicketCodeAsync(t2.Value));
    }

    [Fact]
    public async Task G6_parallel_ticket_creation_gets_distinct_codes()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var planId = await _data.CreateIsolatedPlanAsync(vt);
        var customerId = await _data.CreateCustomerAsync(isResident: true);
        var plates = Enumerable.Range(0, 4).Select(_ => ResidentVisitorData.UniqueNormalizedPlate()).ToList();
        foreach (var plate in plates)
        {
            await _data.AddVehicleAsync(customerId, plate, vt);
        }

        var managers = new List<ServiceProvider>();
        for (var i = 0; i < plates.Count; i++)
        {
            managers.Add((await LoginAsync("Manager")).Sp);
        }

        try
        {
            using var start = new ManualResetEventSlim(false);
            var tasks = plates.Select((plate, i) => Task.Run(async () =>
            {
                start.Wait();
                return await managers[i].GetRequiredService<IMonthlyTicketService>().CreateTicketAsync(new CreateTicketRequest(customerId, plate, planId));
            })).ToArray();
            start.Set();
            var results = await Task.WhenAll(tasks);

            Assert.All(results, r => Assert.True(r.Success, $"{r.Error}: {r.Message}"));
            var codes = new List<string?>();
            foreach (var r in results)
            {
                codes.Add(await TicketCodeAsync(r.Value));
            }

            Assert.Equal(codes.Count, codes.Distinct().Count());
        }
        finally
        {
            foreach (var m in managers)
            {
                m.Dispose();
            }
        }
    }

    // ---------------------------------------------------------------- G8 (CH1-07, review F3, Attacks.F2) -----------

    [Fact]
    public async Task G8_phone_and_id_numbers_in_free_text_are_masked_in_every_audit_field()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 2);
        var planId = await _data.CreateIsolatedPlanAsync(vt);
        var (sp, userId) = await LoginAsync("Manager");
        using var scope = sp;
        await ParkingFlows.OpenShiftAsync(sp, userId);
        var customers = sp.GetRequiredService<ICustomerService>();
        var tickets = sp.GetRequiredService<IMonthlyTicketService>();
        var blacklist = sp.GetRequiredService<IBlacklistService>();
        var gate = sp.GetRequiredService<IGateControlService>();
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        // fullName with a spaced phone and an ID number
        var plate = ResidentVisitorData.UniqueNormalizedPlate();
        var created = await customers.CreateCustomerAsync(new CustomerUpsertRequest
        {
            FullName = "Nguyen Van A 0988 123 456 CMND 012345678901",
            PhoneNumber = ResidentVisitorData.UniquePhone(),
            IsResident = true,
            ApartmentCode = "A-1205",
            Vehicles = new[] { new NewVehicle(plate, vt) }
        });
        Assert.True(created.Success, $"{created.Error}: {created.Message}");
        var updated = await customers.UpdateCustomerAsync(created.Value, new CustomerUpsertRequest
        {
            FullName = "Nguyen Van A 0912.345.678",
            PhoneNumber = ResidentVisitorData.UniquePhone(),
            IsResident = true,
            ApartmentCode = "A-1205"
        });
        Assert.True(updated.Success, $"{updated.Error}: {updated.Message}");

        // suspend reason
        var ticket = await tickets.CreateTicketAsync(new CreateTicketRequest(created.Value, plate, planId));
        Assert.True(ticket.Success, $"{ticket.Error}: {ticket.Message}");
        Assert.True((await tickets.SuspendTicketAsync(ticket.Value, "Khách gọi 0903-456-789 xin tạm dừng")).Success);

        // blacklist reason / remove reason
        var black = await blacklist.AddAsync(ResidentVisitorData.UniqueNormalizedPlate(), "Chủ xe Trần B, SĐT 0905123456, CCCD 079123456789 nợ phí");
        Assert.True(black.Success, $"{black.Error}: {black.Message}");
        Assert.True((await blacklist.RemoveAsync(black.Value, "Đã gọi 0977 888 999 xác nhận")).Success);

        // gate: blocked check-in and exit warning carry the blacklist reason
        var blockedPlate = ResidentVisitorData.UniqueNormalizedPlate();
        await _data.AddBlacklistAsync(blockedPlate, "Báo mất, liên hệ 0966.111.222");
        var blocked = await gate.ProcessCheckInAsync(new GateCheckInRequest { LicensePlate = blockedPlate, VehicleTypeId = vt });
        Assert.True(blocked.IsBlacklisted);
        var exitPlate = ResidentVisitorData.UniqueNormalizedPlate();
        var exitCheckIn = await gate.ProcessCheckInAsync(new GateCheckInRequest { LicensePlate = exitPlate, VehicleTypeId = vt });
        Assert.True(exitCheckIn.Success, exitCheckIn.Message);
        await _data.AddBlacklistAsync(exitPlate, "Nợ cũ, gọi 0933 444 555");
        var (_, exit) = await ParkingFlows.CheckOutCashAsync(sp, exitPlate, userId);
        Assert.True(exit.Success, exit.Message);

        var rows = await AuditDb.RowsAfterAsync(_db.Factory, idBefore);
        foreach (var action in new[]
                 {
                     AuditActions.CustomerCreate, AuditActions.CustomerUpdate, AuditActions.TicketSuspend, AuditActions.BlacklistAdd,
                     AuditActions.BlacklistRemove, AuditActions.GateBlacklistBlocked, AuditActions.GateBlacklistExitWarning
                 })
        {
            Assert.Contains(rows, r => r.Action == action);
        }

        var secrets = new[]
        {
            "0988 123 456", "012345678901", "0912.345.678", "0903-456-789", "0905123456", "079123456789",
            "0977 888 999", "0966.111.222", "0933 444 555", "0988 123", "0912.345", "0903-456", "0977 888", "0966.111", "0933 444"
        };
        foreach (var row in rows)
        {
            foreach (var secret in secrets)
            {
                Assert.False(row.Details.Contains(secret, StringComparison.Ordinal), $"{row.Action} leaks '{secret}': {row.Details}");
            }
        }

        // The non-numeric context is kept
        Assert.Contains(rows, r => r.Action == AuditActions.BlacklistAdd && r.Details.Contains("nợ phí", StringComparison.Ordinal));
        Assert.Contains(rows, r => r.Action == AuditActions.CustomerCreate && r.Details.Contains("Nguyen Van A", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- G12 (review F2) ------------------------------

    [Fact]
    public async Task G12_undefined_audience_value_is_a_validation_error()
    {
        _db.RequireAvailable();
        var vt = await _data.CreateIsolatedVehicleTypeAsync();
        var zone = await _data.CreateZoneAsync(ZoneAudience.Mixed, vt, 1);
        var (sp, _) = await LoginAsync("Manager");
        using var scope = sp;
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var result = await sp.GetRequiredService<IParkingZoneService>().UpdateAudienceAsync(zone.ZoneId, (ZoneAudience)99);

        Assert.False(result.Success);
        Assert.Equal(OperationError.Validation, result.Error);
        Assert.Equal(0, await _data.CountAsync("SELECT \"Audience\" FROM \"ParkingZones\" WHERE \"ZoneId\" = @z", ("z", zone.ZoneId)));
        Assert.Equal(idBefore, await AuditDb.MaxIdAsync(_db.Factory));
    }

    // ---------------------------------------------------------------- G13 (review F5) ------------------------------

    [Fact]
    public async Task G13_invalid_customer_update_is_rejected_before_the_audited_transaction()
    {
        _db.RequireAvailable();
        var customerId = await _data.CreateCustomerAsync(isResident: false);
        var (sp, _) = await LoginAsync("Manager", IntegrationServices.HookBeforeBegin(() => { }));
        using var scope = sp;
        var hook = sp.GetRequiredService<HookAuditServiceDecorator>();
        var customers = sp.GetRequiredService<ICustomerService>();
        var beginsBefore = hook.BeginCalls;
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        var badPhone = await customers.UpdateCustomerAsync(customerId, new CustomerUpsertRequest { FullName = "Sai SĐT", PhoneNumber = "012345678" });
        var noApartment = await customers.UpdateCustomerAsync(customerId, new CustomerUpsertRequest
        {
            FullName = "Cư dân thiếu căn hộ",
            PhoneNumber = ResidentVisitorData.UniquePhone(),
            IsResident = true
        });
        var badCreate = await customers.CreateCustomerAsync(new CustomerUpsertRequest { FullName = "", PhoneNumber = ResidentVisitorData.UniquePhone() });

        Assert.Equal(OperationError.Validation, badPhone.Error);
        Assert.Contains(CustomerValidationError.PhoneInvalid, badPhone.ValidationErrors);
        Assert.Equal(OperationError.Validation, noApartment.Error);
        Assert.Contains(CustomerValidationError.ApartmentRequired, noApartment.ValidationErrors);
        Assert.Equal(OperationError.Validation, badCreate.Error);
        Assert.Equal(beginsBefore, hook.BeginCalls);
        Assert.Equal(idBefore, await AuditDb.MaxIdAsync(_db.Factory));
    }
}
