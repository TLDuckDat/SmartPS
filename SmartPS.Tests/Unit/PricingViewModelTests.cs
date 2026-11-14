using SmartPS.Constants;
using SmartPS.Services.Authorization;
using SmartPS.Services.Common;
using SmartPS.Services.Localization;
using SmartPS.Services.Pricing;
using SmartPS.ViewModels.Pricing;

namespace SmartPS.Tests.Unit;

/// <summary>KAN-29b / DA-3: màn Bảng giá đi qua PricingService, nút Lưu khoá khi thiếu quyền hoặc còn lỗi.</summary>
public class PricingViewModelTests
{
    private sealed class Permissions_ : IPermissionService
    {
        private readonly bool _allowed;
        public Permissions_(bool allowed) => _allowed = allowed;
        public bool IsAuthenticated => true;
        public bool HasPermission(string permission) => _allowed && permission == Permissions.PricingManage;
        public bool HasAnyPermission(params string[] permissions) => permissions.Any(HasPermission);
        public bool HasAllPermissions(params string[] permissions) => permissions.All(HasPermission);
        public bool IsAdmin() => false;
    }

    private sealed class FakePricingService : IPricingService
    {
        public PricingConfiguration Configuration { get; set; } = new(new[]
        {
            new PricingRuleData
            {
                RuleId = 1, VehicleTypeId = 1, VehicleTypeName = "Xe máy", Block4hPrice = 5000, DailyPrice = 25000,
                Monthly1Price = 100000, Monthly3Price = 285000, Monthly6Price = 540000,
                GracePeriodMinutes = 15, OvernightSurcharge = 2000, OverQuotaDailyPrice = 30000
            }
        }.Select(r => r with { LoadedStamp = r.ValuesStamp() }).ToList(), 2, 2);

        public PricingConfiguration? Saved { get; private set; }
        public OperationResult SaveResult { get; set; } = OperationResult.Ok();
        public Exception? LoadException { get; set; }

        public Task<PricingConfiguration> LoadPoliciesAsync(CancellationToken cancellationToken = default)
            => LoadException is { } ex ? Task.FromException<PricingConfiguration>(ex) : Task.FromResult(Configuration);

        public Task<OperationResult> SavePoliciesAsync(PricingConfiguration configuration, CancellationToken cancellationToken = default)
        {
            Saved = configuration;
            if (SaveResult.Success)
            {
                Configuration = new PricingConfiguration(
                    configuration.Rules.Select(r => r with { LoadedStamp = r.ValuesStamp() }).ToList(),
                    configuration.DefaultMaxVehiclesPerHousehold,
                    configuration.DefaultMaxVehiclesPerHousehold);
            }

            return Task.FromResult(SaveResult);
        }
    }

    private static (PricingViewModel Vm, FakePricingService Service, FakeDialogService Dialogs) Create(bool allowed = true)
    {
        var service = new FakePricingService();
        var dialogs = new FakeDialogService();
        var vm = new PricingViewModel(service, dialogs, new Permissions_(allowed));
        return (vm, service, dialogs);
    }

