using SmartPS.Models.Parking;
using SmartPS.Services.Pricing;

namespace SmartPS.Services.GateControl;

/// <summary>
/// Kết quả tính toán cước phí đỗ xe tách biệt logic nghiệp vụ
/// </summary>
public class ParkingFeeCalculationResult
{
    public TimeSpan Duration { get; set; }
    public decimal RawFee { get; set; }
    public decimal TotalFee { get; set; }
    public bool IsMonthlyTicket { get; set; }
    public string Message { get; set; } = string.Empty;
    public string FeeDetails { get; set; } = string.Empty;

    /// <summary>Bản chụp bảng giá đã dùng (null khi bảng giá không đến từ CSDL).</summary>
    public int? PolicyVersionId { get; set; }

    /// <summary>Chi tiết cước dạng JSON để lưu cùng phiên gửi xe khi xe ra.</summary>
    public string? FeeBreakdownJson { get; set; }
}

/// <summary>
/// Trừu tượng hóa chiến lược tính toán cước phí đỗ xe (Strategy Pattern).
/// Tuân thủ:
/// - Single Responsibility Principle (SRP): Tách riêng logic tính cước ra khỏi GateControlService
/// - Open/Closed Principle (OCP): Có thể mở rộng thêm biểu phí ngày lễ, tính theo block bậc thang mà không sửa đổi GateControlService
/// </summary>
public interface IParkingFeeCalculator
{
    /// <summary>
    /// Tính cước theo bảng giá hiện hành của loại xe; vé tháng suy ra từ <see cref="ParkingSession.IsMonthlyPass"/>
    /// (miễn phí toàn bộ lượt), không xét xe vượt định mức.
    /// </summary>
    ParkingFeeCalculationResult CalculateFee(ParkingSession session, PricingRule? pricingRule, DateTime checkOutTime);

    /// <summary>Tính cước đầy đủ: bảng giá chụp, thông tin vé tháng (có hạn) và cờ xe vượt định mức.</summary>
    /// <remarks>
    /// Cài đặt mặc định cho bộ tính cước cũ chỉ biết bản ghi PricingRule: bỏ qua hạn vé tháng và cờ vượt định mức.
    /// </remarks>
    ParkingFeeCalculationResult CalculateFee(ParkingSession session, PricingPolicy policy, DateTime checkOutTime,
        MonthlyPassInfo? monthlyPass, bool isOverQuota)
        => CalculateFee(session, new PricingRule
        {
            VehicleTypeId = policy.VehicleTypeId,
            Block4hPrice = policy.Block4hPrice,
            DailyPrice = policy.DailyPrice,
            GracePeriodMinutes = policy.GracePeriodMinutes,
            OvernightSurcharge = policy.OvernightSurcharge,
            OverQuotaDailyPrice = policy.OverQuotaDailyPrice
        }, checkOutTime);
}
