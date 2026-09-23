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
}

/// <summary>
/// Trừu tượng hóa chiến lược tính toán cước phí đỗ xe (Strategy Pattern).
/// Tuân thủ:
/// - Single Responsibility Principle (SRP): Tách riêng logic tính cước ra khỏi GateControlService
/// - Open/Closed Principle (OCP): Có thể mở rộng thêm biểu phí ngày lễ, tính theo block bậc thang mà không sửa đổi GateControlService
/// </summary>
public interface IParkingFeeCalculator
{
    ParkingFeeCalculationResult CalculateFee(ParkingSession session, PricingRule? pricingRule, DateTime checkOutTime);
}
