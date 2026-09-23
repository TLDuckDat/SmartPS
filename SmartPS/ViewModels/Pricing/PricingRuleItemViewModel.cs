namespace SmartPS.ViewModels.Pricing;

public class PricingRuleItemViewModel : ViewModelBase
{
    public int RuleId { get; set; }
    public string VehicleTypeName { get; set; } = string.Empty;
    public string VehicleIcon { get; set; } = string.Empty;
    public int FirstBlockMinutes { get; set; } = 120;
    public decimal FirstBlockPrice { get; set; }
    public decimal AdditionalPricePerHour { get; set; }
    public decimal OvernightPrice { get; set; }
    public decimal MonthlyPassPrice { get; set; }
    public string Description { get; set; } = string.Empty;

    public string FirstBlockFormatted => $"{FirstBlockPrice:N0} đ / {FirstBlockMinutes / 60}h đầu";
    public string AdditionalFormatted => $"{AdditionalPricePerHour:N0} đ / giờ tiếp theo";
    public string OvernightFormatted => $"{OvernightPrice:N0} đ / đêm (23h-06h)";
    public string MonthlyPassFormatted => $"{MonthlyPassPrice:N0} đ / tháng";
}
