using System.Collections.ObjectModel;
using System.Globalization;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using SmartPS.Constants;
using SmartPS.Models.Reports;
using SmartPS.Services.Authorization;
using SmartPS.Services.Dialog;
using SmartPS.Services.Localization;
using SmartPS.Services.Reports;

namespace SmartPS.ViewModels.Reports;

public class ReportsViewModel : ViewModelBase
{
    private static readonly string[] TileKeys =
    {
        ReportTextKeys.KpiCheckIns, ReportTextKeys.KpiCheckOuts, ReportTextKeys.KpiInLotNow, ReportTextKeys.KpiNetRevenue,
        ReportTextKeys.KpiCash, ReportTextKeys.KpiVietQr, ReportTextKeys.KpiCard, ReportTextKeys.KpiFreeCheckOuts,
        ReportTextKeys.KpiAvgDuration, ReportTextKeys.KpiPeakHour, ReportTextKeys.KpiAvgOccupancy, ReportTextKeys.KpiPeakOccupancy,
        ReportTextKeys.KpiResidentShare, ReportTextKeys.KpiMonthlyTicketRevenue
    };

    private readonly IReportService _reportService;
    private readonly IReportExportService _exportService;
    private readonly IPermissionService _permissions;
    private readonly IDialogService _dialogService;
    private readonly IFileDialogService _fileDialogService;
    private readonly ILocalizationService _localization;
    private readonly TimeProvider _timeProvider;

    private CancellationTokenSource? _reloadCts;
    private int _loadVersion;
    private bool _suppressReload;
    private bool _optionsLoaded;
    private ReportFilterOptions? _options;

    private IReadOnlyList<ReportPresetOption> _presets;
    private ReportPresetOption _selectedPreset;
    private DateTime? _customFrom;
    private DateTime? _customTo;
    private LookupOption? _selectedVehicleType;
    private IReadOnlyList<CustomerGroupOption> _customerGroupOptions;
    private CustomerGroupOption _selectedCustomerGroup;
    private LookupOption? _selectedZone;

    private bool _isLoading;
    private bool _isExporting;
    private string? _errorMessage;
    private string _rangeDisplay = string.Empty;
    private string _previousRangeDisplay = string.Empty;
    private ReportResult? _currentResult;

    private ISeries[] _revenueSeries = Array.Empty<ISeries>();
    private Axis[] _revenueXAxes = Array.Empty<Axis>();
    private Axis[] _revenueYAxes = Array.Empty<Axis>();
    private bool _revenueHasData;
    private ISeries[] _hourlySeries = Array.Empty<ISeries>();
    private Axis[] _hourlyXAxes = Array.Empty<Axis>();
    private Axis[] _hourlyYAxes = Array.Empty<Axis>();
    private bool _hourlyHasData;
    private ISeries[] _dailyTrafficSeries = Array.Empty<ISeries>();
    private Axis[] _dailyTrafficXAxes = Array.Empty<Axis>();
    private Axis[] _dailyTrafficYAxes = Array.Empty<Axis>();
    private bool _dailyTrafficHasData;
    private ISeries[] _customerGroupSeries = Array.Empty<ISeries>();
    private bool _customerGroupHasData;
    private ISeries[] _zoneSeries = Array.Empty<ISeries>();
    private Axis[] _zoneXAxes = Array.Empty<Axis>();
    private Axis[] _zoneYAxes = Array.Empty<Axis>();
    private bool _zoneHasData;
    private ISeries[] _vehicleTypeSeries = Array.Empty<ISeries>();
    private bool _vehicleTypeHasData;

