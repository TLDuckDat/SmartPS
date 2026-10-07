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
