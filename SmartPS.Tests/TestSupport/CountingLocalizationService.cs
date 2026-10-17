using System.Collections.Concurrent;
using System.Globalization;
using SmartPS.Services.Localization;

namespace SmartPS.Tests.TestSupport;

/// <summary>
/// Plan ADDENDUM N1: like <see cref="FakeLocalizationService"/> (returns the key) but records how many lookups were made
/// and on which managed thread, so a test can prove every label was resolved synchronously before the first await (m6).
/// </summary>
public sealed class CountingLocalizationService : ILocalizationService
{
    private readonly ConcurrentQueue<(string Key, int ThreadId)> _calls = new();

    public int CallCount => _calls.Count;

    public IReadOnlyList<(string Key, int ThreadId)> Calls => _calls.ToArray();

    public string CurrentLanguage => "en-US";

    public CultureInfo CurrentCulture => CultureInfo.InvariantCulture;

    public void SetLanguage(string cultureCode) => LanguageChanged?.Invoke();

    public string GetString(string key, params object[] args)
    {
        Record(key);
        return key;
    }

    public string this[string key]
    {
        get
        {
            Record(key);
            return key;
        }
    }

    public event Action? LanguageChanged;

    private void Record(string key) => _calls.Enqueue((key, Environment.CurrentManagedThreadId));
}
