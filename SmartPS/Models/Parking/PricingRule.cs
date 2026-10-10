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
    public string? Description { get; set; }
}
