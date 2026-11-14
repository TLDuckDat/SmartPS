using Microsoft.EntityFrameworkCore;
using SmartPS.Data;
using SmartPS.Services.Dialog;
using System.Collections.ObjectModel;
using SmartPS.Models.Parking;
using SmartPS.Services.Localization;

namespace SmartPS.ViewModels.Pricing;

public class PricingViewModel : ValidatableViewModelBase
{
    private readonly IDbContextFactory<SmartPsDbContext> _dbContextFactory;
    private readonly IDialogService _dialogService;

    public ObservableCollection<PricingRuleItemViewModel> PricingRules { get; } = new();

    private PricingRuleItemViewModel? _selectedRule;
    public PricingRuleItemViewModel? SelectedRule
    {
        get => _selectedRule;
        set => SetProperty(ref _selectedRule, value);
    }

    private int _defaultMaxVehicles;
    public int DefaultMaxVehicles
    {
        get => _defaultMaxVehicles;
        set
        {
            if (SetProperty(ref _defaultMaxVehicles, value))
            {
                ValidateMaxVehicles();
                OnPropertyChanged(nameof(HasAnyErrors));
            }
        }
    }

    /// <summary>Còn lỗi ở số xe tối đa hoặc ở bất kỳ ô giá nào: khoá nút Lưu.</summary>
    public bool HasAnyErrors => HasErrors || PricingRules.Any(r => r.HasErrors);

    private void RefreshErrorState()
    {
        OnPropertyChanged(nameof(HasAnyErrors));
        SaveCommand.RaiseCanExecuteChanged();
    }

    private void ValidateMaxVehicles()
        => ValidateProperty(nameof(DefaultMaxVehicles),
            DefaultMaxVehicles < 1 ? AppText.Get("Msg_Price_MaxVehiclesMin") : null);

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand SaveCommand { get; }

    public PricingViewModel(IDbContextFactory<SmartPsDbContext> dbContextFactory, IDialogService dialogService)
    {
        _dbContextFactory = dbContextFactory;
        _dialogService = dialogService;

        RefreshCommand = new AsyncRelayCommand(LoadDataAsync);
        SaveCommand = new AsyncRelayCommand(SaveDataAsync, () => !HasAnyErrors);
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

    public async Task LoadDataAsync()
    {
        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();
            var rules = await db.PricingRules.Include(x => x.VehicleType).OrderBy(x => x.RuleId).ToListAsync();
            var settings = await db.ParkingSettings.FirstOrDefaultAsync();

            PricingRules.Clear();
            foreach (var rule in rules)
            {
                var item = new PricingRuleItemViewModel
                {
                    RuleId = rule.RuleId,
                    VehicleTypeName = rule.VehicleType?.TypeName ?? AppText.Get("Str_Price_UnknownType"),
                    VehicleIcon = GetIcon(rule.VehicleType?.TypeName),
                    Block4hPrice = rule.Block4hPrice,
                    DailyPrice = rule.DailyPrice,
                    Monthly1Price = rule.Monthly1Price,
                    Monthly3Price = rule.Monthly3Price,
                    Monthly6Price = rule.Monthly6Price,
                    Description = rule.Description ?? string.Empty
                };
                item.ValidateAll();
                PricingRules.Add(item);
            }

            if (settings != null)
            {
                DefaultMaxVehicles = settings.DefaultMaxVehiclesPerHousehold;
            }

            ValidateMaxVehicles();

            SelectedRule = PricingRules.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _dialogService.ShowError(AppText.Get("Msg_Price_LoadError", ex.Message));
        }
    }

    public async Task SaveDataAsync()
    {
        try
        {
            ValidateMaxVehicles();
            foreach (var vm in PricingRules)
            {
                vm.ValidateAll();
            }

            if (HasAnyErrors)
            {
                return;
            }

            await using var db = await _dbContextFactory.CreateDbContextAsync();
            
            // Save pricing rules
            foreach (var vm in PricingRules)
            {
                var rule = await db.PricingRules.FindAsync(vm.RuleId);
                if (rule != null)
                {
                    rule.Block4hPrice = vm.Block4hPrice;
                    rule.DailyPrice = vm.DailyPrice;
                    rule.Monthly1Price = vm.Monthly1Price;
                    rule.Monthly3Price = vm.Monthly3Price;
                    rule.Monthly6Price = vm.Monthly6Price;
                }
            }

            // Save parking settings
            var settings = await db.ParkingSettings.FirstOrDefaultAsync();
            if (settings == null)
            {
                settings = new ParkingSettings { DefaultMaxVehiclesPerHousehold = DefaultMaxVehicles };
                db.ParkingSettings.Add(settings);
            }
            else
            {
                settings.DefaultMaxVehiclesPerHousehold = DefaultMaxVehicles;
            }

            await db.SaveChangesAsync();
            _dialogService.ShowWarning(AppText.Get("Msg_Price_Saved"));
        }
        catch (Exception ex)
        {
            _dialogService.ShowError(AppText.Get("Msg_Price_SaveError", ex.Message));
        }
    }

    private string GetIcon(string? typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return "❓";
        if (typeName.Contains("máy", StringComparison.OrdinalIgnoreCase)) return "🏍";
        if (typeName.Contains("ô tô", StringComparison.OrdinalIgnoreCase)) return "🚗";
        if (typeName.Contains("đạp", StringComparison.OrdinalIgnoreCase)) return "🚲";
        return "🚚";
    }
}
