using SmartPS.Constants;
using SmartPS.Services.Authorization;
using SmartPS.Services.Common;
using SmartPS.Services.Dialog;
using SmartPS.Services.Localization;
using SmartPS.Services.Pricing;
using System.Collections.ObjectModel;

namespace SmartPS.ViewModels.Pricing;

public class PricingViewModel : ValidatableViewModelBase
{
    private readonly IPricingService _pricingService;
    private readonly IDialogService _dialogService;
    private readonly IPermissionService _permissionService;

    public ObservableCollection<PricingRuleItemViewModel> PricingRules { get; } = new();

    private PricingRuleItemViewModel? _selectedRule;
    public PricingRuleItemViewModel? SelectedRule
    {
        get => _selectedRule;
        set => SetProperty(ref _selectedRule, value);
    }

    private int _defaultMaxVehicles = 1;
    public int DefaultMaxVehicles
    {
        get => _defaultMaxVehicles;
        set
        {
            if (SetProperty(ref _defaultMaxVehicles, value))
            {
                ValidateMaxVehicles();
            }
        }
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RefreshErrorState();
            }
        }
    }

    /// <summary>Người dùng có quyền Pricing.Manage: chỉ khi đó nút Lưu mới mở.</summary>
    public bool CanEdit => _permissionService.HasPermission(Permissions.PricingManage);

    /// <summary>Tooltip của nút Lưu khi bị khoá vì thiếu quyền; null khi được phép.</summary>
    public string? SaveToolTip => CanEdit ? null : AppText.Get("Msg_Auth_PermissionDenied");

    /// <summary>Còn lỗi ở số xe tối đa hoặc ở bất kỳ ô giá nào: khoá nút Lưu.</summary>
    public bool HasAnyErrors => HasErrors || PricingRules.Any(r => r.HasErrors);

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand SaveCommand { get; }

    public PricingViewModel(IPricingService pricingService, IDialogService dialogService, IPermissionService permissionService)
    {
        _pricingService = pricingService ?? throw new ArgumentNullException(nameof(pricingService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _permissionService = permissionService ?? throw new ArgumentNullException(nameof(permissionService));

        RefreshCommand = new AsyncRelayCommand(LoadDataAsync);
        SaveCommand = new AsyncRelayCommand(SaveDataAsync, () => CanEdit && !IsBusy && !HasAnyErrors);
        ErrorsChanged += (_, _) => RefreshErrorState();
        PricingRules.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null)
            {
                foreach (PricingRuleItemViewModel item in e.NewItems)
                {
                    item.ErrorsChanged += (_, _) => RefreshErrorState();
                }
            }

            RefreshErrorState();
        };

        _ = LoadDataAsync();
    }

    private void RefreshErrorState()
    {
        OnPropertyChanged(nameof(HasAnyErrors));
        SaveCommand?.RaiseCanExecuteChanged();
    }

    private void ValidateMaxVehicles()
        => ValidateProperty(nameof(DefaultMaxVehicles),
            DefaultMaxVehicles < 1 ? AppText.Get("Msg_Price_MaxVehiclesMin") : null);

    public async Task LoadDataAsync()
    {
        try
        {
            var configuration = await _pricingService.LoadPoliciesAsync();

            PricingRules.Clear();
            foreach (var rule in configuration.Rules)
            {
                var item = new PricingRuleItemViewModel
                {
                    RuleId = rule.RuleId,
                    VehicleTypeId = rule.VehicleTypeId,
                    VehicleTypeName = string.IsNullOrEmpty(rule.VehicleTypeName) ? AppText.Get("Str_Price_UnknownType") : rule.VehicleTypeName,
                    VehicleIcon = GetIcon(rule.VehicleTypeName),
                    Block4hPrice = rule.Block4hPrice,
                    DailyPrice = rule.DailyPrice,
                    Monthly1Price = rule.Monthly1Price,
                    Monthly3Price = rule.Monthly3Price,
                    Monthly6Price = rule.Monthly6Price,
                    GracePeriodMinutes = rule.GracePeriodMinutes,
                    OvernightSurcharge = rule.OvernightSurcharge,
                    OverQuotaDailyPrice = rule.OverQuotaDailyPrice,
                    Description = rule.Description ?? string.Empty
                };
                item.ValidateAll();
                PricingRules.Add(item);
            }

            DefaultMaxVehicles = configuration.DefaultMaxVehiclesPerHousehold;
            ValidateMaxVehicles();
            SelectedRule = PricingRules.FirstOrDefault();
            OnPropertyChanged(nameof(CanEdit));
            OnPropertyChanged(nameof(SaveToolTip));
            RefreshErrorState();
        }
        catch (PermissionDeniedException)
        {
            _dialogService.ShowError(AppText.Get("Msg_Auth_PermissionDenied"));
        }
        catch (Exception ex)
        {
            _dialogService.ShowError(AppText.Get("Msg_Price_LoadError", ex.Message));
        }
    }

    public async Task SaveDataAsync()
    {
        ValidateMaxVehicles();
        foreach (var vm in PricingRules)
        {
            vm.ValidateAll();
        }

        if (!CanEdit || HasAnyErrors)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var configuration = new PricingConfiguration(
                PricingRules.Select(vm => new PricingRuleData
                {
                    RuleId = vm.RuleId,
                    VehicleTypeId = vm.VehicleTypeId,
                    VehicleTypeName = vm.VehicleTypeName,
                    Block4hPrice = vm.Block4hPrice,
                    DailyPrice = vm.DailyPrice,
                    Monthly1Price = vm.Monthly1Price,
                    Monthly3Price = vm.Monthly3Price,
                    Monthly6Price = vm.Monthly6Price,
                    GracePeriodMinutes = vm.GracePeriodMinutes,
                    OvernightSurcharge = vm.OvernightSurcharge,
                    OverQuotaDailyPrice = vm.OverQuotaDailyPrice,
                    Description = vm.Description
                }).ToList(),
                DefaultMaxVehicles);

            var result = await _pricingService.SavePoliciesAsync(configuration);
            if (result.Success)
            {
                _dialogService.ShowSuccess(AppText.Get("Msg_Price_Saved"));
            }
            else if (result.Error == OperationError.PermissionDenied)
            {
                _dialogService.ShowError(AppText.Get("Msg_Auth_PermissionDenied"));
            }
            else if (result.Error == OperationError.Validation || result.Error == OperationError.NotFound)
            {
                _dialogService.ShowWarning(result.Message);
            }
            else
            {
                _dialogService.ShowError(AppText.Get("Msg_Price_SaveError", result.Message));
            }
        }
        catch (Exception ex)
        {
            _dialogService.ShowError(AppText.Get("Msg_Price_SaveError", ex.Message));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string GetIcon(string? typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return "❓";
        if (typeName.Contains("máy", StringComparison.OrdinalIgnoreCase)) return "🏍";
        if (typeName.Contains("ô tô", StringComparison.OrdinalIgnoreCase)) return "🚗";
        if (typeName.Contains("đạp", StringComparison.OrdinalIgnoreCase)) return "🚲";
        return "🚚";
    }
}
