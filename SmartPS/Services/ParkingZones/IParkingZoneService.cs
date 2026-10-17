using SmartPS.Models.Parking;
using SmartPS.Services.Common;

namespace SmartPS.Services.ParkingZones;

public interface IParkingZoneService
{
    /// <summary>Danh sách khu đỗ xe; trả về danh sách rỗng khi không truy cập được cơ sở dữ liệu.</summary>
    Task<IReadOnlyList<ParkingZoneInfo>> GetZonesAsync(CancellationToken cancellationToken = default);

    /// <summary>Đổi đối tượng của khu (cần Parking.Configure). Giá trị không đổi thì trả về thành công và không ghi nhật ký.</summary>
    Task<OperationResult> UpdateAudienceAsync(int zoneId, ZoneAudience audience, CancellationToken cancellationToken = default);
}
