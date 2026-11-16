// Bộ lọc lịch sử lượt xe theo người xử lý và khoảng thời gian nghiệp vụ.

using SmartPS.Models.Parking;

namespace SmartPS.DTOs.Parking;

public sealed class ParkingSessionHistoryFilterRequest
{
    public int UserId { get; init; }
    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }
    public SessionStatus? Status { get; init; }
    public int Limit { get; init; } = 500;
}
