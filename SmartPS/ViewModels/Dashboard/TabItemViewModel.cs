using SmartPS.Models.Navigation;

namespace SmartPS.ViewModels.Dashboard;

/// <summary>Một thẻ (tab) đang mở trong vùng làm việc của cửa sổ chính.</summary>
public class TabItemViewModel : ViewModelBase
{
    public TabItemViewModel(NavigationMenuItem item, ViewModelBase content)
    {
        Item = item ?? throw new ArgumentNullException(nameof(item));
        Content = content ?? throw new ArgumentNullException(nameof(content));
    }

    public NavigationMenuItem Item { get; }

    public ViewModelBase Content { get; }
}
