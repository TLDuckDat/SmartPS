using SmartPS.ViewModels;

namespace SmartPS.Models.Navigation;

/// <summary>Một menu cấp 1 trên thanh Menu (Nghiệp vụ, Danh mục, Báo cáo, Quản trị); ẩn khi không còn mục nào được phép.</summary>
public class NavigationMenuGroup : ViewModelBase
{
    public NavigationMenuGroup(string categoryKey, IEnumerable<NavigationMenuItem> items)
    {
        CategoryKey = categoryKey;
        Items = items.Where(i => i.CategoryKey == categoryKey).ToList();
    }

    public string CategoryKey { get; }

    public IReadOnlyList<NavigationMenuItem> Items { get; }

    private bool _isVisible;
    public bool IsVisible
    {
        get => _isVisible;
        private set => SetProperty(ref _isVisible, value);
    }

    public void RefreshVisibility() => IsVisible = Items.Any(i => i.IsVisible);
}
