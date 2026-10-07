using SmartPS.Constants;
using SmartPS.Models.Audit;
using SmartPS.Models.Parking;
using SmartPS.Services.Audit;

namespace SmartPS.Services.GateControl;

/// <summary>Các bản ghi nhật ký kiểm toán của luồng cổng liên quan đến danh sách đen.</summary>
public static class GateAuditEntries
{
    public static AuditEntry BlacklistBlocked(string licensePlate, string normalizedPlate, BlacklistMatch match, bool hadValidTicket)
    {
        ArgumentNullException.ThrowIfNull(match);
        return new AuditEntry(
            AuditActions.GateBlacklistBlocked,
            AuditOutcome.Denied,
            "BlacklistEntry",
            match.BlacklistEntryId.ToString(),
            new
            {
                LicensePlate = licensePlate,
                NormalizedPlate = normalizedPlate,
                Reason = match.Reason,
                HadValidTicket = hadValidTicket
            });
    }

    public static AuditEntry BlacklistExitWarning(ParkingSession session, BlacklistMatch match, AuditActor? actor = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(match);
        return new AuditEntry(
            AuditActions.GateBlacklistExitWarning,
            AuditOutcome.Success,
            "ParkingSession",
            session.SessionId.ToString(),
            new
            {
                LicensePlate = session.LicensePlate,
                Reason = match.Reason,
                BlacklistEntryId = match.BlacklistEntryId
            },
            actor);
    }
}
