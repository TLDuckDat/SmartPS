using SmartPS.Models.Shifts;
using SmartPS.ViewModels;

namespace SmartPS.ViewModels.Shifts;

public class ShiftUserFilterOption : ViewModelBase
{
    public int? UserId { get; init; }

    private string _displayName = string.Empty;
    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
    }
}

public class ShiftStatusFilterOption : ViewModelBase
{
    public ShiftStatus? Status { get; init; }

    private string _displayName = string.Empty;
    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
    }
}
