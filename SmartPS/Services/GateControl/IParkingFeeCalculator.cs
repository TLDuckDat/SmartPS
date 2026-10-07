using SmartPS.Models.Parking;

namespace SmartPS.Services.GateControl;

/// <summary>
/// Kết quả tính toán cước phí đỗ xe tách biệt logic nghiệp vụ
/// </summary>
public class ParkingFeeCalculationResult
{
    public TimeSpan Duration { get; set; }
    public decimal RawFee { get; set; }
    public double DiscountPercentage { get; set; }
    public decimal TotalFee { get; set; }
    public bool IsMonthlyTicket { get; set; }
    public string Message { get; set; } = string.Empty;

    // Chi tiết phần vé tháng (R15): lý do tính phí, thời điểm bắt đầu tính phí vãng lai và thời điểm vé hết hiệu lực
    public MonthlyChargeReason ChargeReason { get; set; }
    public DateTime? ChargeFromUtc { get; set; }
    public DateTime? TicketValidUntilUtc { get; set; }
}

/// <summary>
/// Trừu tượng hóa chiến lược tính toán cước phí đỗ xe (Strategy Pattern).
/// Tuân thủ:
/// - Single Responsibility Principle (SRP): Tách riêng logic tính cước ra khỏi GateControlService
/// - Open/Closed Principle (OCP): Có thể mở rộng thêm biểu phí ngày lễ, tính theo block bậc thang mà không sửa đổi GateControlService
/// </summary>
public interface IParkingFeeCalculator
{
    /// <summary>Tương đương gọi với coverage = null (không tra cứu được vé tháng): xe vé tháng được miễn phí.</summary>
    ParkingFeeCalculationResult CalculateFee(ParkingSession session, PricingRule? pricingRule, DateTime checkOutTime);

    /// <summary>Xe vé tháng chỉ miễn phí trong phần thời gian được vé bao phủ; phần sau khi hết hạn tính theo giá vãng lai.</summary>
    ParkingFeeCalculationResult CalculateFee(ParkingSession session, PricingRule? pricingRule, DateTime checkOutTime, MonthlyCoverage? coverage);
}
