using SmartPS.Models.Parking;

namespace SmartPS.Services.ParkingZones;

/// <summary>Khu đỗ xe kèm đối tượng được phép đỗ.</summary>
public sealed record ParkingZoneInfo(
    int ZoneId,
    string ZoneCode,
    string ZoneName,
    ZoneAudience Audience,
    int? VehicleTypeId,
    int TotalCapacity);