    public ReportsViewModel(
        IReportService reportService,
        IReportExportService exportService,
        IPermissionService permissions,
        IDialogService dialogService,
        IFileDialogService fileDialogService,
        ILocalizationService localization,
        TimeProvider? timeProvider = null)
    {
        _reportService = reportService ?? throw new ArgumentNullException(nameof(reportService));
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _fileDialogService = fileDialogService ?? throw new ArgumentNullException(nameof(fileDialogService));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _timeProvider = timeProvider ?? TimeProvider.System;

        DebounceDelay = TimeSpan.FromMilliseconds(ReportLimits.DebounceMilliseconds);
        DelayAsync = Task.Delay;

        _presets = BuildPresets();
        _selectedPreset = _presets.First(p => p.Preset == ReportPeriodPreset.Last7Days);
        _customerGroupOptions = BuildCustomerGroups();
        _selectedCustomerGroup = _customerGroupOptions[0];
        VehicleTypeOptions.Add(new LookupOption(null, L(ReportTextKeys.FilterAll)));
        _selectedVehicleType = VehicleTypeOptions[0];
        ZoneOptions.Add(new LookupOption(null, L(ReportTextKeys.FilterAll)));
        _selectedZone = ZoneOptions[0];

        foreach (var titleKey in TileKeys)
        {
            KpiTiles.Add(new KpiTileViewModel(titleKey, L(titleKey)));
        }

        RefreshCommand = new AsyncRelayCommand(LoadDataAsync);
        ExportCommand = new AsyncRelayCommand(ExportAsync, () => CanExport);

        _localization.LanguageChanged += OnLanguageChanged;
    }

    // ---- filters ---------------------------------------------------------------------------------------------------

    public TimeSpan DebounceDelay { get; set; }

    /// <summary>Awaited before a debounced reload; replaced by tests so no wall-clock time is involved.</summary>
    public Func<TimeSpan, CancellationToken, Task> DelayAsync { get; set; }

    public IReadOnlyList<ReportPresetOption> Presets
    {
        get => _presets;
        private set => SetProperty(ref _presets, value);
    }

    public ReportPresetOption SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (value is null || EqualityComparer<ReportPresetOption>.Default.Equals(_selectedPreset, value))
            {
                return;
            }

