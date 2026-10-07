using Microsoft.EntityFrameworkCore;
using SmartPS.Constants;
using SmartPS.Data;
using SmartPS.Models.Parking;
using SmartPS.Models.Reports;
using SmartPS.Models.Shifts;
using SmartPS.Services.Authorization;

namespace SmartPS.Services.Reports;

/// <summary>
/// Report queries. SQL filters and groups in PostgreSQL (no row cap); Vietnam-day bucketing and every metric rule run in
/// the pure <see cref="ReportAggregator"/> over the small aggregate sets.
/// </summary>
public sealed class ReportService : IReportService
{
    private const string EntityType = "Report";

    private readonly IDbContextFactory<SmartPsDbContext> _contextFactory;
    private readonly IAuthorizationGuard _guard;
    private readonly TimeProvider _timeProvider;

    public ReportService(IDbContextFactory<SmartPsDbContext> contextFactory, IAuthorizationGuard guard, TimeProvider? timeProvider = null)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ReportFilterOptions> GetFilterOptionsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var types = await LoadVehicleTypesAsync(db, cancellationToken);
        var zones = await db.ParkingZones.AsNoTracking()
            .OrderBy(z => z.ZoneCode).ThenBy(z => z.ZoneId)
            .Select(z => new LookupItem(z.ZoneId, z.ZoneName))
            .ToListAsync(cancellationToken);
        return new ReportFilterOptions(types, zones);
    }

    public async Task<ReportResult> GetReportAsync(ReportFilter filter, CancellationToken cancellationToken = default)
    {
        ValidateRange(filter);
        await _guard.DemandAsync(Permissions.ReportView, EntityType, filter.Range.Key, cancellationToken);

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var previousRange = ReportPeriodCalculator.PreviousOf(filter.Range);
        var (fromUtc, toUtc) = ReportPeriodCalculator.ToUtcRange(filter.Range);

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var capacity = await ScalarAsync(db, ReportSql.Capacity, fromUtc, toUtc, filter, cancellationToken);
        var current = await LoadPeriodAsync(db, filter, filter.Range, capacity, cancellationToken);
        var previous = await LoadPeriodAsync(db, filter, previousRange, capacity, cancellationToken);

        var activeNow = await ScalarAsync(db, ReportSql.ActiveNow, fromUtc, toUtc, filter, cancellationToken);
        var vehicleTypes = await LoadVehicleTypesAsync(db, cancellationToken);
        var checkOutsByType = await QueryAsync<TypeCheckOutRow>(db, ReportSql.CheckOutsByType, fromUtc, toUtc, filter, cancellationToken);
        var revenueByType = await QueryAsync<TypeAmountRow>(db, ReportSql.FinancialsByType, fromUtc, toUtc, filter, cancellationToken);
        var zoneSlots = await QueryAsync<ZoneSlotsRow>(db, ReportSql.SlotsPerZone, fromUtc, toUtc, filter, cancellationToken);
        var zoneActive = await QueryAsync<ZoneCountRow>(db, ReportSql.ActivePerZone, fromUtc, toUtc, filter, cancellationToken);
        var plates = await QueryAsync<TopPlateSqlRow>(db, ReportSql.TopPlates, fromUtc, toUtc, filter, cancellationToken);
        var shifts = await LoadShiftsAsync(db, fromUtc, toUtc, cancellationToken);

        var currentSummary = ReportAggregator.Summarize(current, nowUtc);
        var previousSummary = ReportAggregator.Summarize(previous, nowUtc);

        var activeByZone = zoneActive.ToDictionary(z => z.ZoneId, z => z.Count);
        var zones = zoneSlots
            .Select(z => new ZoneOccupancyRow(z.ZoneId, z.ZoneName, activeByZone.GetValueOrDefault(z.ZoneId), z.Count))
            .ToList();

        var topPlates = plates
            .Select((p, index) => new TopPlateRow(index + 1, p.LicensePlate, p.Visits, AsUtc(p.LastCheckIn)))
            .ToList();

        var byTypeRevenue = revenueByType.Select(r => new VehicleRevenue(r.VehicleTypeId, r.Amount)).ToList();
        var byTypeCheckOuts = checkOutsByType.Select(r => new VehicleCheckOutStat(r.VehicleTypeId, r.Count, r.DurationSeconds)).ToList();

        return new ReportResult(
            filter,
            previousRange,
            nowUtc,
            ReportAggregator.BuildKpis(currentSummary, previousSummary, activeNow),
            ReportAggregator.BuildDaily(current),
            ReportAggregator.BuildHourly(current),
            shifts,
            ReportAggregator.BuildVehicleTypes(vehicleTypes, current.GroupVehicleCounts, byTypeCheckOuts, byTypeRevenue),
            topPlates,
            zones,
            currentSummary.Groups);
    }

    public async Task<SessionDetailPage> GetSessionDetailsAsync(
        ReportFilter filter, int maxRows = ReportLimits.MaxExportSessionRows, CancellationToken cancellationToken = default)
    {
        ValidateRange(filter);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRows);
        await _guard.DemandAsync(Permissions.ReportExport, EntityType, filter.Range.Key, cancellationToken);

        var (fromUtc, toUtc) = ReportPeriodCalculator.ToUtcRange(filter.Range);
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = db.ParkingSessions.AsNoTracking()
            .Where(s => s.Status != SessionStatus.Cancelled && s.CheckInTime >= fromUtc && s.CheckInTime < toUtc);
        if (filter.VehicleTypeId is { } vehicleTypeId)
        {
            query = query.Where(s => s.VehicleTypeId == vehicleTypeId);
        }

        if (filter.ZoneId is { } zoneId)
        {
            query = query.Where(s => s.Slot != null && s.Slot.ZoneId == zoneId);
        }

        query = ReportCustomerGroupRules.Apply(query, filter.CustomerGroup);

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderBy(s => s.CheckInTime).ThenBy(s => s.SessionId)
            .Take(maxRows)
            .Select(s => new
            {
                s.SessionId,
                s.TicketCode,
                s.LicensePlate,
                VehicleTypeName = s.VehicleType != null ? s.VehicleType.TypeName : string.Empty,
                s.CustomerType,
                s.IsMonthlyPass,
                ZoneName = s.Slot != null && s.Slot.Zone != null ? s.Slot.Zone.ZoneName : null,
                SlotCode = s.Slot != null ? s.Slot.SlotCode : null,
                s.CheckInTime,
                s.CheckOutTime,
                s.Status,
                s.TotalFee,
                s.PaymentMethod
            })
            .ToListAsync(cancellationToken);

        var details = rows.Select(r => new SessionDetailRow(
                r.SessionId,
                r.TicketCode,
                r.LicensePlate,
                r.VehicleTypeName,
                ReportCustomerGroupRules.Classify(r.CustomerType, r.IsMonthlyPass),
                r.ZoneName,
                r.SlotCode,
                AsUtc(r.CheckInTime),
                r.CheckOutTime is null ? null : AsUtc(r.CheckOutTime.Value),
                r.CheckOutTime is null ? null : (r.CheckOutTime.Value - r.CheckInTime).TotalMinutes,
                r.Status,
                r.TotalFee,
                r.PaymentMethod))
            .ToList();

        return new SessionDetailPage(details, total, total > details.Count);
    }

    public async Task<OverviewSnapshot> GetOverviewSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var todayVn = ReportPeriodCalculator.TodayVn(_timeProvider.GetUtcNow().UtcDateTime);
        var (fromUtc, toUtc) = ReportPeriodCalculator.ToUtcRange(new ReportDateRange(todayVn, todayVn));

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var slotsByStatus = await db.ParkingSlots.AsNoTracking()
            .GroupBy(s => s.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var totalSlots = slotsByStatus.Sum(s => s.Count);
        var availableSlots = slotsByStatus.Where(s => s.Status == SlotStatus.Available).Sum(s => s.Count);
        var maintenanceSlots = slotsByStatus.Where(s => s.Status == SlotStatus.Maintenance).Sum(s => s.Count);

        var active = await db.ParkingSessions.AsNoTracking()
            .Where(s => s.Status == SessionStatus.Active)
            .Select(s => new { s.VehicleTypeId, s.CustomerType, s.IsMonthlyPass })
            .ToListAsync(cancellationToken);

        var checkIns = await db.ParkingSessions.AsNoTracking()
            .CountAsync(s => s.Status != SessionStatus.Cancelled && s.CheckInTime >= fromUtc && s.CheckInTime < toUtc, cancellationToken);
        var checkOuts = await db.ParkingSessions.AsNoTracking()
            .CountAsync(s => s.Status == SessionStatus.Completed && s.CheckOutTime >= fromUtc && s.CheckOutTime < toUtc, cancellationToken);
        var revenue = await db.FinancialTransactions.AsNoTracking()
            .Where(f => f.CreatedAt >= fromUtc && f.CreatedAt < toUtc
                        && (f.Type == FinancialTransactionType.ParkingFee
                            || f.Type == FinancialTransactionType.Refund
                            || f.Type == FinancialTransactionType.Adjustment))
            .SumAsync(f => (decimal?)f.Amount, cancellationToken) ?? 0m;

        var types = await LoadVehicleTypesAsync(db, cancellationToken);
        var parkedByType = types
            .Select(t => new VehicleTypeCount(t.Id, t.Name, active.Count(a => a.VehicleTypeId == t.Id)))
            .ToList();

        var residents = active.Count(a => ReportCustomerGroupRules.Classify(a.CustomerType, a.IsMonthlyPass) == ReportCustomerGroup.Resident);
        var monthly = active.Count(a => ReportCustomerGroupRules.Classify(a.CustomerType, a.IsMonthlyPass) == ReportCustomerGroup.MonthlyPass);
        var visitors = active.Count - residents - monthly;

        return new OverviewSnapshot(
            todayVn,
            totalSlots,
            availableSlots,
            maintenanceSlots,
            active.Count,
            totalSlots == 0 ? 0 : Math.Round(active.Count * 100.0 / totalSlots, 1),
            checkIns,
            checkOuts,
            revenue,
            parkedByType,
            residents,
            monthly,
            visitors);
    }

    public async Task<IReadOnlyList<ParkingSession>> GetRecentSessionsAsync(int count = 15, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.ParkingSessions.AsNoTracking()
            .Include(s => s.VehicleType)
            .Include(s => s.Slot)
            .Include(s => s.Customer)
            .OrderByDescending(s => s.CheckInTime).ThenByDescending(s => s.SessionId)
            .Take(count)
            .ToListAsync(cancellationToken);
    }

    private static void ValidateRange(ReportFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var validation = ReportPeriodCalculator.Validate(filter.Range.FromVn, filter.Range.ToVn);
        if (validation != ReportRangeValidation.Valid)
        {
            throw new ArgumentException($"Invalid report range: {validation}.", nameof(filter));
        }
    }

    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static Task<List<T>> QueryAsync<T>(
        SmartPsDbContext db, string sql, DateTime fromUtc, DateTime toUtc, ReportFilter filter, CancellationToken ct)
        => db.Database.SqlQueryRaw<T>(sql, ReportSqlParameters.Create(fromUtc, toUtc, filter)).ToListAsync(ct);

    private static async Task<int> ScalarAsync(
        SmartPsDbContext db, string sql, DateTime fromUtc, DateTime toUtc, ReportFilter filter, CancellationToken ct)
    {
        var rows = await QueryAsync<ScalarIntRow>(db, sql, fromUtc, toUtc, filter, ct);
        return rows.Count == 0 ? 0 : rows[0].Value;
    }

    private static async Task<List<LookupItem>> LoadVehicleTypesAsync(SmartPsDbContext db, CancellationToken ct)
        => await db.VehicleTypes.AsNoTracking()
            .OrderBy(v => v.VehicleTypeId)
            .Select(v => new LookupItem(v.VehicleTypeId, v.TypeName))
            .ToListAsync(ct);

    private static async Task<PeriodRawData> LoadPeriodAsync(
        SmartPsDbContext db, ReportFilter filter, ReportDateRange range, int capacity, CancellationToken ct)
    {
        var (fromUtc, toUtc) = ReportPeriodCalculator.ToUtcRange(range);

        var ins = await QueryAsync<CheckInHourRow>(db, ReportSql.CheckInsByHour, fromUtc, toUtc, filter, ct);
        var outs = await QueryAsync<CheckOutHourRow>(db, ReportSql.CheckOutsByHour, fromUtc, toUtc, filter, ct);
        var groups = await QueryAsync<TypeGroupRow>(db, ReportSql.CheckInsByTypeAndGroup, fromUtc, toUtc, filter, ct);
        var money = await QueryAsync<FinancialDayRow>(db, ReportSql.FinancialsByDay, fromUtc, toUtc, filter, ct);
        var initial = await ScalarAsync(db, ReportSql.InitialOccupancy, fromUtc, toUtc, filter, ct);
        var tickets = await MonthlyTicketRevenueQuery.ExecuteAsync(db, fromUtc, toUtc, filter, ct);

        var hours = new Dictionary<DateTime, HourBucket>();
        foreach (var row in ins)
        {
            var hour = AsUtc(row.HourUtc);
            hours[hour] = new HourBucket(hour, row.Count, 0, 0, 0);
        }

        foreach (var row in outs)
        {
            var hour = AsUtc(row.HourUtc);
            hours[hour] = hours.TryGetValue(hour, out var existing)
                ? existing with { CheckOuts = row.Count, FreeCheckOuts = row.FreeCount, CheckOutDurationSeconds = row.DurationSeconds }
                : new HourBucket(hour, 0, row.Count, row.FreeCount, row.DurationSeconds);
        }

        return new PeriodRawData(
            range,
            hours.Values.OrderBy(h => h.HourUtc).ToList(),
            money.Select(m => new FinancialDayBucket(m.DayVn, (FinancialTransactionType)m.Type, (PaymentMethod)m.Method, m.Amount)).ToList(),
            groups.Select(g => new GroupVehicleCount(g.VehicleTypeId, (ReportCustomerGroup)g.Group, g.Count)).ToList(),
            initial,
            capacity,
            tickets);
    }

    private static async Task<List<ShiftReportRow>> LoadShiftsAsync(SmartPsDbContext db, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        var rows = await db.Shifts.AsNoTracking()
            .Where(s => s.OpenedAt >= fromUtc && s.OpenedAt < toUtc)
            .OrderBy(s => s.OpenedAt).ThenBy(s => s.ShiftId)
            .Select(s => new
            {
                s.ShiftId,
                OpenedByName = s.OpenedByUser != null ? s.OpenedByUser.FullName : string.Empty,
                s.OpenedAt,
                s.ClosedAt,
                s.BeginningCash,
                s.ExpectedCash,
                s.ActualCash,
                s.Difference,
                s.Status
            })
            .ToListAsync(ct);

        return rows.Select(r => new ShiftReportRow(
                r.ShiftId,
                r.OpenedByName,
                AsUtc(r.OpenedAt),
                r.ClosedAt is null ? null : AsUtc(r.ClosedAt.Value),
                r.BeginningCash,
                r.ExpectedCash,
                r.ActualCash,
                r.Difference,
                r.Status))
            .ToList();
    }
}
