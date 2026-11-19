using SmartPS.Models.Parking;
using SmartPS.Services.Common;

namespace SmartPS.Services.ParkingZones;

/// <summary>Khu đỗ xe kèm đối tượng được phép đỗ.</summary>
public sealed record ParkingZoneInfo(
    int ZoneId,
    string ZoneCode,
    string ZoneName,
    ZoneAudience Audience,
    int? VehicleTypeId,
    int TotalCapacity);

/// <summary>Tạo khu đỗ mới. VehicleTypeId = null nghĩa là khu dùng cho nhiều loại xe.</summary>
public sealed record CreateZoneRequest(
    string ZoneCode,
    string ZoneName,
    int? VehicleTypeId,
    ZoneAudience Audience,
    string? Description = null);

/// <summary>Thêm <paramref name="Count"/> ô liên tiếp vào khu với tiền tố mã ô, ví dụ "C-" → C-31..C-40.</summary>
public sealed record AddSlotsRequest(
    int ZoneId,
    int VehicleTypeId,
    string Prefix,
    int Count);

/// <summary>Kết quả thêm ô: các mã ô vừa tạo.</summary>
public sealed record AddSlotsResult(OperationResult Result, IReadOnlyList<string> SlotCodes)
{
    public bool Success => Result.Success;
}
