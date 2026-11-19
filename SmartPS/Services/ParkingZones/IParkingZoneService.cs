using SmartPS.Models.Parking;
using SmartPS.Services.Common;

namespace SmartPS.Services.ParkingZones;

public interface IParkingZoneService
{
    /// <summary>Danh sách khu đỗ xe; trả về danh sách rỗng khi không truy cập được cơ sở dữ liệu.</summary>
    Task<IReadOnlyList<ParkingZoneInfo>> GetZonesAsync(CancellationToken cancellationToken = default);

    /// <summary>Đổi đối tượng của khu (cần Parking.Configure). Giá trị không đổi thì trả về thành công và không ghi nhật ký.</summary>
    Task<OperationResult> UpdateAudienceAsync(int zoneId, ZoneAudience audience, CancellationToken cancellationToken = default);

    /// <summary>Tạo khu đỗ mới, chưa có ô (cần Parking.Configure). Ghi nhật ký ZONE_CREATE.</summary>
    Task<OperationResult> CreateZoneAsync(CreateZoneRequest request, CancellationToken cancellationToken = default);

    /// <summary>Thêm nhiều ô liên tiếp vào khu (cần Parking.Configure). Ghi nhật ký SLOT_CREATE.</summary>
    Task<AddSlotsResult> AddSlotsAsync(AddSlotsRequest request, CancellationToken cancellationToken = default);

    /// <summary>Chuyển ô sang Bảo trì hoặc trả về Trống; không áp dụng cho ô đang có xe. Ghi nhật ký SLOT_STATUS_UPDATE.</summary>
    Task<OperationResult> SetSlotMaintenanceAsync(int slotId, bool maintenance, string? reason, CancellationToken cancellationToken = default);

    /// <summary>Xoá ô trống chưa từng có lượt gửi (ô đã có lịch sử thì chỉ chuyển bảo trì). Ghi nhật ký SLOT_DELETE.</summary>
    Task<OperationResult> DeleteSlotAsync(int slotId, CancellationToken cancellationToken = default);
}
