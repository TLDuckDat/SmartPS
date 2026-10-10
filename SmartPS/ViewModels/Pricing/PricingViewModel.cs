using Microsoft.EntityFrameworkCore;
using SmartPS.Data;
using SmartPS.Services.Dialog;
using System.Collections.ObjectModel;
using SmartPS.Models.Parking;

namespace SmartPS.ViewModels.Pricing;

public class PricingViewModel : ViewModelBase
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
        set => SetProperty(ref _defaultMaxVehicles, value);
    }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand SaveCommand { get; }

    public PricingViewModel(IDbContextFactory<SmartPsDbContext> dbContextFactory, IDialogService dialogService)
    {
        _dbContextFactory = dbContextFactory;
        _dialogService = dialogService;

        RefreshCommand = new AsyncRelayCommand(LoadDataAsync);
        SaveCommand = new AsyncRelayCommand(SaveDataAsync);

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
                PricingRules.Add(new PricingRuleItemViewModel
                {
                    RuleId = rule.RuleId,
                    VehicleTypeName = rule.VehicleType?.TypeName ?? "Không xác định",
                    VehicleIcon = GetIcon(rule.VehicleType?.TypeName),
                    Block4hPrice = rule.Block4hPrice,
                    DailyPrice = rule.DailyPrice,
                    Monthly1Price = rule.Monthly1Price,
                    Monthly3Price = rule.Monthly3Price,
                    Monthly6Price = rule.Monthly6Price,
                    Description = rule.Description ?? string.Empty
                });
            }

            if (settings != null)
            {
                DefaultMaxVehicles = settings.DefaultMaxVehiclesPerHousehold;
            }

            SelectedRule = PricingRules.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"Lỗi tải cấu hình biểu phí: {ex.Message}");
        }
    }

    public async Task SaveDataAsync()
    {
        try
        {
            // 1. Validate số xe tối đa mỗi hộ
            if (DefaultMaxVehicles < 1)
            {
                _dialogService.ShowError("Số xe tối đa mặc định cho mỗi hộ gia đình phải từ 1 xe trở lên!");
                return;
            }

            // 2. Validate biểu phí từng loại phương tiện
            foreach (var vm in PricingRules)
            {
                if (vm.Block4hPrice < 0 || vm.DailyPrice < 0 || vm.Monthly1Price < 0 || vm.Monthly3Price < 0 || vm.Monthly6Price < 0)
                {
                    _dialogService.ShowError($"Biểu phí của '{vm.VehicleTypeName}' không được có giá trị âm!");
                    return;
                }

                // Với xe máy và ô tô, giá vé lượt và vé tháng không được bằng 0
                if ((vm.VehicleTypeName.Contains("máy", StringComparison.OrdinalIgnoreCase) || vm.VehicleTypeName.Contains("ô tô", StringComparison.OrdinalIgnoreCase))
                    && (vm.Block4hPrice <= 0 || vm.DailyPrice <= 0 || vm.Monthly1Price <= 0))
                {
                    _dialogService.ShowError($"Giá vé lượt và vé tháng của '{vm.VehicleTypeName}' phải lớn hơn 0đ!");
                    return;
                }
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
            _dialogService.ShowWarning("Lưu thay đổi thành công!");
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"Lỗi lưu cấu hình biểu phí: {ex.Message}");
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