            var previous = _selectedPreset;
            _selectedPreset = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCustomRange));

            if (value.Preset == ReportPeriodPreset.Custom && (_customFrom is null || _customTo is null))
            {
                PrefillCustomRange(previous.Preset);
            }

            ScheduleReload();
        }
    }

    public DateTime? CustomFrom
    {
        get => _customFrom;
        set
        {
            if (SetProperty(ref _customFrom, value) && IsCustomRange)
            {
                ScheduleReload();
            }
        }
    }

    public DateTime? CustomTo
    {
        get => _customTo;
        set
        {
            if (SetProperty(ref _customTo, value) && IsCustomRange)
            {
                ScheduleReload();
            }
        }
    }

    public bool IsCustomRange => _selectedPreset.Preset == ReportPeriodPreset.Custom;

    public ObservableCollection<LookupOption> VehicleTypeOptions { get; } = new();

    public LookupOption? SelectedVehicleType
    {
        get => _selectedVehicleType;
        set
        {
            if (SetProperty(ref _selectedVehicleType, value))
            {
                ScheduleReload();
            }
        }
    }

    public IReadOnlyList<CustomerGroupOption> CustomerGroupOptions
    {
        get => _customerGroupOptions;
        private set => SetProperty(ref _customerGroupOptions, value);
    }

    public CustomerGroupOption SelectedCustomerGroup
    {
        get => _selectedCustomerGroup;
        set
        {
            if (value is not null && SetProperty(ref _selectedCustomerGroup, value))
            {
                ScheduleReload();
            }
        }
    }

    public ObservableCollection<LookupOption> ZoneOptions { get; } = new();

    public LookupOption? SelectedZone
    {
        get => _selectedZone;
        set
        {
            if (SetProperty(ref _selectedZone, value))
            {
                ScheduleReload();
            }
        }
    }

    // ---- state -----------------------------------------------------------------------------------------------------

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(CanExport));
                ExportCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsExporting
    {
        get => _isExporting;
        private set
        {
            if (SetProperty(ref _isExporting, value))
            {
                OnPropertyChanged(nameof(CanExport));
                ExportCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrEmpty(_errorMessage);

    public string RangeDisplay
    {
        get => _rangeDisplay;
        private set => SetProperty(ref _rangeDisplay, value);
    }

    public string PreviousRangeDisplay
    {
        get => _previousRangeDisplay;
        private set => SetProperty(ref _previousRangeDisplay, value);
    }

    public ReportResult? CurrentResult
    {
        get => _currentResult;
        private set
        {
            if (SetProperty(ref _currentResult, value))
            {
                OnPropertyChanged(nameof(CanExport));
                OnPropertyChanged(nameof(HasResult));
                ExportCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasResult => _currentResult is not null;

    public bool HasExportPermission => _permissions.HasPermission(Permissions.ReportExport);

    /// <summary>Tooltip of the disabled export button; null when the user may export.</summary>
    public string? ExportToolTip => HasExportPermission ? null : L("Msg_Auth_PermissionDenied");

    public bool CanExport => HasExportPermission && !IsExporting && !IsLoading && CurrentResult is not null;

    // ---- KPIs, charts, tables --------------------------------------------------------------------------------------

    public ObservableCollection<KpiTileViewModel> KpiTiles { get; } = new();

    public ISeries[] RevenueSeries { get => _revenueSeries; private set => SetProperty(ref _revenueSeries, value); }

    public Axis[] RevenueXAxes { get => _revenueXAxes; private set => SetProperty(ref _revenueXAxes, value); }

    public Axis[] RevenueYAxes { get => _revenueYAxes; private set => SetProperty(ref _revenueYAxes, value); }

    public bool RevenueHasData { get => _revenueHasData; private set => SetProperty(ref _revenueHasData, value); }

    public ISeries[] HourlySeries { get => _hourlySeries; private set => SetProperty(ref _hourlySeries, value); }

    public Axis[] HourlyXAxes { get => _hourlyXAxes; private set => SetProperty(ref _hourlyXAxes, value); }

    public Axis[] HourlyYAxes { get => _hourlyYAxes; private set => SetProperty(ref _hourlyYAxes, value); }

    public bool HourlyHasData { get => _hourlyHasData; private set => SetProperty(ref _hourlyHasData, value); }

    public ISeries[] DailyTrafficSeries { get => _dailyTrafficSeries; private set => SetProperty(ref _dailyTrafficSeries, value); }

    public Axis[] DailyTrafficXAxes { get => _dailyTrafficXAxes; private set => SetProperty(ref _dailyTrafficXAxes, value); }

    public Axis[] DailyTrafficYAxes { get => _dailyTrafficYAxes; private set => SetProperty(ref _dailyTrafficYAxes, value); }

    public bool DailyTrafficHasData { get => _dailyTrafficHasData; private set => SetProperty(ref _dailyTrafficHasData, value); }

    public ISeries[] CustomerGroupSeries { get => _customerGroupSeries; private set => SetProperty(ref _customerGroupSeries, value); }

    public bool CustomerGroupHasData { get => _customerGroupHasData; private set => SetProperty(ref _customerGroupHasData, value); }

    public ISeries[] ZoneSeries { get => _zoneSeries; private set => SetProperty(ref _zoneSeries, value); }

    /// <summary>Row series: the X axis holds the values.</summary>
    public Axis[] ZoneXAxes { get => _zoneXAxes; private set => SetProperty(ref _zoneXAxes, value); }

    /// <summary>Row series: the Y axis holds the zone names.</summary>
    public Axis[] ZoneYAxes { get => _zoneYAxes; private set => SetProperty(ref _zoneYAxes, value); }

    public bool ZoneHasData { get => _zoneHasData; private set => SetProperty(ref _zoneHasData, value); }

    public ISeries[] VehicleTypeSeries { get => _vehicleTypeSeries; private set => SetProperty(ref _vehicleTypeSeries, value); }

    public bool VehicleTypeHasData { get => _vehicleTypeHasData; private set => SetProperty(ref _vehicleTypeHasData, value); }

    public ObservableCollection<DailyReportRow> DailyRows { get; } = new();

    public ObservableCollection<ShiftReportRow> ShiftRows { get; } = new();

    public ObservableCollection<VehicleTypeReportRow> VehicleTypeRows { get; } = new();

    public ObservableCollection<TopPlateRow> TopPlateRows { get; } = new();

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand ExportCommand { get; }

    // ---- loading ---------------------------------------------------------------------------------------------------

    /// <summary>Loads the filter options once, then the report for the current filter (no debounce).</summary>
    public async Task LoadDataAsync()
    {
        CancelPendingReload();
        var cts = new CancellationTokenSource();
        _reloadCts = cts;
        await LoadAsync(cts.Token, loadOptions: true);
    }

    private void ScheduleReload()
    {
        if (_suppressReload)
        {
            return;
        }

        CancelPendingReload();
        var cts = new CancellationTokenSource();
        _reloadCts = cts;
        _ = ReloadAfterDelayAsync(cts.Token);
    }

    private void CancelPendingReload()
    {
        var previous = _reloadCts;
        _reloadCts = null;
        if (previous is null)
        {
            return;
        }

        try
        {
            previous.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // already finished
        }
    }

    private async Task ReloadAfterDelayAsync(CancellationToken token)
    {
        try
        {
            await DelayAsync(DebounceDelay, token);
            token.ThrowIfCancellationRequested();
            await LoadAsync(token, loadOptions: false);
        }
        catch (OperationCanceledException)
        {
            // superseded by a newer filter change
        }
    }

    private async Task LoadAsync(CancellationToken token, bool loadOptions)
    {
        var version = Interlocked.Increment(ref _loadVersion);
        try
        {
            IsLoading = true;

            if (loadOptions && !_optionsLoaded)
            {
                _options = await _reportService.GetFilterOptionsAsync(token);
                token.ThrowIfCancellationRequested();
                FillOptions(_options);
                _optionsLoaded = true;
            }

            if (!TryBuildFilter(out var filter, out var invalidMessageKey))
            {
                ErrorMessage = L(invalidMessageKey!);
                return;
            }

            ErrorMessage = null;
            var result = await _reportService.GetReportAsync(filter!, token);
            if (version != _loadVersion || token.IsCancellationRequested)
            {
                return;
            }

            ApplyResult(result);
        }
        catch (OperationCanceledException)
        {
            // superseded
        }
        catch (PermissionDeniedException)
        {
            if (version == _loadVersion)
            {
                ErrorMessage = L("Msg_Auth_PermissionDenied");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ReportsViewModel] load failed: {ex.Message}");
            if (version == _loadVersion)
            {
                ErrorMessage = L(ReportTextKeys.MsgLoadError);
            }
        }
        finally
        {
            if (version == _loadVersion)
            {
                IsLoading = false;
            }
        }
    }

    private bool TryBuildFilter(out ReportFilter? filter, out string? invalidMessageKey)
    {
        filter = null;
        invalidMessageKey = null;
        var today = ReportPeriodCalculator.TodayVn(_timeProvider.GetUtcNow().UtcDateTime);

        ReportDateRange range;
        if (_selectedPreset.Preset == ReportPeriodPreset.Custom)
        {
            DateOnly? from = _customFrom is { } f ? DateOnly.FromDateTime(f) : null;
            DateOnly? to = _customTo is { } t ? DateOnly.FromDateTime(t) : null;
            switch (ReportPeriodCalculator.Validate(from, to))
            {
                case ReportRangeValidation.Valid:
                    break;
                case ReportRangeValidation.TooLong:
                    invalidMessageKey = ReportTextKeys.MsgRangeTooLong;
                    return false;
                default:
                    invalidMessageKey = ReportTextKeys.MsgInvalidRange;
                    return false;
            }

            range = ReportPeriodCalculator.Resolve(ReportPeriodPreset.Custom, today, from, to);
        }
        else
        {
            range = ReportPeriodCalculator.Resolve(_selectedPreset.Preset, today);
        }

        filter = new ReportFilter(range, _selectedVehicleType?.Id, _selectedCustomerGroup.Group, _selectedZone?.Id, _selectedPreset.Preset);
        return true;
    }

    private void PrefillCustomRange(ReportPeriodPreset basedOn)
    {
        var today = ReportPeriodCalculator.TodayVn(_timeProvider.GetUtcNow().UtcDateTime);
        var range = ReportPeriodCalculator.Resolve(basedOn == ReportPeriodPreset.Custom ? ReportPeriodPreset.Last7Days : basedOn, today);

        _customFrom = range.FromVn.ToDateTime(TimeOnly.MinValue);
        _customTo = range.ToVn.ToDateTime(TimeOnly.MinValue);
        OnPropertyChanged(nameof(CustomFrom));
        OnPropertyChanged(nameof(CustomTo));
    }

    private void FillOptions(ReportFilterOptions options)
    {
        var vehicleId = _selectedVehicleType?.Id;
        var zoneId = _selectedZone?.Id;
        var previousSuppress = _suppressReload;
        _suppressReload = true;
        try
        {
            VehicleTypeOptions.Clear();
            VehicleTypeOptions.Add(new LookupOption(null, L(ReportTextKeys.FilterAll)));
            foreach (var item in options.VehicleTypes)
            {
                VehicleTypeOptions.Add(new LookupOption(item.Id, item.Name));
            }

            ZoneOptions.Clear();
            ZoneOptions.Add(new LookupOption(null, L(ReportTextKeys.FilterAll)));
            foreach (var item in options.Zones)
            {
                ZoneOptions.Add(new LookupOption(item.Id, item.Name));
            }

            SelectedVehicleType = VehicleTypeOptions.FirstOrDefault(o => o.Id == vehicleId) ?? VehicleTypeOptions[0];
            SelectedZone = ZoneOptions.FirstOrDefault(o => o.Id == zoneId) ?? ZoneOptions[0];
        }
        finally
        {
            _suppressReload = previousSuppress;
        }
    }

    // ---- presentation ----------------------------------------------------------------------------------------------

    private void ApplyResult(ReportResult result)
    {
        CurrentResult = result;
        RangeDisplay = DisplayRange(result.Filter.Range);
        PreviousRangeDisplay = L(ReportTextKeys.ComparedTo) + ": " + DisplayRange(result.PreviousRange);

        ApplyTiles(result);
        ApplyCharts(result);

        Replace(DailyRows, result.Daily);
        Replace(ShiftRows, result.Shifts);
        Replace(VehicleTypeRows, result.VehicleTypes);
        Replace(TopPlateRows, result.TopPlates);
    }

    private void ApplyTiles(ReportResult result)
    {
        var k = result.Kpis;

        void Set(string key, string value, KpiComparison? comparison, string? sub = null)
        {
            var tile = KpiTiles.First(t => t.TitleKey == key);
            tile.Title = L(key);
            tile.ValueText = value;
            tile.ChangeText = comparison is null ? string.Empty : ReportFormat.Change(comparison);
            tile.Trend = comparison?.Trend ?? KpiTrend.None;
            tile.SubText = sub;
        }

        Set(ReportTextKeys.KpiCheckIns, ReportFormat.Number((double)k.CheckIns.Current), k.CheckIns);
        Set(ReportTextKeys.KpiCheckOuts, ReportFormat.Number((double)k.CheckOuts.Current), k.CheckOuts);
        Set(ReportTextKeys.KpiInLotNow, ReportFormat.Number(k.VehiclesInLotNow), null);
        Set(ReportTextKeys.KpiNetRevenue, ReportFormat.Currency(k.NetRevenue.Current), k.NetRevenue,
            Format(ReportTextKeys.KpiRefundAdjustment, ReportFormat.Currency(k.RefundTotal), ReportFormat.Currency(k.AdjustmentTotal)));
        Set(ReportTextKeys.KpiCash, ReportFormat.Currency(k.CashRevenue.Current), k.CashRevenue);
        Set(ReportTextKeys.KpiVietQr, ReportFormat.Currency(k.VietQrRevenue.Current), k.VietQrRevenue);
        Set(ReportTextKeys.KpiCard, ReportFormat.Currency(k.CardRevenue.Current), k.CardRevenue);
        Set(ReportTextKeys.KpiFreeCheckOuts, ReportFormat.Number((double)k.FreeCheckOuts.Current), k.FreeCheckOuts);
        Set(ReportTextKeys.KpiAvgDuration,
            ReportFormat.Duration(ToDouble(k.AverageDurationMinutes), (key, args) => _localization.GetString(key, args)),
            k.AverageDurationMinutes);
        Set(ReportTextKeys.KpiPeakHour, ReportFormat.HourRange(k.PeakHour), null,
            Format(ReportTextKeys.KpiPreviousPeakHour, ReportFormat.HourRange(k.PreviousPeakHour)));
        Set(ReportTextKeys.KpiAvgOccupancy, ReportFormat.Percent(ToDouble(k.AverageOccupancyPercent)), k.AverageOccupancyPercent);
        Set(ReportTextKeys.KpiPeakOccupancy, ReportFormat.Percent(ToDouble(k.PeakOccupancyPercent)), k.PeakOccupancyPercent);
        Set(ReportTextKeys.KpiResidentShare, ReportFormat.Percent(ToDouble(k.ResidentSharePercent)), k.ResidentSharePercent,
            k.ResidentSharePercent is null
                ? null
                : Format(ReportTextKeys.KpiResidentVisitorSplit,
                    ReportFormat.Percent(ToDouble(k.ResidentSharePercent)),
                    ReportFormat.Percent(k.VisitorSharePercent is null ? null : (double)k.VisitorSharePercent.Value)));

        if (k.MonthlyTicketRevenue is null || k.MonthlyTicketSales is null)
        {
            Set(ReportTextKeys.KpiMonthlyTicketRevenue, ReportFormat.Dash, null, L(ReportTextKeys.KpiNotApplicableZone));
        }
        else
        {
            var sales = k.MonthlyTicketSales;
            Set(ReportTextKeys.KpiMonthlyTicketRevenue, ReportFormat.Currency(k.MonthlyTicketRevenue.Current), k.MonthlyTicketRevenue,
                Format(ReportTextKeys.KpiMonthlyTicketSplit,
                    ReportFormat.Currency(sales.NewRevenue), sales.NewCount, ReportFormat.Currency(sales.RenewRevenue), sales.RenewCount));
        }
    }

    private void ApplyCharts(ReportResult result)
    {
        var revenue = ReportChartMapper.RevenueByDay(result);
        RevenueSeries = LiveChartsSeriesFactory.CreateCartesian(revenue, L);
        RevenueXAxes = LiveChartsSeriesFactory.CreateCategoryAxes(revenue);
        RevenueYAxes = LiveChartsSeriesFactory.CreateValueAxes(revenue.IsCurrency);
        RevenueHasData = revenue.HasData;

        var hourly = ReportChartMapper.HourlyTraffic(result);
        HourlySeries = LiveChartsSeriesFactory.CreateCartesian(hourly, L);
        HourlyXAxes = LiveChartsSeriesFactory.CreateCategoryAxes(hourly);
        HourlyYAxes = LiveChartsSeriesFactory.CreateValueAxes(hourly.IsCurrency);
        HourlyHasData = hourly.HasData;

        var daily = ReportChartMapper.DailyTraffic(result);
        DailyTrafficSeries = LiveChartsSeriesFactory.CreateCartesian(daily, L);
        DailyTrafficXAxes = LiveChartsSeriesFactory.CreateCategoryAxes(daily);
        DailyTrafficYAxes = LiveChartsSeriesFactory.CreateValueAxes(daily.IsCurrency);
        DailyTrafficHasData = daily.HasData;

        var groups = ReportChartMapper.CustomerGroups(result);
        CustomerGroupSeries = LiveChartsSeriesFactory.CreatePie(groups, L);
        CustomerGroupHasData = groups.HasData;

        var zones = ReportChartMapper.ZoneOccupancy(result);
        ZoneSeries = LiveChartsSeriesFactory.CreateCartesian(zones, L);
        ZoneXAxes = LiveChartsSeriesFactory.CreateValueAxes(zones.IsCurrency);
        ZoneYAxes = LiveChartsSeriesFactory.CreateCategoryAxes(zones);
        ZoneHasData = zones.HasData;

        var vehicles = ReportChartMapper.VehicleTypeMix(result);
        VehicleTypeSeries = LiveChartsSeriesFactory.CreatePie(vehicles, L);
        VehicleTypeHasData = vehicles.HasData;
    }

    private static void Replace<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }

    private static double? ToDouble(KpiComparison? comparison) => comparison is null ? null : (double)comparison.Current;

    private static string DisplayRange(ReportDateRange range)
        => string.Create(CultureInfo.InvariantCulture, $"{range.FromVn:dd/MM/yyyy} – {range.ToVn:dd/MM/yyyy}");

    private string L(string key) => _localization.GetString(key);

    private string Format(string key, params object[] args) => _localization.GetString(key, args);

    private ReportPresetOption[] BuildPresets() => new[]
    {
        new ReportPresetOption(ReportPeriodPreset.Today, L(ReportTextKeys.PresetToday)),
        new ReportPresetOption(ReportPeriodPreset.Yesterday, L(ReportTextKeys.PresetYesterday)),
        new ReportPresetOption(ReportPeriodPreset.Last7Days, L(ReportTextKeys.PresetLast7Days)),
        new ReportPresetOption(ReportPeriodPreset.Last30Days, L(ReportTextKeys.PresetLast30Days)),
        new ReportPresetOption(ReportPeriodPreset.ThisMonth, L(ReportTextKeys.PresetThisMonth)),
        new ReportPresetOption(ReportPeriodPreset.LastMonth, L(ReportTextKeys.PresetLastMonth)),
        new ReportPresetOption(ReportPeriodPreset.Custom, L(ReportTextKeys.PresetCustom))
    };

    private CustomerGroupOption[] BuildCustomerGroups() => new[]
    {
        new CustomerGroupOption(ReportCustomerGroup.All, L(ReportTextKeys.GroupAll)),
        new CustomerGroupOption(ReportCustomerGroup.Resident, L(ReportTextKeys.GroupResident)),
        new CustomerGroupOption(ReportCustomerGroup.MonthlyPass, L(ReportTextKeys.GroupMonthlyPass)),
        new CustomerGroupOption(ReportCustomerGroup.Visitor, L(ReportTextKeys.GroupVisitor))
    };

    private void OnLanguageChanged()
    {
        var preset = _selectedPreset.Preset;
        var group = _selectedCustomerGroup.Group;
        var previousSuppress = _suppressReload;
        _suppressReload = true;
        try
        {
            Presets = BuildPresets();
            CustomerGroupOptions = BuildCustomerGroups();
            _selectedPreset = Presets.First(p => p.Preset == preset);
            _selectedCustomerGroup = CustomerGroupOptions.First(g => g.Group == group);
            OnPropertyChanged(nameof(SelectedPreset));
            OnPropertyChanged(nameof(SelectedCustomerGroup));

            if (_options is not null)
            {
                FillOptions(_options);
            }
            else
            {
                VehicleTypeOptions[0] = new LookupOption(null, L(ReportTextKeys.FilterAll));
                ZoneOptions[0] = new LookupOption(null, L(ReportTextKeys.FilterAll));
                SelectedVehicleType = VehicleTypeOptions[0];
                SelectedZone = ZoneOptions[0];
            }
        }
        finally
        {
            _suppressReload = previousSuppress;
        }

        foreach (var tile in KpiTiles)
        {
            tile.Title = L(tile.TitleKey);
        }

        OnPropertyChanged(nameof(ExportToolTip));
        if (_currentResult is not null)
        {
            ApplyResult(_currentResult);
        }
    }

    // ---- export ----------------------------------------------------------------------------------------------------

    private async Task ExportAsync()
    {
        var result = CurrentResult;
        if (result is null || !CanExport)
        {
            return;
        }

        var filter = result.Filter;
        var path = _fileDialogService.ShowSaveFileDialog(
            L(ReportTextKeys.SaveDialogFilter),
            _exportService.GetDefaultFileName(filter.Range),
            L(ReportTextKeys.SaveDialogTitle));
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            IsExporting = true;
            var export = await _exportService.ExportAsync(filter, path);
            switch (export.Status)
            {
                case ReportExportStatus.Success when !export.AuditWritten:
                    _dialogService.ShowWarning(L(ReportTextKeys.MsgExportAuditNotWritten));
                    break;
                case ReportExportStatus.Success:
                    _dialogService.ShowSuccess(Format(ReportTextKeys.MsgExportSuccess, export.FilePath ?? path));
                    break;
                case ReportExportStatus.FileLocked:
                    _dialogService.ShowError(L(ReportTextKeys.MsgExportFileLocked));
                    break;
                default:
                    _dialogService.ShowError(L(ReportTextKeys.MsgExportError));
                    break;
            }
        }
        catch (PermissionDeniedException)
        {
            _dialogService.ShowError(L("Msg_Auth_PermissionDenied"));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ReportsViewModel] export failed: {ex.Message}");
            _dialogService.ShowError(L(ReportTextKeys.MsgExportError));
        }
        finally
        {
            IsExporting = false;
        }
    }
}
