using SmartPS.Services.Localization;
using SmartPS.ViewModels.Pricing;

namespace SmartPS.Tests.Unit;

/// <summary>DA-3: kiểm tra ô giá trong form Bảng giá.</summary>
public class PricingRuleItemValidationTests
{
    private static PricingRuleItemViewModel Motorbike()
    {
        var vm = new PricingRuleItemViewModel { VehicleTypeName = "Xe máy", Block4hPrice = 5000, DailyPrice = 25000, Monthly1Price = 100000 };
        vm.ValidateAll();
        return vm;
    }

    [Fact]
    public void Valid_rule_has_no_errors()
    {
        Assert.False(Motorbike().HasErrors);
    }

    [Fact]
    public void Negative_price_is_error_and_clears_when_fixed()
    {
        var vm = Motorbike();
        vm.Monthly3Price = -1;
        Assert.True(vm.HasErrors);
        Assert.Equal(new[] { AppText.Get("Msg_Price_NegativeValue", "Xe máy") }, vm.GetErrors(nameof(vm.Monthly3Price)).Cast<string>());
        vm.Monthly3Price = 0;
        Assert.False(vm.HasErrors);
    }

    [Fact]
    public void Zero_price_on_motorbike_or_car_is_error()
    {
        var vm = Motorbike();
        vm.Block4hPrice = 0;
        Assert.Equal(new[] { AppText.Get("Msg_Price_MustBePositive", "Xe máy") }, vm.GetErrors(nameof(vm.Block4hPrice)).Cast<string>());

        var car = new PricingRuleItemViewModel { VehicleTypeName = "Ô tô", Block4hPrice = 1, DailyPrice = 1, Monthly1Price = 1 };
        car.DailyPrice = 0;
        Assert.True(car.HasErrors);
    }

    [Fact]
    public void Zero_price_on_other_vehicle_types_is_allowed()
    {
        var bike = new PricingRuleItemViewModel { VehicleTypeName = "Xe đạp" };
        bike.ValidateAll();
        Assert.False(bike.HasErrors);
    }

    [Fact]
    public void Zero_monthly3_and_monthly6_are_allowed_for_motorbike()
    {
        var vm = Motorbike();
        vm.Monthly3Price = 0;
        vm.Monthly6Price = 0;
        Assert.False(vm.HasErrors);
    }
}
