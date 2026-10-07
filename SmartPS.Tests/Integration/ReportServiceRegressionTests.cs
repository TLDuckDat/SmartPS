using Microsoft.Extensions.DependencyInjection;
using SmartPS.Models.Parking;

namespace SmartPS.Tests.Integration;

/// <summary>
/// Fix round 1 against PostgreSQL. K5 (challenge D8): out-of-bounds ranges are rejected with an ArgumentException before
/// the permission guard runs. K6 (challenge D2): the zone occupancy rows honour the vehicle-type filter (slot totals) and,
/// with a zone filter, list only that zone.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class ReportServiceRegressionTests : IClassFixture<PostgresDatabaseFixture>
{
    private static readonly DateTime ServiceNowUtc = new(2032, 1, 15, 0, 0, 0, DateTimeKind.Utc);

    private readonly PostgresDatabaseFixture _db;
    private readonly ReportSeed _seed;
    private readonly ResidentVisitorData _data;

    public ReportServiceRegressionTests(PostgresDatabaseFixture db)
    {
        _db = db;
        _seed = new ReportSeed(db);
        _data = new ResidentVisitorData(db);
    }

    private static DateTime Vn(int y, int mo, int d, int h, int mi = 0) => new DateTime(y, mo, d, h, mi, 0, DateTimeKind.Utc) - AuditTime.VietnamOffset;

    private static ReportFilter Filter(DateOnly from, DateOnly to, int? vehicleTypeId = null, int? zoneId = null)
        => new(new ReportDateRange(from, to), vehicleTypeId, ReportCustomerGroup.All, zoneId, ReportPeriodPreset.Custom);

    private async Task<(ServiceProvider Sp, ReportService Service)> LoggedInAsync(string? username = null)
    {
        var sp = IntegrationServices.Create(_db);
        if (username is null)
        {
            await sp.LoginAdminAsync();
        }
        else
        {
            await sp.LoginAsync(username);
        }

        return (sp, new ReportService(sp.DbFactory(), sp.GetRequiredService<IAuthorizationGuard>(), new FixedTimeProvider(ServiceNowUtc)));
    }

    public static TheoryData<DateOnly, DateOnly> OutOfBoundsRanges() => new()
    {
        { DateOnly.MinValue, DateOnly.MinValue },
        { new DateOnly(1, 1, 1), new DateOnly(1, 12, 31) },
        { new DateOnly(1999, 12, 31), new DateOnly(2000, 1, 1) },
        { DateOnly.MaxValue, DateOnly.MaxValue },
        { new DateOnly(2033, 2, 1), new DateOnly(2033, 2, 5) }, // ends more than 366 days after "now" (2032-01-15)
    };

    [Theory]
    [MemberData(nameof(OutOfBoundsRanges))]
    public async Task K5_out_of_bounds_range_is_an_ArgumentException_for_reports_and_session_details(DateOnly from, DateOnly to)
    {
        _db.RequireAvailable();
        var (sp, service) = await LoggedInAsync();
        using var spScope = sp;

        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.GetReportAsync(Filter(from, to)));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.GetSessionDetailsAsync(Filter(from, to)));
    }

    [Fact]
    public async Task K5_out_of_bounds_range_is_rejected_before_the_permission_guard()
    {
        _db.RequireAvailable();
        var role = await TestUsers.CreateRoleAsync(_db.Factory, TestUsers.UniqueName("noreport"), Permissions.ParkingView);
        var user = await TestUsers.CreateAsync(_db.Factory, role.RoleName);
        var (sp, service) = await LoggedInAsync(user.Username);
        using var spScope = sp;
        var idBefore = await AuditDb.MaxIdAsync(_db.Factory);

        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.GetReportAsync(Filter(DateOnly.MinValue, DateOnly.MinValue)));

        Assert.Empty(await AuditDb.RowsAfterAsync(_db.Factory, idBefore, AuditActions.AccessDenied));
    }

    [Fact]
    public async Task K5_range_ending_exactly_366_days_after_today_is_accepted()
    {
        _db.RequireAvailable();
        var (sp, service) = await LoggedInAsync();
        using var spScope = sp;
        var today = ReportPeriodCalculator.TodayVn(ServiceNowUtc);
        var limit = today.AddDays(366);

        var report = await service.GetReportAsync(Filter(limit, limit));

        Assert.Equal(0m, report.Kpis.CheckIns.Current);
    }

    [Fact]
    public async Task K6_zone_rows_count_only_slots_of_the_filtered_vehicle_type()
    {
        _db.RequireAvailable();
        var (sp, service) = await LoggedInAsync();
        using var spScope = sp;
        var isolated = await _data.CreateIsolatedVehicleTypeAsync();
        var moto = await _seed.MotorbikeTypeIdAsync();
        var isolatedZone = await _data.CreateZoneAsync(ZoneAudience.Mixed, isolated, 3);
        var motoZone = await _data.CreateZoneAsync(ZoneAudience.Mixed, moto, 2);
        await _seed.SessionAsync(Vn(2031, 3, 1, 8), vehicleTypeId: isolated, slotId: isolatedZone.SlotIds[0]);
        await _seed.SessionAsync(Vn(2031, 3, 1, 8), vehicleTypeId: moto, slotId: motoZone.SlotIds[0]);
        var day = new DateOnly(2031, 3, 1);

        var report = await service.GetReportAsync(Filter(day, day, vehicleTypeId: isolated));

        var row = Assert.Single(report.Zones, z => z.ZoneId == isolatedZone.ZoneId);
        Assert.Equal(3, row.TotalSlots);
        Assert.Equal(1, row.OccupiedSlots);
        // the motorbike-only zone has no slot of the filtered type: it is either omitted or shows 0 / 0
        var other = report.Zones.SingleOrDefault(z => z.ZoneId == motoZone.ZoneId);
        Assert.True(other is null || (other.TotalSlots == 0 && other.OccupiedSlots == 0),
            $"zone {motoZone.ZoneCode} shows {other?.OccupiedSlots}/{other?.TotalSlots} under the vehicle-type filter");
        foreach (var zone in report.Zones)
        {
            var expected = await _db.ScalarAsync<long>(
                "SELECT count(*) FROM \"ParkingSlots\" WHERE \"ZoneId\" = @z AND \"VehicleTypeId\" = @vt", ("z", zone.ZoneId), ("vt", isolated));
            Assert.Equal(expected, zone.TotalSlots);
            Assert.True(zone.OccupiedSlots <= zone.TotalSlots, $"zone {zone.ZoneName}: {zone.OccupiedSlots}/{zone.TotalSlots}");
        }

        Assert.True(ReportChartMapper.ZoneOccupancy(report).HasData);
    }

    [Fact]
    public async Task K6_zone_filter_lists_only_the_selected_zone()
    {
        _db.RequireAvailable();
        var (sp, service) = await LoggedInAsync();
        using var spScope = sp;
        var moto = await _seed.MotorbikeTypeIdAsync();
        var zone = await _data.CreateZoneAsync(ZoneAudience.Mixed, moto, 2);
        await _seed.SessionAsync(Vn(2031, 3, 5, 8), slotId: zone.SlotIds[1]);
        var day = new DateOnly(2031, 3, 5);

        var report = await service.GetReportAsync(Filter(day, day, zoneId: zone.ZoneId));

        var row = Assert.Single(report.Zones);
        Assert.Equal(zone.ZoneId, row.ZoneId);
        Assert.Equal(2, row.TotalSlots);
        Assert.Equal(1, row.OccupiedSlots);
        Assert.Equal(new[] { row.ZoneName }, ReportChartMapper.ZoneOccupancy(report).Labels);
    }

    [Fact]
    public async Task K6_without_filters_every_zone_shows_all_its_slots()
    {
        _db.RequireAvailable();
        var (sp, service) = await LoggedInAsync();
        using var spScope = sp;
        var day = new DateOnly(2031, 3, 9);

        var report = await service.GetReportAsync(Filter(day, day));

        Assert.Equal(await _db.ScalarAsync<long>("SELECT count(*) FROM \"ParkingZones\""), report.Zones.Count);
        foreach (var zone in report.Zones)
        {
            Assert.Equal(await _db.ScalarAsync<long>("SELECT count(*) FROM \"ParkingSlots\" WHERE \"ZoneId\" = @z", ("z", zone.ZoneId)), zone.TotalSlots);
        }
    }
}
