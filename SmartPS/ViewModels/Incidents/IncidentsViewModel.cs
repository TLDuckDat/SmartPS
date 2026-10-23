using System.Collections.ObjectModel;
using SmartPS.Constants;
using SmartPS.Models.Incidents;
using SmartPS.Services.Authorization;
using SmartPS.Services.Common;
using SmartPS.Services.Dialog;
using SmartPS.Services.Incidents;

namespace SmartPS.ViewModels.Incidents;

public sealed record IncidentTypeOption(IncidentType Type, string Name);

public class IncidentsViewModel : ViewModelBase
{
    public static readonly string[] StatusFilters = { "Tất cả trạng thái", IncidentItemViewModel.OpenStatusText, IncidentItemViewModel.ResolvedStatusText };

    private readonly IIncidentService _incidentService;
    private readonly IPermissionService _permissionService;
    private readonly IDialogService _dialogService;
    private List<IncidentItemViewModel> _allIncidents = new();

    private int _totalIncidents;
    public int TotalIncidents
    {
        get => _totalIncidents;
        set => SetProperty(ref _totalIncidents, value);
    }

    private int _pendingIncidents;
    public int PendingIncidents
    {
        get => _pendingIncidents;
        set => SetProperty(ref _pendingIncidents, value);
    }

    private int _resolvedIncidents;
    public int ResolvedIncidents
    {
        get => _resolvedIncidents;
        set => SetProperty(ref _resolvedIncidents, value);
    }

    private string _searchKeyword = string.Empty;
    public string SearchKeyword
    {
        get => _searchKeyword;
        set
        {
            if (SetProperty(ref _searchKeyword, value))
            {
                ApplyFilter();
            }
        }
    }

    public IReadOnlyList<string> StatusFilterOptions => StatusFilters;

