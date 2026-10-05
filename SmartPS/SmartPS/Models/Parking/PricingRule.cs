namespace SmartPS.Models.Parking;

public class PricingRule
{
    public int RuleId { get; set; }
    public int VehicleTypeId { get; set; }
    public VehicleType? VehicleType { get; set; }

    public int FirstBlockMinutes { get; set; } = 120; // 2 giờ đầu
    public decimal FirstBlockPrice { get; set; }     // Giá 2 giờ đầu
    public decimal AdditionalPricePerHour { get; set; } // Mỗi giờ tiếp theo
    public decimal OvernightPrice { get; set; }      // Phụ thu qua đêm (từ 23h - 6h)
    public string? Description { get; set; }
}
