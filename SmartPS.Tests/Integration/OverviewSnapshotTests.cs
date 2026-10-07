using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartPS.Models.Parking;
using SmartPS.Models.Shifts;

namespace SmartPS.Tests.Integration;

/// <summary>
/// AC-9 / R7 against PostgreSQL: Overview "today" is the VN day of the injected clock, counts are not capped,
/// total slots is the real ParkingSlots count (no 20 fallback), vehicle types come from the DB (no hard-coded 1/2),
/// net revenue comes from FinancialTransaction, and the recent list holds at most 15 sessions.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class OverviewSnapshotTests : IClassFixture<PostgresDatabaseFixture>
{
    private readonly PostgresDatabaseFixture _db;
    private readonly ReportSeed _seed;
    private readonly ResidentVisitorData _data;

    public OverviewSnapshotTests(PostgresDatabaseFixture db)
    {
        _db = db;
        _seed = new ReportSeed(db);
        _data = new ResidentVisitorData(db);
    }

    private static DateTime Utc(int y, int mo, int d, int h, int mi = 0, int s = 0) => new(y, mo, d, h, mi, s, DateTimeKind.Utc);

    private async Task<(ServiceProvider Sp, ReportService Service)> OperatorAsync(DateTime nowUtc)
    {
        var op = await TestUsers.CreateAsync(_db.Factory, "Operator");
        var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(op.Username);
        return (sp, new ReportService(sp.DbFactory(), sp.GetRequiredService<IAuthorizationGuard>(), new FixedTimeProvider(nowUtc)));
    }

    [Fact]
    public async Task AC9_today_is_the_VN_day_of_the_clock()
    {
        _db.RequireAvailable();
        // 2031-03-10T01:00Z = 08:00 VN on 10/03; VN day 10/03 starts at 2031-03-09T17:00Z
        var (sp, service) = await OperatorAsync(Utc(2031, 3, 10, 1));
        using var spScope = sp;
        await _seed.PaidSessionAsync(Utc(2031, 3, 9, 17, 30), Utc(2031, 3, 9, 18), 15_000m);              // today
        await _seed.PaidSessionAsync(Utc(2031, 3, 9, 16, 59), Utc(2031, 3, 9, 16, 59, 30), 20_000m);      // yesterday (23:59 VN)
        await _seed.FinancialAsync(FinancialTransactionType.Refund, PaymentMethod.Cash, -5_000m, Utc(2031, 3, 9, 20));
        await _seed.FinancialAsync(FinancialTransactionType.Incident, PaymentMethod.Cash, 50_000m, Utc(2031, 3, 9, 21));

        var snapshot = await service.GetOverviewSnapshotAsync();

        Assert.Equal(new DateOnly(2031, 3, 10), snapshot.TodayVn);
        Assert.Equal(1, snapshot.TodayCheckIns);
        Assert.Equal(1, snapshot.TodayCheckOuts);
        Assert.Equal(10_000m, snapshot.TodayNetRevenue);
    }

    [Fact]
    public async Task Today_counts_are_not_capped_at_100()
    {
        _db.RequireAvailable();
        var (sp, service) = await OperatorAsync(Utc(2031, 4, 10, 12));
        using var spScope = sp;
        await _seed.SessionsAsync(130, i => Utc(2031, 4, 10, 0).AddMinutes(i * 2), i => Utc(2031, 4, 10, 6).AddMinutes(i));

        var snapshot = await service.GetOverviewSnapshotAsync();

        Assert.Equal(130, snapshot.TodayCheckIns);
        Assert.Equal(130, snapshot.TodayCheckOuts);
    }

    [Fact]
    public async Task AC9_total_slots_is_the_real_slot_count_and_follows_inserts()
    {
        _db.RequireAvailable();
        var (sp, service) = await OperatorAsync(Utc(2031, 5, 10, 1));
        using var spScope = sp;

        var before = await service.GetOverviewSnapshotAsync();
        Assert.Equal(await _db.ScalarAsync<long>("SELECT count(*) FROM \"ParkingSlots\""), before.TotalSlots);
        Assert.Equal(await _db.ScalarAsync<long>("SELECT count(*) FROM \"ParkingSlots\" WHERE \"Status\" = 0"), before.AvailableSlots);
        Assert.Equal(await _db.ScalarAsync<long>("SELECT count(*) FROM \"ParkingSlots\" WHERE \"Status\" = 2"), before.MaintenanceSlots);
        Assert.NotEqual(20, before.TotalSlots);

        await _data.CreateZoneAsync(ZoneAudience.Mixed, await _seed.MotorbikeTypeIdAsync(), 1, SlotStatus.Maintenance);
        var after = await service.GetOverviewSnapshotAsync();

        Assert.Equal(before.TotalSlots + 1, after.TotalSlots);
        Assert.Equal(before.MaintenanceSlots + 1, after.MaintenanceSlots);
        Assert.Equal(before.AvailableSlots, after.AvailableSlots);
    }

    [Fact]
    public async Task Parked_counts_come_from_active_sessions_by_DB_vehicle_type_and_group()
    {
        _db.RequireAvailable();
        var (sp, service) = await OperatorAsync(Utc(2031, 6, 10, 1));
        using var spScope = sp;
        var car = await _seed.CarTypeIdAsync();
        await _seed.SessionAsync(Utc(2031, 6, 9, 20), customerType: CustomerType.Resident, isMonthlyPass: true);
        await _seed.SessionAsync(Utc(2031, 6, 9, 21), customerType: CustomerType.Regular, isMonthlyPass: true, vehicleTypeId: car);
        await _seed.SessionAsync(Utc(2031, 6, 9, 22), customerType: CustomerType.VIP);
        await _seed.SessionAsync(Utc(2031, 6, 9, 22), Utc(2031, 6, 9, 23)); // completed: not parked

        var snapshot = await service.GetOverviewSnapshotAsync();

        await using var db = _db.CreateContext();
        var active = await db.ParkingSessions.AsNoTracking().Where(s => s.Status == SessionStatus.Active)
            .Select(s => new { s.VehicleTypeId, s.CustomerType, s.IsMonthlyPass }).ToListAsync();
        var typeNames = await db.VehicleTypes.AsNoTracking().Select(v => v.TypeName).ToListAsync();

        Assert.Equal(active.Count, snapshot.OccupiedNow);
        Assert.Equal(active.Count(a => a.CustomerType == CustomerType.Resident), snapshot.ParkedResidents);
        Assert.Equal(active.Count(a => a.CustomerType != CustomerType.Resident && a.IsMonthlyPass), snapshot.ParkedMonthlyPass);
        Assert.Equal(active.Count(a => a.CustomerType != CustomerType.Resident && !a.IsMonthlyPass), snapshot.ParkedVisitors);
        Assert.Equal(snapshot.OccupiedNow, snapshot.ParkedResidents + snapshot.ParkedMonthlyPass + snapshot.ParkedVisitors);

        Assert.Equal(typeNames.OrderBy(n => n, StringComparer.Ordinal), snapshot.ParkedByVehicleType.Select(v => v.Name).OrderBy(n => n, StringComparer.Ordinal));
        foreach (var row in snapshot.ParkedByVehicleType)
        {
            Assert.Equal(active.Count(a => a.VehicleTypeId == row.VehicleTypeId), row.Count);
        }

        Assert.Equal(snapshot.OccupiedNow, snapshot.ParkedByVehicleType.Sum(v => v.Count));
        Assert.Equal(snapshot.OccupiedNow * 100.0 / snapshot.TotalSlots, snapshot.OccupancyPercent, 1);
    }

    [Fact]
    public async Task Recent_sessions_are_at_most_15_newest_first()
    {
        _db.RequireAvailable();
        var (sp, service) = await OperatorAsync(Utc(2031, 7, 10, 12));
        using var spScope = sp;
        await _seed.SessionsAsync(20, i => Utc(2031, 7, 10, 1).AddMinutes(i * 5), i => Utc(2031, 7, 10, 3).AddMinutes(i));

        var recent = await service.GetRecentSessionsAsync(15);
        var three = await service.GetRecentSessionsAsync(3);

        Assert.Equal(15, recent.Count);
        Assert.Equal(recent.OrderByDescending(s => s.CheckInTime).Select(s => s.SessionId), recent.Select(s => s.SessionId));
        Assert.Equal(3, three.Count);
        Assert.Equal(recent.Take(3).Select(s => s.SessionId), three.Select(s => s.SessionId));
    }

    [Fact]
    public async Task Overview_needs_no_report_permission()
    {
        _db.RequireAvailable();
        var role = await TestUsers.CreateRoleAsync(_db.Factory, TestUsers.UniqueName("parkonly"), Permissions.ParkingView);
        var user = await TestUsers.CreateAsync(_db.Factory, role.RoleName);
        using var sp = IntegrationServices.Create(_db);
        await sp.LoginAsync(user.Username);
        var service = new ReportService(sp.DbFactory(), sp.GetRequiredService<IAuthorizationGuard>(), new FixedTimeProvider(Utc(2031, 8, 1, 1)));

        var snapshot = await service.GetOverviewSnapshotAsync();
        var recent = await service.GetRecentSessionsAsync();

        Assert.True(snapshot.TotalSlots > 0);
        Assert.True(recent.Count <= 15);
    }
}