    private int _selectedStatusFilterIndex;
    public int SelectedStatusFilterIndex
    {
        get => _selectedStatusFilterIndex;
        set
        {
            if (SetProperty(ref _selectedStatusFilterIndex, value))
            {
                ApplyFilter();
            }
        }
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public ObservableCollection<IncidentItemViewModel> FilteredIncidents { get; } = new();

    private IncidentItemViewModel? _selectedIncident;
    public IncidentItemViewModel? SelectedIncident
    {
        get => _selectedIncident;
        set => SetProperty(ref _selectedIncident, value);
    }

    public bool CanReport => _permissionService.HasPermission(Permissions.IncidentManage) ||
                             _permissionService.HasPermission(Permissions.ParkingCheckIn) ||
                             _permissionService.HasPermission(Permissions.ParkingCheckOut);

    public bool CanManage => _permissionService.HasPermission(Permissions.IncidentManage);

    #region Lập biên bản

    public IReadOnlyList<IncidentTypeOption> TypeOptions { get; } = IncidentItemViewModel.TypeNames
        .Select(kv => new IncidentTypeOption(kv.Key, kv.Value))
        .ToList();

    private bool _isReportFormOpen;
    public bool IsReportFormOpen
    {
        get => _isReportFormOpen;
        set => SetProperty(ref _isReportFormOpen, value);
    }

    private IncidentTypeOption? _newType;
    public IncidentTypeOption? NewType
    {
        get => _newType;
        set => SetProperty(ref _newType, value);
    }

    private string _newLicensePlate = string.Empty;
    public string NewLicensePlate
    {
        get => _newLicensePlate;
        set => SetProperty(ref _newLicensePlate, value);
    }

    private string _newDescription = string.Empty;
    public string NewDescription
    {
        get => _newDescription;
        set => SetProperty(ref _newDescription, value);
    }

    #endregion

    #region Xử lý biên bản

    private IncidentItemViewModel? _resolvingIncident;
    public IncidentItemViewModel? ResolvingIncident
    {
        get => _resolvingIncident;
        set
        {
            if (SetProperty(ref _resolvingIncident, value))
            {
                OnPropertyChanged(nameof(IsResolvePanelOpen));
            }
        }
    }

    public bool IsResolvePanelOpen => ResolvingIncident != null;

    private string _resolutionText = string.Empty;
    public string ResolutionText
    {
        get => _resolutionText;
        set => SetProperty(ref _resolutionText, value);
    }

    #endregion

    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand OpenReportFormCommand { get; }
    public RelayCommand CancelReportCommand { get; }
    public AsyncRelayCommand SubmitReportCommand { get; }
    public RelayCommand<IncidentItemViewModel> ResolveCommand { get; }
    public RelayCommand CancelResolveCommand { get; }
    public AsyncRelayCommand ConfirmResolveCommand { get; }
    public AsyncRelayCommand ScanOverstayCommand { get; }

    public IncidentsViewModel(IIncidentService incidentService, IPermissionService permissionService, IDialogService dialogService)
    {
        _incidentService = incidentService ?? throw new ArgumentNullException(nameof(incidentService));
        _permissionService = permissionService ?? throw new ArgumentNullException(nameof(permissionService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

        RefreshCommand = new AsyncRelayCommand(LoadDataAsync);
        OpenReportFormCommand = new RelayCommand(OpenReportForm, () => CanReport);
        CancelReportCommand = new RelayCommand(() => IsReportFormOpen = false);
        SubmitReportCommand = new AsyncRelayCommand(SubmitReportAsync, () => CanReport && !IsBusy);
        ResolveCommand = new RelayCommand<IncidentItemViewModel>(item =>
        {
            if (item == null || item.IsResolved) return;
            ResolutionText = string.Empty;
            ResolvingIncident = item;
        }, item => CanManage && item is { IsResolved: false });
        CancelResolveCommand = new RelayCommand(() => ResolvingIncident = null);
        ConfirmResolveCommand = new AsyncRelayCommand(ConfirmResolveAsync, () => CanManage && !IsBusy);
        ScanOverstayCommand = new AsyncRelayCommand(ScanOverstayAsync, () => CanReport && !IsBusy);

        _ = LoadDataAsync();
    }

    public async Task LoadDataAsync()
    {
        var incidents = await _incidentService.GetIncidentsAsync();
        _allIncidents = incidents.Select(IncidentItemViewModel.FromInfo).ToList();

        TotalIncidents = _allIncidents.Count;
        PendingIncidents = _allIncidents.Count(i => !i.IsResolved);
        ResolvedIncidents = _allIncidents.Count(i => i.IsResolved);

        var selectedId = SelectedIncident?.IncidentId;
        ApplyFilter();
        SelectedIncident = FilteredIncidents.FirstOrDefault(i => i.IncidentId == selectedId);
    }

    private void ApplyFilter()
    {
        var kw = SearchKeyword?.Trim().ToUpperInvariant() ?? string.Empty;
        FilteredIncidents.Clear();

        foreach (var item in _allIncidents)
        {
            var statusMatch = SelectedStatusFilterIndex switch
            {
                1 => !item.IsResolved,
                2 => item.IsResolved,
                _ => true
            };

            var keywordMatch = string.IsNullOrEmpty(kw) ||
                               item.IncidentCode.ToUpperInvariant().Contains(kw) ||
                               item.LicensePlate.ToUpperInvariant().Contains(kw) ||
                               item.IncidentType.ToUpperInvariant().Contains(kw) ||
                               item.ReportedBy.ToUpperInvariant().Contains(kw);

            if (statusMatch && keywordMatch)
            {
                FilteredIncidents.Add(item);
            }
        }
    }

    private void OpenReportForm()
    {
        NewType = TypeOptions.FirstOrDefault();
        NewLicensePlate = string.Empty;
        NewDescription = string.Empty;
        IsReportFormOpen = true;
    }

    private async Task SubmitReportAsync()
    {
        if (NewType == null)
        {
            _dialogService.ShowWarning("Vui lòng chọn loại sự cố.");
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _incidentService.ReportAsync(NewType.Type, NewLicensePlate, NewDescription);
            if (!ShowFailure(result, "Không thể lập biên bản sự cố"))
            {
                IsReportFormOpen = false;
                _dialogService.ShowSuccess($"Đã lập biên bản {result.Message}.");
                await LoadDataAsync();
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ConfirmResolveAsync()
    {
        var item = ResolvingIncident;
        if (item == null) return;

        IsBusy = true;
        try
        {
            var result = await _incidentService.ResolveAsync(item.IncidentId, ResolutionText);
            if (!ShowFailure(result, "Không thể xử lý biên bản"))
            {
                ResolvingIncident = null;
                _dialogService.ShowSuccess($"Đã đóng biên bản {result.Message}.");
                await LoadDataAsync();
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ScanOverstayAsync()
    {
        IsBusy = true;
        try
        {
            var result = await _incidentService.ScanOverstayAsync();
            if (!ShowFailure(result, "Không thể quét xe quá hạn"))
            {
                var created = int.TryParse(result.Message, out var n) ? n : 0;
                _dialogService.ShowInfo(created == 0
                    ? "Không có xe gửi quá hạn mới cần lập biên bản."
                    : $"Đã lập {created} biên bản xe gửi quá hạn.");
                await LoadDataAsync();
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Hiển thị lỗi của thao tác; trả về true nếu thao tác thất bại.</summary>
    private bool ShowFailure(OperationResult result, string title)
    {
        if (result.Success) return false;

        if (result.IsPermissionDenied)
            _dialogService.ShowWarning("Bạn không có quyền thực hiện thao tác này.", title);
        else if (result.Error == OperationError.Validation || result.Error == OperationError.NotFound)
            _dialogService.ShowWarning(result.Message, title);
        else
            _dialogService.ShowError($"{title}: {result.Message}", title);

        return true;
    }
}
