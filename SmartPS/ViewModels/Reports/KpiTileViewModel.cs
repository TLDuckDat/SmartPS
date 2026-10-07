using SmartPS.Models.Reports;

namespace SmartPS.ViewModels.Reports;

/// <summary>One KPI card: localized title, formatted value, comparison text and an optional sub-line.</summary>
public sealed class KpiTileViewModel : ViewModelBase
{
    private string _title;
    private string _valueText = ReportKpiPlaceholder;
    private string _changeText = string.Empty;
    private KpiTrend _trend;
    private string? _subText;

    private const string ReportKpiPlaceholder = "—";

    public KpiTileViewModel(string titleKey, string title)
    {
        TitleKey = titleKey ?? throw new ArgumentNullException(nameof(titleKey));
        _title = title;
    }

    public string TitleKey { get; }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string ValueText
    {
        get => _valueText;
        set => SetProperty(ref _valueText, value);
    }

    public string ChangeText
    {
        get => _changeText;
        set
        {
            if (SetProperty(ref _changeText, value))
            {
                OnPropertyChanged(nameof(HasChange));
            }
        }
    }

    public bool HasChange => !string.IsNullOrEmpty(_changeText);

    public KpiTrend Trend
    {
        get => _trend;
        set => SetProperty(ref _trend, value);
    }

    public string? SubText
    {
        get => _subText;
        set
        {
            if (SetProperty(ref _subText, value))
            {
                OnPropertyChanged(nameof(HasSubText));
            }
        }
    }

    public bool HasSubText => !string.IsNullOrEmpty(_subText);
}
