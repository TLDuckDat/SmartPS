using System.Collections.ObjectModel;
using System.Text.Json;
using SmartPS.Constants;
using SmartPS.Data;
using SmartPS.Models.Audit;
using SmartPS.Services.Audit;
using SmartPS.Services.Authorization;
using SmartPS.Services.Dialog;
using SmartPS.Services.Localization;

namespace SmartPS.ViewModels.Audit;

/// <summary>Read-only audit log viewer: filter, page, inspect details and verify the hash chain.</summary>
public class AuditLogViewModel : ViewModelBase
{
    private readonly IAuditQueryService _queryService;
    private readonly IAuditIntegrityVerifier _verifier;
    private readonly IPermissionService _permissionService;
    private readonly IDialogService _dialogService;
    private readonly ILocalizationService _localizationService;

    private static readonly JsonSerializerOptions PrettyJson = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public sealed record FilterOption(string? Value, string Label);

    public ObservableCollection<AuditLog> Items { get; } = new();
    public ObservableCollection<FilterOption> ActionOptions { get; } = new();
    public ObservableCollection<FilterOption> OutcomeOptions { get; } = new();

    private DateTime? _fromDate;
    public DateTime? FromDate
    {
        get => _fromDate;
        set => SetProperty(ref _fromDate, value);
    }

    private DateTime? _toDate;
    public DateTime? ToDate
    {
        get => _toDate;
        set => SetProperty(ref _toDate, value);
    }

    private string _usernameFilter = string.Empty;
    public string UsernameFilter
    {
        get => _usernameFilter;
        set => SetProperty(ref _usernameFilter, value);
    }

    private FilterOption? _selectedAction;
    public FilterOption? SelectedAction
    {
        get => _selectedAction;
        set => SetProperty(ref _selectedAction, value);
    }

