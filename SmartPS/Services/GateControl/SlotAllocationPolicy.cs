using SmartPS.Models.GateControl;
using SmartPS.Models.Parking;

namespace SmartPS.Services.GateControl;

public sealed record SlotCandidate(
    int SlotId,
    string SlotCode,
    int VehicleTypeId,
    SlotStatus Status,
    ZoneAudience Audience,
    int? ZoneId,
    string? ZoneCode,
    bool HasActiveSession = false);

/// <summary>Quy tắc cấp ô theo nhóm xe và đối tượng của khu (R12, R13).</summary>
public static class SlotAllocationPolicy
{
    private static readonly ZoneAudience[] ResidentOrder = { ZoneAudience.ResidentOnly, ZoneAudience.Mixed, ZoneAudience.VisitorOnly };
    private static readonly ZoneAudience[] VisitorOrder = { ZoneAudience.VisitorOnly, ZoneAudience.Mixed };

    public static IReadOnlyList<ZoneAudience> GetAudiencePreference(VehicleCategory category)
        => category switch
        {
            VehicleCategory.Resident => ResidentOrder,
            VehicleCategory.Visitor or VehicleCategory.MonthlyPass => VisitorOrder,
            _ => Array.Empty<ZoneAudience>()
        };

    public static bool IsAudienceAllowed(VehicleCategory category, ZoneAudience audience)
        => GetAudiencePreference(category).Contains(audience);

    public static SlotCandidate? PickSlot(IEnumerable<SlotCandidate> candidates, VehicleCategory category, int vehicleTypeId)
    {
        var preference = GetAudiencePreference(category);
        if (preference.Count == 0)
        {
            return null;
        }

        return candidates
            .Where(s => s.VehicleTypeId == vehicleTypeId
                        && s.Status == SlotStatus.Available
                        && !s.HasActiveSession
                        && IsAudienceAllowed(category, s.Audience))
            .OrderBy(s => IndexOf(preference, s.Audience))
            .ThenBy(s => s.SlotCode, StringComparer.Ordinal)
            .ThenBy(s => s.SlotId)
            .FirstOrDefault();
    }

    public static CheckInRejectReason ValidateRequestedSlot(SlotCandidate? slot, int vehicleTypeId, VehicleCategory category)
    {
        if (slot is null)
        {
            return CheckInRejectReason.SlotNotFound;
        }

        if (slot.VehicleTypeId != vehicleTypeId)
        {
            return CheckInRejectReason.SlotVehicleTypeMismatch;
        }

        if (!IsAudienceAllowed(category, slot.Audience))
        {
            return CheckInRejectReason.SlotAudienceNotAllowed;
        }

        if (slot.Status != SlotStatus.Available || slot.HasActiveSession)
        {
            return CheckInRejectReason.SlotNotAvailable;
        }

        return CheckInRejectReason.None;
    }

    private static int IndexOf(IReadOnlyList<ZoneAudience> preference, ZoneAudience audience)
    {
        for (var i = 0; i < preference.Count; i++)
        {
            if (preference[i] == audience)
            {
                return i;
            }
        }

        return int.MaxValue;
    }
}
