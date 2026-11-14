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
        }, 2);

        public PricingConfiguration? Saved { get; private set; }
        public OperationResult SaveResult { get; set; } = OperationResult.Ok();
        public Exception? LoadException { get; set; }

        public Task<PricingConfiguration> LoadPoliciesAsync(CancellationToken cancellationToken = default)
            => LoadException is { } ex ? Task.FromException<PricingConfiguration>(ex) : Task.FromResult(Configuration);

        public Task<OperationResult> SavePoliciesAsync(PricingConfiguration configuration, CancellationToken cancellationToken = default)
        {
            Saved = configuration;
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
}