    private FilterOption? _selectedOutcome;
    public FilterOption? SelectedOutcome
    {
        get => _selectedOutcome;
        set => SetProperty(ref _selectedOutcome, value);
    }

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value);
    }

    private AuditLog? _selectedEntry;
    public AuditLog? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (SetProperty(ref _selectedEntry, value))
            {
                OnPropertyChanged(nameof(SelectedDetailsText));
            }
        }
    }

    /// <summary>Pretty-printed Details JSON of the selected record.</summary>
    public string SelectedDetailsText => FormatDetails(SelectedEntry);

    private int _pageIndex;
    public int PageIndex
    {
        get => _pageIndex;
        private set
        {
            if (SetProperty(ref _pageIndex, value))
            {
                RaisePagingChanged();
            }
        }
    }

    private int _totalCount;
    public int TotalCount
    {
        get => _totalCount;
        private set
        {
            if (SetProperty(ref _totalCount, value))
            {
                RaisePagingChanged();
            }
        }
    }

    private int _totalPages;
    public int TotalPages
    {
        get => _totalPages;
        private set
        {
            if (SetProperty(ref _totalPages, value))
            {
                RaisePagingChanged();
            }
        }
    }

    public string PageInfoText => _localizationService.GetString(
        "Str_Audit_PageInfo", Math.Min(PageIndex + 1, Math.Max(TotalPages, 1)), Math.Max(TotalPages, 1), TotalCount);

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaiseCommandsChanged();
            }
        }
    }

    public bool CanVerify => _permissionService.HasPermission(Permissions.AuditVerify);

    public AsyncRelayCommand ApplyFilterCommand { get; }
    public AsyncRelayCommand ClearFilterCommand { get; }
    public AsyncRelayCommand PrevPageCommand { get; }
    public AsyncRelayCommand NextPageCommand { get; }
    public AsyncRelayCommand VerifyCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }

    public AuditLogViewModel(
        IAuditQueryService queryService,
        IAuditIntegrityVerifier verifier,
        IPermissionService permissionService,
        IDialogService dialogService,
        ILocalizationService localizationService)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        _permissionService = permissionService ?? throw new ArgumentNullException(nameof(permissionService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));

        BuildFilterOptions();
        ResetFilterValues();

        ApplyFilterCommand = new AsyncRelayCommand(() => LoadPageAsync(0), () => !IsBusy);
        ClearFilterCommand = new AsyncRelayCommand(ClearFilterAsync, () => !IsBusy);
        PrevPageCommand = new AsyncRelayCommand(() => LoadPageAsync(PageIndex - 1), () => !IsBusy && PageIndex > 0);
        NextPageCommand = new AsyncRelayCommand(() => LoadPageAsync(PageIndex + 1), () => !IsBusy && PageIndex + 1 < TotalPages);
        VerifyCommand = new AsyncRelayCommand(VerifyAsync, () => !IsBusy && CanVerify);
        RefreshCommand = new AsyncRelayCommand(() => LoadPageAsync(PageIndex), () => !IsBusy);

        _localizationService.LanguageChanged += () =>
        {
            void Update()
            {
                var action = SelectedAction?.Value;
                var outcome = SelectedOutcome?.Value;
                BuildFilterOptions();
                SelectedAction = ActionOptions.FirstOrDefault(o => o.Value == action) ?? ActionOptions[0];
                SelectedOutcome = OutcomeOptions.FirstOrDefault(o => o.Value == outcome) ?? OutcomeOptions[0];
                OnPropertyChanged(nameof(PageInfoText));
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

    public Task LoadDataAsync() => LoadPageAsync(0);

    private void BuildFilterOptions()
    {
        ActionOptions.Clear();
        ActionOptions.Add(new FilterOption(null, _localizationService.GetString("Str_Audit_All")));
        foreach (var action in AuditActions.All)
        {
            ActionOptions.Add(new FilterOption(action, action));
        }

        OutcomeOptions.Clear();
        OutcomeOptions.Add(new FilterOption(null, _localizationService.GetString("Str_Audit_All")));
        foreach (var outcome in Enum.GetValues<AuditOutcome>())
        {
            OutcomeOptions.Add(new FilterOption(outcome.ToString(), _localizationService.GetString($"Str_Audit_Outcome_{outcome}")));
        }
    }

    /// <summary>Default filter: today in Vietnam time.</summary>
    private void ResetFilterValues()
    {
        var today = AuditTime.ToVietnamTime(DateTime.UtcNow).Date;
        FromDate = today;
        ToDate = today;
        UsernameFilter = string.Empty;
        SearchText = string.Empty;
        SelectedAction = ActionOptions[0];
        SelectedOutcome = OutcomeOptions[0];
    }

    private Task ClearFilterAsync()
    {
        ResetFilterValues();
        return LoadPageAsync(0);
    }

    private AuditQueryFilter BuildFilter(int pageIndex)
    {
        AuditOutcome? outcome = Enum.TryParse<AuditOutcome>(SelectedOutcome?.Value, out var parsed) ? parsed : null;
        return new AuditQueryFilter
        {
            FromDateVn = FromDate.HasValue ? DateOnly.FromDateTime(FromDate.Value) : null,
            ToDateVn = ToDate.HasValue ? DateOnly.FromDateTime(ToDate.Value) : null,
            Username = string.IsNullOrWhiteSpace(UsernameFilter) ? null : UsernameFilter.Trim(),
            Action = SelectedAction?.Value,
            Outcome = outcome,
            SearchText = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
            PageIndex = Math.Max(0, pageIndex),
            PageSize = AuditQueryFilter.DefaultPageSize
        };
    }

    private async Task LoadPageAsync(int pageIndex)
    {
        try
        {
            IsBusy = true;
            var page = await _queryService.QueryAsync(BuildFilter(pageIndex));

            Items.Clear();
            foreach (var item in page.Items)
            {
                Items.Add(item);
            }

            SelectedEntry = null;
            TotalCount = page.TotalCount;
            TotalPages = page.TotalPages;
            PageIndex = page.PageIndex;
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
                _dialogService.ShowError(_localizationService.GetString("Msg_Audit_LoadError", ex.Message));
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task VerifyAsync()
    {
        try
        {
            IsBusy = true;
            var result = await _verifier.VerifyAsync();
            if (result.IsValid)
            {
                _dialogService.ShowSuccess(_localizationService.GetString("Msg_Audit_VerifyOk", result.CheckedCount));
            }
            else
            {
                _dialogService.ShowError(_localizationService.GetString(
                    "Msg_Audit_VerifyBroken", result.FirstInvalidAuditLogId?.ToString() ?? "?", result.FailureReason?.ToString() ?? string.Empty));
            }

            IsBusy = false;
            await LoadPageAsync(0);
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
                _dialogService.ShowError(_localizationService.GetString("Msg_Audit_VerifyError", ex.Message));
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string FormatDetails(AuditLog? entry)
    {
        if (entry is null || string.IsNullOrWhiteSpace(entry.Details))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(entry.Details);
            return JsonSerializer.Serialize(document.RootElement, PrettyJson);
        }
        catch (JsonException)
        {
            return entry.Details;
        }
    }

    private void RaisePagingChanged()
    {
        OnPropertyChanged(nameof(PageInfoText));
        RaiseCommandsChanged();
    }

    private void RaiseCommandsChanged()
    {
        ApplyFilterCommand?.RaiseCanExecuteChanged();
        PrevPageCommand?.RaiseCanExecuteChanged();
        NextPageCommand?.RaiseCanExecuteChanged();
        VerifyCommand?.RaiseCanExecuteChanged();
    }
}
