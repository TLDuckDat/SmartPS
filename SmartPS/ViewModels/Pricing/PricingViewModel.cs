using System.Collections.ObjectModel;
using SmartPS.Models.Parking;

namespace SmartPS.ViewModels.Pricing;

public class PricingViewModel : ViewModelBase
{
    public ObservableCollection<PricingRuleItemViewModel> PricingRules { get; } = new();

    private PricingRuleItemViewModel? _selectedRule;
    public PricingRuleItemViewModel? SelectedRule
    {
        get => _selectedRule;
        set => SetProperty(ref _selectedRule, value);
    }

    public AsyncRelayCommand RefreshCommand { get; }

    public PricingViewModel()
    {
        RefreshCommand = new AsyncRelayCommand(LoadDataAsync);
        InitializeRules();
        _ = LoadDataAsync();
    }

    private void InitializeRules()
    {
        PricingRules.Clear();
        PricingRules.Add(new PricingRuleItemViewModel
        {
            RuleId = 1,
            VehicleTypeName = "Xe máy & Xe tay ga",
            VehicleIcon = "🏍",
            FirstBlockMinutes = 120,
            FirstBlockPrice = 5000,
            AdditionalPricePerHour = 2000,
            OvernightPrice = 15000,
            MonthlyPassPrice = 100000,
            Description = "Áp dụng cho mọi loại xe gắn máy hai bánh, xe máy điện và xe đạp điện."
        });

        PricingRules.Add(new PricingRuleItemViewModel
        {
            RuleId = 2,
            VehicleTypeName = "Ô tô con (4 - 7 chỗ)",
            VehicleIcon = "🚗",
            FirstBlockMinutes = 120,
            FirstBlockPrice = 25000,
            AdditionalPricePerHour = 10000,
            OvernightPrice = 70000,
            MonthlyPassPrice = 1200000,
            Description = "Áp dụng cho xe du lịch, xe con gia đình từ 4 đến 7 chỗ ngồi có đăng ký gửi bãi."
        });

        PricingRules.Add(new PricingRuleItemViewModel
        {
            RuleId = 3,
            VehicleTypeName = "Xe tải & Xe khách (>16 chỗ)",
            VehicleIcon = "🚚",
            FirstBlockMinutes = 120,
            FirstBlockPrice = 40000,
            AdditionalPricePerHour = 15000,
            OvernightPrice = 120000,
            MonthlyPassPrice = 2000000,
            Description = "Áp dụng cho xe tải chở hàng, xe bán tải trọng tải lớn và xe khách chở đoàn."
        });

        SelectedRule = PricingRules.FirstOrDefault();
    }

    public Task LoadDataAsync()
    {
        // Có thể mở rộng lấy từ DbContext hoặc Service
        return Task.CompletedTask;
    }
}
