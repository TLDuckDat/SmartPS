using SmartPS.Services.Localization;

namespace SmartPS.ViewModels.Pricing;

public class PricingRuleItemViewModel : ValidatableViewModelBase
{
    public int RuleId { get; set; }

    private string _vehicleTypeName = string.Empty;
    public string VehicleTypeName
    {
        get => _vehicleTypeName;
        set
        {
            if (SetProperty(ref _vehicleTypeName, value))
            {
                ValidateAll();
            }
        }
    }

    public string VehicleIcon { get; set; } = string.Empty;

    private decimal _block4hPrice;
    public decimal Block4hPrice
    {
        get => _block4hPrice;
        set
        {
            if (SetProperty(ref _block4hPrice, value))
            {
                ValidatePrice(nameof(Block4hPrice), _block4hPrice, requiresPositive: true);
            }
        }
    }

    private decimal _dailyPrice;
    public decimal DailyPrice
    {
        get => _dailyPrice;
        set
        {
            if (SetProperty(ref _dailyPrice, value))
            {
                ValidatePrice(nameof(DailyPrice), _dailyPrice, requiresPositive: true);
            }
        }
    }

    private decimal _monthly1Price;
    public decimal Monthly1Price
    {
        get => _monthly1Price;
        set
        {
            if (SetProperty(ref _monthly1Price, value))
            {
                ValidatePrice(nameof(Monthly1Price), _monthly1Price, requiresPositive: true);
            }
        }
    }

    private decimal _monthly3Price;
    public decimal Monthly3Price
    {
        get => _monthly3Price;
        set
        {
            if (SetProperty(ref _monthly3Price, value))
            {
                ValidatePrice(nameof(Monthly3Price), _monthly3Price, requiresPositive: false);
            }
        }
    }

    private decimal _monthly6Price;
    public decimal Monthly6Price
    {
        get => _monthly6Price;
        set
        {
            if (SetProperty(ref _monthly6Price, value))
            {
                ValidatePrice(nameof(Monthly6Price), _monthly6Price, requiresPositive: false);
            }
        }
    }

    public string Description { get; set; } = string.Empty;

    /// <summary>Xe máy và ô tô: giá lượt (4 giờ, ngày) và vé tháng 1 tháng phải lớn hơn 0.</summary>
    public bool RequiresPositivePrices
        => VehicleTypeName.Contains("máy", StringComparison.OrdinalIgnoreCase)
           || VehicleTypeName.Contains("ô tô", StringComparison.OrdinalIgnoreCase);

    /// <summary>Kiểm tra lại mọi ô (gọi sau khi nạp dữ liệu và trước khi lưu).</summary>
    public virtual void ValidateAll()
    {
        ValidatePrice(nameof(Block4hPrice), Block4hPrice, requiresPositive: true);
        ValidatePrice(nameof(DailyPrice), DailyPrice, requiresPositive: true);
        ValidatePrice(nameof(Monthly1Price), Monthly1Price, requiresPositive: true);
        ValidatePrice(nameof(Monthly3Price), Monthly3Price, requiresPositive: false);
        ValidatePrice(nameof(Monthly6Price), Monthly6Price, requiresPositive: false);
    }

    protected void ValidatePrice(string propertyName, decimal value, bool requiresPositive)
    {
        string? error = null;
        if (value < 0)
        {
            error = AppText.Get("Msg_Price_NegativeValue", VehicleTypeName);
        }
        else if (requiresPositive && value <= 0 && RequiresPositivePrices)
        {
            error = AppText.Get("Msg_Price_MustBePositive", VehicleTypeName);
        }

        ValidateProperty(propertyName, error);
    }
}
