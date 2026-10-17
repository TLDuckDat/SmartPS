using SmartPS.Services.RolePermissions;

namespace SmartPS.ViewModels.RolePermissions;

/// <summary>One checkbox of the role x permission matrix.</summary>
public class RolePermissionCellViewModel : ViewModelBase
{
    private readonly Action _onChanged;
    private bool _isGranted;

    public RolePermissionCellViewModel(RoleInfo role, string permission, bool isGranted, bool isEditable, Action onChanged)
    {
        Role = role;
        Permission = permission;
        OriginalGranted = isGranted;
        IsEditable = isEditable;
        _isGranted = isGranted;
        _onChanged = onChanged;
    }

    public RoleInfo Role { get; }

    public string Permission { get; }

    public bool OriginalGranted { get; }

    /// <summary>False for the built-in Admin role and for users who may only view the matrix.</summary>
    public bool IsEditable { get; }

    public bool IsGranted
    {
        get => _isGranted;
        set
        {
            if (!IsEditable)
            {
                return;
            }

            if (SetProperty(ref _isGranted, value))
            {
                OnPropertyChanged(nameof(IsChanged));
                _onChanged();
            }
        }
    }

    public bool IsChanged => _isGranted != OriginalGranted;

    public void Reset()
    {
        if (_isGranted == OriginalGranted)
        {
            return;
        }

        _isGranted = OriginalGranted;
        OnPropertyChanged(nameof(IsGranted));
        OnPropertyChanged(nameof(IsChanged));
    }
}
