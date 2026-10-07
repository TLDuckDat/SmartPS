using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using SmartPS.Constants;
using SmartPS.Data;
using SmartPS.Services.Authorization;
using SmartPS.Services.Dialog;
using SmartPS.Services.Localization;
using SmartPS.Services.RolePermissions;

namespace SmartPS.ViewModels.RolePermissions;

public class RolePermissionsViewModel : ViewModelBase
{
    private readonly IRolePermissionService _rolePermissionService;
    private readonly IPermissionService _permissionService;
    private readonly IDialogService _dialogService;
    private readonly ILocalizationService _localizationService;

    public ObservableCollection<RoleInfo> Roles { get; } = new();

    public ObservableCollection<PermissionRowViewModel> Rows { get; } = new();

    /// <summary>Rows grouped by permission module (User, Role, Parking, ...).</summary>
    public ICollectionView RowsView { get; }

    public AsyncRelayCommand SaveCommand { get; }
    public RelayCommand ResetCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                SaveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private bool _hasChanges;
    public bool HasChanges
    {
        get => _hasChanges;
        private set
        {
            if (SetProperty(ref _hasChanges, value))
            {
                SaveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>Only users with Role.Manage may change the matrix; Role.View users see it read-only.</summary>
    public bool CanEdit => _permissionService.HasPermission(Permissions.RoleManage);

    public bool IsReadOnly => !CanEdit;

    public RolePermissionsViewModel(
        IRolePermissionService rolePermissionService,
        IPermissionService permissionService,
        IDialogService dialogService,
        ILocalizationService localizationService)
    {
        _rolePermissionService = rolePermissionService ?? throw new ArgumentNullException(nameof(rolePermissionService));
        _permissionService = permissionService ?? throw new ArgumentNullException(nameof(permissionService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));

        RowsView = CollectionViewSource.GetDefaultView(Rows);
        RowsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(PermissionRowViewModel.Module)));

        SaveCommand = new AsyncRelayCommand(SaveAsync, () => CanEdit && HasChanges && !IsBusy);
        ResetCommand = new RelayCommand(ResetChanges);
        RefreshCommand = new AsyncRelayCommand(LoadDataAsync);

        _localizationService.LanguageChanged += () =>
        {
            void Update()
            {
                foreach (var row in Rows)
                {
                    row.RaiseLocalizedPropertiesChanged();
                }
            }

            if (System.Windows.Application.Current?.Dispatcher.CheckAccess() == false)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(Update);
            }
            else
            {
                Update();
            }
        };
    }

    public async Task LoadDataAsync()
    {
        try
        {
            IsBusy = true;
            var matrix = await _rolePermissionService.GetMatrixAsync();
            var canEdit = CanEdit;

            Roles.Clear();
            Rows.Clear();
            foreach (var role in matrix.Roles)
            {
                Roles.Add(role);
            }

            foreach (var permission in matrix.Permissions)
            {
                var cells = new List<RolePermissionCellViewModel>();
                foreach (var role in matrix.Roles)
                {
                    var granted = matrix.GrantsByRoleId.TryGetValue(role.RoleId, out var set) && set.Contains(permission.Name);
                    cells.Add(new RolePermissionCellViewModel(role, permission.Name, granted, canEdit && !role.IsSystemAdmin, UpdateHasChanges));
                }

                Rows.Add(new PermissionRowViewModel(permission, cells, _localizationService));
            }

            UpdateHasChanges();
            OnPropertyChanged(nameof(CanEdit));
            OnPropertyChanged(nameof(IsReadOnly));
        }
        catch (PermissionDeniedException)
        {
            _dialogService.ShowWarning(_localizationService.GetString("Msg_Auth_PermissionDenied"));
        }
        catch (Exception ex)
        {
            if (DbConnectionHelper.IsConnectionException(ex))
            {
                _dialogService.ShowError(_localizationService.GetString("Msg_Db_ConnectionLost"));
            }
            else
            {
                _dialogService.ShowError(_localizationService.GetString("Msg_RolePerm_LoadError", ex.Message));
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SaveAsync()
    {
        try
        {
            IsBusy = true;

            var desired = new Dictionary<int, IReadOnlyCollection<string>>();
            foreach (var role in Roles.Where(r => !r.IsSystemAdmin))
            {
                desired[role.RoleId] = Rows
                    .SelectMany(row => row.Cells)
                    .Where(cell => cell.Role.RoleId == role.RoleId && cell.IsGranted)
                    .Select(cell => cell.Permission)
                    .ToList();
            }

            var changes = await _rolePermissionService.SaveAsync(desired);
            if (changes.Count == 0)
            {
                _dialogService.ShowInfo(_localizationService.GetString("Msg_RolePerm_NoChanges"));
            }
            else
            {
                _dialogService.ShowSuccess(_localizationService.GetString("Msg_RolePerm_SaveSuccess", changes.Count));
            }

            IsBusy = false;
            await LoadDataAsync();
        }
        catch (PermissionDeniedException)
        {
            _dialogService.ShowWarning(_localizationService.GetString("Msg_Auth_PermissionDenied"));
        }
        catch (Exception ex)
        {
            if (DbConnectionHelper.IsConnectionException(ex))
            {
                _dialogService.ShowError(_localizationService.GetString("Msg_Db_ConnectionLost"));
            }
            else
            {
                _dialogService.ShowError(_localizationService.GetString("Msg_RolePerm_SaveError", ex.Message));
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ResetChanges()
    {
        foreach (var cell in Rows.SelectMany(row => row.Cells))
        {
            cell.Reset();
        }

        UpdateHasChanges();
    }

    private void UpdateHasChanges()
    {
        HasChanges = Rows.Any(row => row.Cells.Any(cell => cell.IsChanged));
    }
}
