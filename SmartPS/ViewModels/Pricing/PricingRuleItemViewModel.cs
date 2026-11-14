using System.Globalization;
using SmartPS.Services.Localization;
using SmartPS.Services.Pricing;

namespace SmartPS.ViewModels.Pricing;

public class PricingRuleItemViewModel : ValidatableViewModelBase
{
    public int RuleId { get; set; }
    public int VehicleTypeId { get; set; }

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
                ResetText(nameof(Block4hPriceText));
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
                ResetText(nameof(DailyPriceText));
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
                ResetText(nameof(Monthly1PriceText));
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
                ResetText(nameof(Monthly3PriceText));
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
                ResetText(nameof(Monthly6PriceText));
                ValidatePrice(nameof(Monthly6Price), _monthly6Price, requiresPositive: false);
            }
        }
    }

    private int _gracePeriodMinutes = 15;
    public int GracePeriodMinutes
    {
        get => _gracePeriodMinutes;
        set
        {
            if (SetProperty(ref _gracePeriodMinutes, value))
            {
                ResetText(nameof(GracePeriodMinutesText));
                ValidateGrace();
            }
        }
    }

    private decimal _overnightSurcharge;
    public decimal OvernightSurcharge
    {
        get => _overnightSurcharge;
        set
        {
            if (SetProperty(ref _overnightSurcharge, value))
            {
                ResetText(nameof(OvernightSurchargeText));
                ValidatePrice(nameof(OvernightSurcharge), _overnightSurcharge, requiresPositive: false);
            }
        }
    }

    private decimal _overQuotaDailyPrice;
    public decimal OverQuotaDailyPrice
    {
        get => _overQuotaDailyPrice;
        set
        {
            if (SetProperty(ref _overQuotaDailyPrice, value))
            {
                ResetText(nameof(OverQuotaDailyPriceText));
                ValidatePrice(nameof(OverQuotaDailyPrice), _overQuotaDailyPrice, requiresPositive: false);
            }
        }
    }


    /// <summary>Ô nhập tiền dạng chuỗi: chữ rác không thành 0 âm thầm mà tạo lỗi và khoá nút Lưu.</summary>
    public string Block4hPriceText
    {
        get => TextOf(nameof(Block4hPriceText), FormatMoney(Block4hPrice));
        set => SetMoneyText(nameof(Block4hPriceText), value, v => Block4hPrice = v);
    }

    /// <summary>Ô nhập tiền dạng chuỗi: chữ rác không thành 0 âm thầm mà tạo lỗi và khoá nút Lưu.</summary>
    public string DailyPriceText
    {
        get => TextOf(nameof(DailyPriceText), FormatMoney(DailyPrice));
        set => SetMoneyText(nameof(DailyPriceText), value, v => DailyPrice = v);
    }

    /// <summary>Ô nhập tiền dạng chuỗi: chữ rác không thành 0 âm thầm mà tạo lỗi và khoá nút Lưu.</summary>
    public string Monthly1PriceText
    {
        get => TextOf(nameof(Monthly1PriceText), FormatMoney(Monthly1Price));
        set => SetMoneyText(nameof(Monthly1PriceText), value, v => Monthly1Price = v);
    }

    /// <summary>Ô nhập tiền dạng chuỗi: chữ rác không thành 0 âm thầm mà tạo lỗi và khoá nút Lưu.</summary>
    public string Monthly3PriceText
    {
        get => TextOf(nameof(Monthly3PriceText), FormatMoney(Monthly3Price));
        set => SetMoneyText(nameof(Monthly3PriceText), value, v => Monthly3Price = v);
    }

    /// <summary>Ô nhập tiền dạng chuỗi: chữ rác không thành 0 âm thầm mà tạo lỗi và khoá nút Lưu.</summary>
    public string Monthly6PriceText
    {
        get => TextOf(nameof(Monthly6PriceText), FormatMoney(Monthly6Price));
        set => SetMoneyText(nameof(Monthly6PriceText), value, v => Monthly6Price = v);
    }

    /// <summary>Ô nhập tiền dạng chuỗi: chữ rác không thành 0 âm thầm mà tạo lỗi và khoá nút Lưu.</summary>
    public string OvernightSurchargeText
    {
        get => TextOf(nameof(OvernightSurchargeText), FormatMoney(OvernightSurcharge));
        set => SetMoneyText(nameof(OvernightSurchargeText), value, v => OvernightSurcharge = v);
    }

    /// <summary>Ô nhập tiền dạng chuỗi: chữ rác không thành 0 âm thầm mà tạo lỗi và khoá nút Lưu.</summary>
    public string OverQuotaDailyPriceText
    {
        get => TextOf(nameof(OverQuotaDailyPriceText), FormatMoney(OverQuotaDailyPrice));
        set => SetMoneyText(nameof(OverQuotaDailyPriceText), value, v => OverQuotaDailyPrice = v);
    }

    /// <summary>Ô nhập số nguyên dạng chuỗi: để trống/gõ chữ tạo lỗi và khoá nút Lưu.</summary>
    public string GracePeriodMinutesText
    {
        get => TextOf(nameof(GracePeriodMinutesText), GracePeriodMinutes.ToString(CultureInfo.InvariantCulture));
        set => SetIntText(nameof(GracePeriodMinutesText), value, v => GracePeriodMinutes = v);
    }

    private readonly Dictionary<string, string> _rawText = new(StringComparer.Ordinal);
    private bool _fromText;

    private string TextOf(string textProperty, string formatted)
        => _rawText.TryGetValue(textProperty, out var raw) ? raw : formatted;

    /// <summary>Giá trị được gán từ mã (không phải do gõ): bỏ chuỗi đang gõ dở và lỗi chuyển đổi, làm mới ô.</summary>
    private void ResetText(string textProperty)
    {
        if (_fromText)
        {
            return;
        }

        _rawText.Remove(textProperty);
        ClearErrors(textProperty);
        OnPropertyChanged(textProperty);
    }

    private void SetMoneyText(string textProperty, string? text, Action<decimal> apply)
        => SetText(textProperty, text, TryParseMoney, apply);

    private void SetIntText(string textProperty, string? text, Action<int> apply)
        => SetText(textProperty, text, TryParseInt, apply);

    private delegate bool TryParse<T>(string text, out T value);

    private void SetText<T>(string textProperty, string? text, TryParse<T> parse, Action<T> apply)
    {
        text ??= string.Empty;
        _rawText[textProperty] = text;
        if (!parse(text, out var value))
        {
            SetErrors(textProperty, new[] { AppText.Get("Msg_Price_NotANumber") });
            return;
        }

        ClearErrors(textProperty);
        _fromText = true;
        try
        {
            apply(value);
        }
        finally
        {
            _fromText = false;
        }
    }

    /// <summary>Số tiền nguyên; chấp nhận dấu chấm/phẩy ngăn cách hàng nghìn (như ô hiển thị 100.000).</summary>
    internal static bool TryParseMoney(string text, out decimal value)
    {
        var cleaned = text.Trim().Replace(".", string.Empty).Replace(",", string.Empty).Replace(" ", string.Empty);
        return decimal.TryParse(cleaned, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }

    internal static bool TryParseInt(string text, out int value)
        => int.TryParse(text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);

    internal static string FormatMoney(decimal value)
        => value == 0 ? "0" : value.ToString("N0", CultureInfo.InvariantCulture).Replace(",", ".");

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
        ValidatePrice(nameof(OvernightSurcharge), OvernightSurcharge, requiresPositive: false);
        ValidatePrice(nameof(OverQuotaDailyPrice), OverQuotaDailyPrice, requiresPositive: false);
        ValidateGrace();
    }

    private void ValidateGrace()
        => ValidateProperty(nameof(GracePeriodMinutes),
            GracePeriodMinutes is < 0 or > IPricingService.MaxGracePeriodMinutes
                ? AppText.Get("Msg_Price_GraceRange", VehicleTypeName, IPricingService.MaxGracePeriodMinutes)
                : null);

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
