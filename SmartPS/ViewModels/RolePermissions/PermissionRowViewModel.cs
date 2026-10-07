using SmartPS.Services.Localization;
using SmartPS.Services.RolePermissions;

namespace SmartPS.ViewModels.RolePermissions;

/// <summary>One permission with its grant state for every role.</summary>
public class PermissionRowViewModel : ViewModelBase
{
    private readonly ILocalizationService _localizationService;

    public PermissionRowViewModel(
        PermissionInfo permission,
        IReadOnlyList<RolePermissionCellViewModel> cells,
        ILocalizationService localizationService)
    {
        Permission = permission;
        Cells = cells;
        _localizationService = localizationService;
    }

    public PermissionInfo Permission { get; }

    public IReadOnlyList<RolePermissionCellViewModel> Cells { get; }

    /// <summary>Module key, used for grouping.</summary>
    public string Module => Permission.Module;

    public string Name => Permission.Name;

    public string ModuleTitle => _localizationService.GetString($"Str_Perm_Module_{Permission.Module}");

    public string Title => _localizationService.GetString($"Str_Perm_{Permission.Name.Replace('.', '_')}");

    public void RaiseLocalizedPropertiesChanged()
    {
        OnPropertyChanged(nameof(ModuleTitle));
        OnPropertyChanged(nameof(Title));
    }
}
