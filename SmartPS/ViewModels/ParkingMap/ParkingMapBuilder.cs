using SmartPS.Models.Parking;
using SmartPS.Services.ParkingZones;

namespace SmartPS.ViewModels.ParkingMap;

public sealed record SlotMapData(
    int SlotId,
    string SlotCode,
    int VehicleTypeId,
    string VehicleTypeName,
    SlotStatus EffectiveStatus,
    string? CurrentLicensePlate,
    DateTime? CheckInTimeLocal,
    string? TicketCode,
    string? CustomerName,
    CustomerType? SessionCustomerType,
    bool IsMonthlyPass);

public sealed record ZoneGroupData(
    int? ZoneId,
    string ZoneCode,
    string ZoneName,
    ZoneAudience Audience,
    IReadOnlyList<SlotMapData> Slots,
    int TotalCount,
    int AvailableCount,
    int OccupiedCount,
    int MaintenanceCount);

/// <summary>Gom ô đỗ theo khu (không dựa vào tiền tố mã ô) và tính trạng thái hiển thị của từng ô.</summary>
public static class ParkingMapBuilder
{
    public static IReadOnlyList<ZoneGroupData> BuildGroups(
        IEnumerable<ParkingSlot> slots,
        IEnumerable<ParkingSession> activeSessions,
        IEnumerable<ParkingZoneInfo> zones)
    {
        var slotList = slots.ToList();
        var sessions = activeSessions.Where(s => s.Status == SessionStatus.Active).OrderByDescending(s => s.CheckInTime).ToList();
        var zoneList = zones.OrderBy(z => z.ZoneCode, StringComparer.Ordinal).ToList();
        var knownZoneIds = zoneList.Select(z => z.ZoneId).ToHashSet();

        var groups = new List<ZoneGroupData>();
        foreach (var zone in zoneList)
        {
            var zoneSlots = slotList.Where(s => s.ZoneId == zone.ZoneId);
            groups.Add(Group(zone.ZoneId, zone.ZoneCode, zone.ZoneName, zone.Audience, zoneSlots, sessions));
        }

        // Ô chưa gán khu (hoặc khu không còn trong danh sách): gom theo tên khu cũ, đối tượng dùng chung
        var unzoned = slotList.Where(s => s.ZoneId is null || !knownZoneIds.Contains(s.ZoneId.Value));
        foreach (var byName in unzoned.GroupBy(s => s.ZoneName ?? string.Empty).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            groups.Add(Group(null, string.Empty, byName.Key, ZoneAudience.Mixed, byName, sessions));
        }

        return groups;
    }

    private static ZoneGroupData Group(
        int? zoneId,
        string zoneCode,
        string zoneName,
        ZoneAudience audience,
        IEnumerable<ParkingSlot> slots,
        IReadOnlyList<ParkingSession> sessions)
    {
        var mapped = slots
            .OrderBy(s => s.SlotCode, StringComparer.Ordinal)
            .ThenBy(s => s.SlotId)
            .Select(s => Map(s, sessions))
            .ToList();

        return new ZoneGroupData(
            zoneId,
            zoneCode,
            zoneName,
            audience,
            mapped,
            mapped.Count,
            mapped.Count(s => s.EffectiveStatus == SlotStatus.Available),
            mapped.Count(s => s.EffectiveStatus == SlotStatus.Occupied),
            mapped.Count(s => s.EffectiveStatus == SlotStatus.Maintenance));
    }

    private static SlotMapData Map(ParkingSlot slot, IReadOnlyList<ParkingSession> sessions)
    {
        var session = sessions.FirstOrDefault(s => s.SlotId == slot.SlotId)
                      ?? (string.IsNullOrEmpty(slot.CurrentLicensePlate)
                          ? null
                          : sessions.FirstOrDefault(s => string.Equals(s.LicensePlate, slot.CurrentLicensePlate, StringComparison.OrdinalIgnoreCase)));

        var status = session is not null || slot.Status == SlotStatus.Occupied
            ? SlotStatus.Occupied
            : slot.Status == SlotStatus.Maintenance ? SlotStatus.Maintenance : SlotStatus.Available;

        return new SlotMapData(
            slot.SlotId,
            slot.SlotCode,
            slot.VehicleTypeId,
            slot.VehicleType?.TypeName ?? string.Empty,
            status,
            slot.CurrentLicensePlate ?? session?.LicensePlate,
            session?.CheckInTimeLocal,
            session?.TicketCode,
            session?.Customer?.FullName,
            session?.CustomerType,
            session?.IsMonthlyPass ?? false);
    }
}
