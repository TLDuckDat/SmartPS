namespace SmartPS.Models.Parking;

public class PricingRule
{
    public int RuleId { get; set; }
    public int VehicleTypeId { get; set; }
    public VehicleType? VehicleType { get; set; }

    public decimal Block4hPrice { get; set; }
    public decimal DailyPrice { get; set; }
    public decimal Monthly1Price { get; set; }
    public decimal Monthly3Price { get; set; }
    public decimal Monthly6Price { get; set; }

    /// <summary>Số phút ân hạn: gửi không quá số phút này thì miễn phí.</summary>
    public int GracePeriodMinutes { get; set; } = 15;

    /// <summary>Phụ phí cố định mỗi lần khoảng gửi đi qua mốc 00:00 giờ Việt Nam.</summary>
    public decimal OvernightSurcharge { get; set; }

    /// <summary>Giá mỗi chu kỳ 24h của xe vượt định mức số xe mỗi hộ.</summary>
    public decimal OverQuotaDailyPrice { get; set; }

    public string? Description { get; set; }
}