    [Fact]
    public async Task Load_fills_rules_including_the_new_fields_and_max_vehicles()
    {
        var (vm, _, _) = Create();
        await vm.LoadDataAsync();

        var rule = Assert.Single(vm.PricingRules);
        Assert.Equal(15, rule.GracePeriodMinutes);
        Assert.Equal(2000m, rule.OvernightSurcharge);
        Assert.Equal(30000m, rule.OverQuotaDailyPrice);
        Assert.Equal(2, vm.DefaultMaxVehicles);
        Assert.False(vm.HasAnyErrors);
        Assert.True(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task Save_is_locked_without_Pricing_Manage_permission()
    {
        var (vm, service, dialogs) = Create(allowed: false);
        await vm.LoadDataAsync();

        Assert.False(vm.CanEdit);
        Assert.False(vm.SaveCommand.CanExecute(null));
        Assert.Equal(AppText.Get("Msg_Auth_PermissionDenied"), vm.SaveToolTip);

        await vm.SaveDataAsync();
        Assert.Null(service.Saved);
        Assert.Empty(dialogs.Successes);
    }

    [Fact]
    public async Task Field_errors_lock_save_and_clear_when_fixed()
    {
        var (vm, _, _) = Create();
        await vm.LoadDataAsync();
        var rule = vm.PricingRules[0];

        rule.GracePeriodMinutes = 121;
        Assert.True(vm.HasAnyErrors);
        Assert.False(vm.SaveCommand.CanExecute(null));
        rule.GracePeriodMinutes = 30;
        Assert.False(vm.HasAnyErrors);

        rule.OverQuotaDailyPrice = -1;
        Assert.NotEmpty(rule.GetErrors(nameof(rule.OverQuotaDailyPrice)).Cast<string>());
        Assert.False(vm.SaveCommand.CanExecute(null));
        rule.OverQuotaDailyPrice = 0;

        vm.DefaultMaxVehicles = 0;
        Assert.Equal(new[] { AppText.Get("Msg_Price_MaxVehiclesMin") }, vm.GetErrors(nameof(vm.DefaultMaxVehicles)).Cast<string>());
        Assert.False(vm.SaveCommand.CanExecute(null));
        vm.DefaultMaxVehicles = 3;

        Assert.False(vm.HasAnyErrors);
        Assert.True(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task Save_sends_the_edited_configuration_to_the_service_and_reports_success()
    {
        var (vm, service, dialogs) = Create();
        await vm.LoadDataAsync();
        vm.PricingRules[0].Block4hPrice = 6000;
        vm.PricingRules[0].GracePeriodMinutes = 10;
        vm.DefaultMaxVehicles = 4;

        await vm.SaveDataAsync();

        Assert.NotNull(service.Saved);
        var saved = Assert.Single(service.Saved!.Rules);
        Assert.Equal(6000m, saved.Block4hPrice);
        Assert.Equal(10, saved.GracePeriodMinutes);
        Assert.Equal(4, service.Saved.DefaultMaxVehiclesPerHousehold);
        Assert.Single(dialogs.Successes);
    }

    [Fact]
    public async Task Save_does_not_call_the_service_while_there_are_errors()
    {
        var (vm, service, _) = Create();
        await vm.LoadDataAsync();
        vm.PricingRules[0].Monthly1Price = -5;

        await vm.SaveDataAsync();

        Assert.Null(service.Saved);
    }

    [Fact]
    public async Task Service_permission_denial_is_shown_as_an_error()
    {
        var (vm, service, dialogs) = Create();
        await vm.LoadDataAsync();
        service.SaveResult = OperationResult.Fail(OperationError.PermissionDenied, "x");

        await vm.SaveDataAsync();

        Assert.Equal(new[] { AppText.Get("Msg_Auth_PermissionDenied") }, dialogs.Errors);
        Assert.Empty(dialogs.Successes);
    }

    [Fact]
    public async Task Load_permission_denial_is_shown_as_an_error()
    {
        var (vm, service, dialogs) = Create();
        service.LoadException = new PermissionDeniedException(new[] { Permissions.PricingManage }, "tester");

        await vm.LoadDataAsync();

        Assert.Contains(AppText.Get("Msg_Auth_PermissionDenied"), dialogs.Errors);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("12x")]
    public async Task Garbage_in_an_integer_box_raises_an_error_locks_save_and_clears_when_fixed(string input)
    {
        var (vm, service, _) = Create();
        await vm.LoadDataAsync();
        var rule = vm.PricingRules[0];

        rule.GracePeriodMinutesText = input;
        Assert.True(rule.HasErrors);
        Assert.True(vm.HasAnyErrors);
        Assert.False(vm.SaveCommand.CanExecute(null));
        Assert.Equal(new[] { AppText.Get("Msg_Price_NotANumber") }, rule.GetErrors(nameof(rule.GracePeriodMinutesText)).Cast<string>());
        await vm.SaveDataAsync();
        Assert.Null(service.Saved);

        rule.GracePeriodMinutesText = "20";
        Assert.False(vm.HasAnyErrors);
        Assert.True(vm.SaveCommand.CanExecute(null));
        Assert.Equal(20, rule.GracePeriodMinutes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1.5x")]
    public async Task Garbage_in_a_money_box_raises_an_error_instead_of_becoming_zero(string input)
    {
        var (vm, service, _) = Create();
        await vm.LoadDataAsync();
        var rule = vm.PricingRules[0];

        rule.Block4hPriceText = input;
        Assert.True(vm.HasAnyErrors);
        Assert.False(vm.SaveCommand.CanExecute(null));
        Assert.Equal(5000m, rule.Block4hPrice);

        rule.Block4hPriceText = "6.000";
        Assert.False(vm.HasAnyErrors);
        Assert.Equal(6000m, rule.Block4hPrice);
        await vm.SaveDataAsync();
        Assert.Equal(6000m, Assert.Single(service.Saved!.Rules).Block4hPrice);
    }

    [Fact]
    public async Task Money_text_shows_the_dotted_format_of_the_loaded_value()
    {
        var (vm, _, _) = Create();
        await vm.LoadDataAsync();

        Assert.Equal("100.000", vm.PricingRules[0].Monthly1PriceText);
        Assert.Equal("2", vm.DefaultMaxVehiclesText);
    }

    [Fact]
    public async Task Garbage_in_the_max_vehicles_box_raises_an_error_and_locks_save()
    {
        var (vm, service, _) = Create();
        await vm.LoadDataAsync();

        vm.DefaultMaxVehiclesText = "";
        Assert.True(vm.HasAnyErrors);
        Assert.False(vm.SaveCommand.CanExecute(null));
        Assert.Equal(new[] { AppText.Get("Msg_Price_NotANumber") }, vm.GetErrors(nameof(vm.DefaultMaxVehiclesText)).Cast<string>());
        await vm.SaveDataAsync();
        Assert.Null(service.Saved);

        vm.DefaultMaxVehiclesText = "5";
        Assert.False(vm.HasAnyErrors);
        Assert.Equal(5, vm.DefaultMaxVehicles);
    }

    /// <summary>Fake dùng chung cho kiểm tra đồng thời: lưu giả lập kiểm tra dấu hiệu như dịch vụ thật.</summary>
    [Fact]
    public async Task Saving_twice_in_a_row_does_not_conflict_with_itself()
    {
        var (vm, service, dialogs) = Create();
        await vm.LoadDataAsync();
        var rule = vm.PricingRules[0];

        rule.GracePeriodMinutesText = "45";
        await vm.SaveDataAsync();
        Assert.Equal(45, Assert.Single(service.Saved!.Rules).GracePeriodMinutes);
        var stampAfterFirst = vm.PricingRules[0].LoadedStamp;
        Assert.Equal(service.Configuration.Rules[0].ValuesStamp(), stampAfterFirst);

        vm.PricingRules[0].GracePeriodMinutesText = "60";
        vm.DefaultMaxVehiclesText = "7";
        await vm.SaveDataAsync();

        var saved = Assert.Single(service.Saved!.Rules);
        Assert.Equal(60, saved.GracePeriodMinutes);
        Assert.Equal(stampAfterFirst, saved.LoadedStamp);
        Assert.Equal(2, service.Saved.LoadedDefaultMaxVehicles);
        Assert.Equal(2, dialogs.Successes.Count);
    }

    [Fact]
    public void Stamp_ignores_decimal_scale_and_current_culture()
    {
        var a = new PricingRuleData { Block4hPrice = 5000.00m, DailyPrice = 25000m, OvernightSurcharge = 1500.5m };
        var b = new PricingRuleData { Block4hPrice = 5000m, DailyPrice = 25000.0m, OvernightSurcharge = 1500.50m };
        Assert.Equal(a.ValuesStamp(), b.ValuesStamp());

        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("vi-VN");
            var vi = a.ValuesStamp();
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("en-US");
            Assert.Equal(vi, a.ValuesStamp());
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("Block4hPriceText", "-5")]
    [InlineData("Block4hPriceText", "0")]
    [InlineData("Monthly1PriceText", "0")]
    [InlineData("OverQuotaDailyPriceText", "-1")]
    [InlineData("GracePeriodMinutesText", "500")]
    [InlineData("GracePeriodMinutesText", "-1")]
    [InlineData("Block4hPriceText", "99999999999999999999")]
    public async Task Business_errors_are_reported_on_the_bound_text_property(string textProperty, string input)
    {
        var (vm, _, _) = Create();
        await vm.LoadDataAsync();
        var rule = vm.PricingRules[0];

        typeof(PricingRuleItemViewModel).GetProperty(textProperty)!.SetValue(rule, input);

        Assert.NotEmpty(rule.GetErrors(textProperty).Cast<string>());
        Assert.False(vm.SaveCommand.CanExecute(null));

        var fine = textProperty == "GracePeriodMinutesText" ? "10" : "7000";
        typeof(PricingRuleItemViewModel).GetProperty(textProperty)!.SetValue(rule, fine);
        Assert.Empty(rule.GetErrors(textProperty).Cast<string>());
        Assert.False(vm.HasAnyErrors);
    }

    [Fact]
    public async Task Max_vehicles_business_error_is_reported_on_the_bound_text_property()
    {
        var (vm, _, _) = Create();
        await vm.LoadDataAsync();

        vm.DefaultMaxVehiclesText = "0";
        Assert.NotEmpty(vm.GetErrors(nameof(vm.DefaultMaxVehiclesText)).Cast<string>());
        Assert.False(vm.SaveCommand.CanExecute(null));
        vm.DefaultMaxVehiclesText = "3";
        Assert.Empty(vm.GetErrors(nameof(vm.DefaultMaxVehiclesText)).Cast<string>());
        Assert.False(vm.HasAnyErrors);
    }

    [Theory]
    [InlineData("1.5")]
    [InlineData("6,5")]
    [InlineData("2.500,50")]
    [InlineData("1.23")]
    [InlineData("12.3456")]
    [InlineData(".5")]
    public async Task Ambiguous_or_decimal_money_text_is_rejected(string input)
    {
        var (vm, _, _) = Create();
        await vm.LoadDataAsync();
        var rule = vm.PricingRules[0];

        rule.Block4hPriceText = input;

        Assert.Equal(new[] { AppText.Get("Msg_Price_NotANumber") }, rule.GetErrors(nameof(rule.Block4hPriceText)).Cast<string>());
        Assert.Equal(5000m, rule.Block4hPrice);
        Assert.False(vm.SaveCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("6.000", 6000)]
    [InlineData("6,000", 6000)]
    [InlineData("1.234.567", 1234567)]
    [InlineData("6000", 6000)]
    [InlineData(" 7000 ", 7000)]
    public async Task Plain_and_thousand_grouped_money_text_is_accepted(string input, int expected)
    {
        var (vm, _, _) = Create();
        await vm.LoadDataAsync();
        var rule = vm.PricingRules[0];

        rule.Block4hPriceText = input;

        Assert.False(vm.HasAnyErrors);
        Assert.Equal(expected, rule.Block4hPrice);
    }

    [Fact]
    public async Task Max_vehicles_error_does_not_stick_after_refresh_when_the_number_is_unchanged()
    {
        var (vm, _, _) = Create();
        await vm.LoadDataAsync();

        vm.DefaultMaxVehiclesText = "abc";
        Assert.True(vm.HasAnyErrors);

        await vm.LoadDataAsync();

        Assert.False(vm.HasAnyErrors);
        Assert.Equal("2", vm.DefaultMaxVehiclesText);
        Assert.True(vm.SaveCommand.CanExecute(null));
    }
}
