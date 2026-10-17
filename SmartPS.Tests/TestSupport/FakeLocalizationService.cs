using System.Globalization;
using SmartPS.Services.Localization;

namespace SmartPS.Tests.TestSupport;

/// <summary>Returns the resource key itself so tests can assert which message was chosen.</summary>
public sealed class FakeLocalizationService : ILocalizationService
{
    public string CurrentLanguage => "en-US";

    public CultureInfo CurrentCulture => CultureInfo.InvariantCulture;

    public void SetLanguage(string cultureCode) => LanguageChanged?.Invoke();

    public string GetString(string key, params object[] args) => key;

    public string this[string key] => key;

    public event Action? LanguageChanged;
}
