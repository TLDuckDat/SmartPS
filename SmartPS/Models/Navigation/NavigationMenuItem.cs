using SmartPS.ViewModels;

namespace SmartPS.Models.Navigation;

public class NavigationMenuItem : ViewModelBase
{
    public NavigationItemType Id { get; init; }
    public string TitleKey { get; init; } = string.Empty;
    public string CategoryKey { get; init; } = string.Empty;
    public string IconData { get; init; } = string.Empty;
    public IReadOnlyList<string> RequiredPermissions { get; init; } = Array.Empty<string>();

    /// <summary>Phím số của tổ hợp Ctrl+1…9 mở màn hình này; null nếu không có phím tắt.</summary>
    public int? ShortcutDigit { get; init; }

    public string ShortcutText => ShortcutDigit is int digit ? $"Ctrl+{digit}" : string.Empty;

    /// <summary>Mở phân hệ này (dùng chung cho Menu và ToolBar, không phụ thuộc DataContext của cửa sổ).</summary>
    public System.Windows.Input.ICommand? OpenCommand { get; set; }

    // Tên hiển thị theo ngôn ngữ hiện tại (menu, thanh công cụ, tiêu đề thẻ)
    private string _title = string.Empty;
    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    private bool _isVisible = true;
    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    private string? _badgeText;
    public string? BadgeText
    {
        get => _badgeText;
        set => SetProperty(ref _badgeText, value);
    }
}
