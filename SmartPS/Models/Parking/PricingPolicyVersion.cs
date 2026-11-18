using SmartPS.Models.Auth;

namespace SmartPS.Models.Parking;

/// <summary>
/// Bản chụp bảng giá của một loại xe, tạo mới mỗi lần lưu Bảng giá có thay đổi. Chỉ thêm, không sửa,
/// để phiên gửi xe đã tính phí luôn đối chiếu được giá đã dùng.
/// </summary>
public class PricingPolicyVersion
{
    public int VersionId { get; set; }

    public int VehicleTypeId { get; set; }
    public VehicleType? VehicleType { get; set; }

    /// <summary>Số thứ tự phiên bản theo từng loại xe (1, 2, ...).</summary>
    public int VersionNumber { get; set; }

    public decimal Block4hPrice { get; set; }
    public decimal DailyPrice { get; set; }
    public decimal Monthly1Price { get; set; }
    public decimal Monthly3Price { get; set; }
    public decimal Monthly6Price { get; set; }
    public int GracePeriodMinutes { get; set; } = 15;
    public decimal OvernightSurcharge { get; set; }
    public decimal OverQuotaDailyPrice { get; set; }
    public int BlockHours { get; set; } = 4;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
}
