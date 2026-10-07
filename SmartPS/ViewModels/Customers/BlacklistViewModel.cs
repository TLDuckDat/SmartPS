using System.Collections.ObjectModel;
using SmartPS.Constants;
using SmartPS.Services.Authorization;
using SmartPS.Services.Customers;
using SmartPS.Services.Dialog;
using SmartPS.Services.Localization;

namespace SmartPS.ViewModels.Customers;

/// <summary>Tab danh sách đen biển số: xem cần Customer.View, thêm và gỡ cần Blacklist.Manage.</summary>
public class BlacklistViewModel : ViewModelBase
{
    private readonly IBlacklistService _blacklist;
    private readonly IPermissionService _permissions;
    private readonly IDialogService _dialog;
    private readonly ILocalizationService _localization;

    public BlacklistViewModel(
        IBlacklistService blacklist,
        IPermissionService permissions,
        IDialogService dialogService,
        ILocalizationService localization)
    {
        _blacklist = blacklist ?? throw new ArgumentNullException(nameof(blacklist));
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _dialog = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));

        AddCommand = new AsyncRelayCommand(AddAsync, CanAdd);
        RemoveCommand = new AsyncRelayCommand(RemoveAsync, CanRemove);
    }

    public bool CanManageBlacklist => _permissions.HasPermission(Permissions.BlacklistManage);

    public ObservableCollection<BlacklistEntryDto> Entries { get; } = new();

    public AsyncRelayCommand AddCommand { get; }

    public AsyncRelayCommand RemoveCommand { get; }

    private bool _includeHistory;
    public bool IncludeHistory
    {
        get => _includeHistory;
        set
        {
            if (SetProperty(ref _includeHistory, value))
            {
                _ = LoadAsync();
            }
        }
    }

    private string _newPlate = string.Empty;
    public string NewPlate
    {
        get => _newPlate;
        set
        {
            if (SetProperty(ref _newPlate, value ?? string.Empty))
            {
                AddCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private string _newReason = string.Empty;
    public string NewReason
    {
        get => _newReason;
        set
        {
            if (SetProperty(ref _newReason, value ?? string.Empty))
            {
                AddCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private string _removeReason = string.Empty;
    public string RemoveReason
    {
        get => _removeReason;
        set
        {
            if (SetProperty(ref _removeReason, value ?? string.Empty))
            {
                RemoveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private BlacklistEntryDto? _selectedEntry;
    public BlacklistEntryDto? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (SetProperty(ref _selectedEntry, value))
            {
                RemoveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public async Task LoadAsync()
    {
        try
        {
            var entries = await _blacklist.GetEntriesAsync(new BlacklistQuery(IncludeHistory));
            Entries.Clear();
            foreach (var entry in entries)
            {
                Entries.Add(entry);
            }
        }
        catch (PermissionDeniedException)
        {
            _dialog.ShowWarning(_localization.GetString("Msg_Auth_PermissionDenied"));
        }
        catch (Exception ex)
        {
            _dialog.ShowError(_localization.GetString("Msg_Cust_LoadError", ex.Message));
        }
    }

    private bool CanAdd()
        => CanManageBlacklist && !string.IsNullOrWhiteSpace(NewPlate) && !string.IsNullOrWhiteSpace(NewReason);

    private bool CanRemove()
        => CanManageBlacklist && SelectedEntry is { IsActive: true } && !string.IsNullOrWhiteSpace(RemoveReason);

    private async Task AddAsync()
    {
        var result = await _blacklist.AddAsync(NewPlate, NewReason);
        if (!result.Success)
        {
            _dialog.ShowError(OperationMessages.Describe(_localization, result));
            return;
        }

        _dialog.ShowSuccess(_localization.GetString("Msg_Blacklist_Added"));
        NewPlate = string.Empty;
        NewReason = string.Empty;
        await LoadAsync();
    }

    private async Task RemoveAsync()
    {
        if (SelectedEntry is not { IsActive: true } entry)
        {
            return;
        }

        var result = await _blacklist.RemoveAsync(entry.BlacklistEntryId, RemoveReason);
        if (!result.Success)
        {
            _dialog.ShowError(OperationMessages.Describe(_localization, result));
            return;
        }

        _dialog.ShowSuccess(_localization.GetString("Msg_Blacklist_Removed"));
        RemoveReason = string.Empty;
        SelectedEntry = null;
        await LoadAsync();
    }
}
