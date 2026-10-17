using SmartPS.Services.Localization;

namespace SmartPS.Services.Reports;

/// <summary>
/// Immutable snapshot of every report label. It is resolved on the calling (UI) thread before any await, so the
/// workbook writer running on a worker thread never touches <see cref="ILocalizationService"/>.
/// </summary>
public sealed class ReportWorkbookLabels
{
    private readonly IReadOnlyDictionary<string, string> _values;

    private ReportWorkbookLabels(IReadOnlyDictionary<string, string> values) => _values = values;

    public static ReportWorkbookLabels Resolve(ILocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(localization);

        var values = new Dictionary<string, string>(ReportTextKeys.All.Count, StringComparer.Ordinal);
        foreach (var key in ReportTextKeys.All)
        {
            values[key] = localization.GetString(key);
        }

        return new ReportWorkbookLabels(values);
    }

    public static ReportWorkbookLabels FromDictionary(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new ReportWorkbookLabels(new Dictionary<string, string>(values, StringComparer.Ordinal));
    }

    /// <summary>The label for <paramref name="key"/>, or the key itself when it is unknown.</summary>
    public string this[string key] => _values.TryGetValue(key, out var value) ? value : key;

    public string Format(string key, params object[] args)
    {
        var template = this[key];
        if (args.Length == 0)
        {
            return template;
        }

        try
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }
}
