namespace SmartPS.ViewModels.Pricing;

public class PricingRuleItemViewModel : ViewModelBase
{
    public int RuleId { get; set; }
    public string VehicleTypeName { get; set; } = string.Empty;
    public string VehicleIcon { get; set; } = string.Empty;
    private decimal _block4hPrice;
    public decimal Block4hPrice
    {
        get => _block4hPrice;
        set => SetProperty(ref _block4hPrice, value);
    }

    private decimal _dailyPrice;
    public decimal DailyPrice
    {
        get => _dailyPrice;
        set => SetProperty(ref _dailyPrice, value);
    }

    private decimal _monthly1Price;
    public decimal Monthly1Price
    {
        get => _monthly1Price;
        set => SetProperty(ref _monthly1Price, value);
    }

    private decimal _monthly3Price;
    public decimal Monthly3Price
    {
        get => _monthly3Price;
        set => SetProperty(ref _monthly3Price, value);
    }

    private decimal _monthly6Price;
    public decimal Monthly6Price
    {
        get => _monthly6Price;
        set => SetProperty(ref _monthly6Price, value);
    }

    public string Description { get; set; } = string.Empty;
}
