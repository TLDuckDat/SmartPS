using Microsoft.EntityFrameworkCore;
using SmartPS.Data;
using SmartPS.Models.Parking;

namespace SmartPS.Services.GateControl;

/// <summary>Truy vấn phân loại xe trên DB (N2): chỉ dùng chỉ mục biển số, không tải toàn bộ vé tháng vào bộ nhớ.</summary>
public static class GateClassificationQueries
{
    public static Task<BlacklistMatch?> FindActiveBlacklistAsync(
        SmartPsDbContext db,
        string normalizedPlate,
        CancellationToken cancellationToken = default)
    {
        return db.BlacklistEntries
            .AsNoTracking()
            .Where(b => b.IsActive && b.LicensePlate == normalizedPlate)
            .Select(b => new BlacklistMatch(b.BlacklistEntryId, b.LicensePlate, b.Reason))
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Vé tháng đang hiệu lực của biển số, chỉ tính khi biển số là phương tiện đang hoạt động của chính khách sở hữu vé.
    /// </summary>
    public static async Task<IReadOnlyList<TicketCandidate>> FindTicketCandidatesAsync(
        SmartPsDbContext db,
        string normalizedPlate,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        return await db.MonthlyTickets
            .AsNoTracking()
            .Where(t => t.RegisteredLicensePlate == normalizedPlate
                        && t.Status == MonthlyTicketStatus.Active
                        && t.StartDate <= nowUtc
                        && t.EndDate > nowUtc
                        && db.CustomerVehicles.Any(v => v.CustomerId == t.CustomerId && v.LicensePlate == t.RegisteredLicensePlate && v.IsActive))
            .Select(t => new TicketCandidate(
                t.TicketId,
                t.TicketCode,
                t.CustomerId,
                t.Customer!.FullName,
                t.Customer.IsActive,
                t.Customer.IsResident,
                t.Customer.ApartmentCode,
                t.Customer.Building,
                t.VehicleTypeId,
                t.Status,
                t.StartDate,
                t.EndDate))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Các kỳ vé tháng đã thanh toán của biển số: chỉ vé đang Active, khách đang hoạt động và biển số là phương tiện đang hoạt động của khách đó.
    /// Vé cũ chưa có dòng mua nào dùng khoảng [StartDate, EndDate) của chính vé làm một kỳ.
    /// </summary>
    public static async Task<IReadOnlyList<CoveragePeriod>> GetCoveragePeriodsAsync(
        SmartPsDbContext db,
        string normalizedPlate,
        DateTime checkInUtc,
        CancellationToken cancellationToken = default)
    {
        var tickets = await db.MonthlyTickets
            .AsNoTracking()
            .Where(t => t.RegisteredLicensePlate == normalizedPlate
                        && t.Status == MonthlyTicketStatus.Active
                        && t.Customer!.IsActive
                        // Chủ biển số tại thời điểm xe vào: xe được gỡ/chuyển sau đó không làm đổi độ phủ
                        && db.CustomerVehicles.Any(v => v.CustomerId == t.CustomerId && v.LicensePlate == t.RegisteredLicensePlate
                                                        && v.CreatedAt <= checkInUtc && (v.RemovedAt == null || v.RemovedAt > checkInUtc)))
            .Select(t => new { t.TicketId, t.StartDate, t.EndDate })
            .ToListAsync(cancellationToken);
        if (tickets.Count == 0)
        {
            return Array.Empty<CoveragePeriod>();
        }

        var ids = tickets.Select(t => t.TicketId).ToList();
        var purchases = await db.MonthlyTicketPurchases
            .AsNoTracking()
            .Where(p => ids.Contains(p.TicketId))
            .Select(p => new { p.TicketId, p.PeriodStartUtc, p.PeriodEndUtc })
            .ToListAsync(cancellationToken);

        var periods = new List<CoveragePeriod>();
        foreach (var ticket in tickets)
        {
            var own = purchases.Where(p => p.TicketId == ticket.TicketId).ToList();
            if (own.Count == 0)
            {
                periods.Add(new CoveragePeriod(ticket.StartDate, ticket.EndDate));
                continue;
            }

            // Vé cũ chưa có dòng mua đầu tiên: phần từ ngày bắt đầu của vé đến kỳ mua sớm nhất vẫn được phủ
            var earliest = own.Min(p => p.PeriodStartUtc);
            if (ticket.StartDate < earliest)
            {
                periods.Add(new CoveragePeriod(ticket.StartDate, earliest));
            }

            periods.AddRange(own.Select(p => new CoveragePeriod(p.PeriodStartUtc, p.PeriodEndUtc)));
        }

        return periods;
    }

    public static async Task<MonthlyCoverage> GetMonthlyCoverageAsync(
        SmartPsDbContext db,
        string normalizedPlate,
        DateTime checkInUtc,
        CancellationToken cancellationToken = default)
    {
        var periods = await GetCoveragePeriodsAsync(db, normalizedPlate, checkInUtc, cancellationToken);
        return new MonthlyCoverage(MonthlyCoverageCalculator.ContiguousValidUntil(periods, checkInUtc));
    }

    public static async Task<VehicleClassification> ClassifyAsync(
        SmartPsDbContext db,
        string normalizedPlate,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        var blacklist = await FindActiveBlacklistAsync(db, normalizedPlate, cancellationToken);
        var tickets = await FindTicketCandidatesAsync(db, normalizedPlate, nowUtc, cancellationToken);
        return VehicleClassifier.Classify(normalizedPlate, blacklist, tickets, nowUtc);
    }
}
