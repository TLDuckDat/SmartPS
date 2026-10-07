using SmartPS.Models.GateControl;
using SmartPS.Models.Parking;

namespace SmartPS.Services.GateControl;

public enum ClassificationWarning
{
    None = 0,
    CustomerLocked = 1
}

public sealed record BlacklistMatch(int BlacklistEntryId, string LicensePlate, string Reason);

public sealed record TicketCandidate(
    int TicketId,
    string TicketCode,
    int CustomerId,
    string CustomerName,
    bool CustomerIsActive,
    bool IsResident,
    string? ApartmentCode,
    string? Building,
    int VehicleTypeId,
    MonthlyTicketStatus Status,
    DateTime StartDateUtc,
    DateTime EndDateUtc);

public sealed record VehicleClassification(
    VehicleCategory Category,
    string NormalizedPlate,
    BlacklistMatch? Blacklist,
    TicketCandidate? Ticket,
    ClassificationWarning Warning)
{
    public bool IsBlacklisted => Category == VehicleCategory.Blacklisted;

    public bool IsMonthlyPass => Category is VehicleCategory.MonthlyPass or VehicleCategory.Resident;

    public static VehicleClassification Visitor(string normalizedPlate)
        => new(VehicleCategory.Visitor, normalizedPlate, null, null, ClassificationWarning.None);
}

/// <summary>Phân loại xe vào bãi (R10): danh sách đen, cư dân, vé tháng, vãng lai.</summary>
public static class VehicleClassifier
{
    /// <summary>Vé hiệu lực theo khoảng nửa mở [StartDate, EndDate).</summary>
    public static bool IsTicketValidAt(TicketCandidate ticket, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        return ticket.Status == MonthlyTicketStatus.Active
               && ticket.StartDateUtc <= nowUtc
               && nowUtc < ticket.EndDateUtc;
    }

    public static VehicleClassification Classify(
        string normalizedPlate,
        BlacklistMatch? blacklist,
        IEnumerable<TicketCandidate> tickets,
        DateTime nowUtc)
    {
        var valid = tickets
            .Where(t => IsTicketValidAt(t, nowUtc))
            .OrderByDescending(t => t.EndDateUtc)
            .ThenByDescending(t => t.TicketId)
            .ToList();
        var best = valid.FirstOrDefault(t => t.CustomerIsActive);

        if (blacklist is not null)
        {
            // Danh sách đen luôn thắng vé tháng; giữ vé hợp lệ tốt nhất để ghi nhận hadValidTicket.
            return new VehicleClassification(VehicleCategory.Blacklisted, normalizedPlate, blacklist,
                best ?? valid.FirstOrDefault(), ClassificationWarning.None);
        }

        if (best is not null)
        {
            var category = best.IsResident ? VehicleCategory.Resident : VehicleCategory.MonthlyPass;
            return new VehicleClassification(category, normalizedPlate, null, best, ClassificationWarning.None);
        }

        // Khách bị khoá nhưng vé còn hạn: coi là khách vãng lai kèm cảnh báo (E3).
        var warning = valid.Count > 0 ? ClassificationWarning.CustomerLocked : ClassificationWarning.None;
        return new VehicleClassification(VehicleCategory.Visitor, normalizedPlate, null, null, warning);
    }
}
