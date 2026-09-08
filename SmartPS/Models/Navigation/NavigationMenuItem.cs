using SmartPS.ViewModels;

namespace SmartPS.Models.Navigation;

public enum NavigationItemType
{
    Overview,
    GateControl,
    ParkingMap,
    Customers,
    Reports,
    Incidents,
    Transactions,
    UserManagement,
    Pricing,
    Settings
}

public class NavigationMenuItem : ViewModelBase
{
    public NavigationItemType Id { get; init; }
    public string TitleKey { get; init; } = string.Empty;
    public string CategoryKey { get; init; } = string.Empty;
    public string IconData { get; init; } = string.Empty;
    public string[] AllowedRoles { get; init; } = Array.Empty<string>();

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

