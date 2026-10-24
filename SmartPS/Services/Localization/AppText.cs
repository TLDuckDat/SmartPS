using System.Collections;
using System.Globalization;
using System.Windows;

namespace SmartPS.Services.Localization;

/// <summary>
/// Chuỗi giao diện theo ngôn ngữ đang chọn cho nơi không nhận ILocalizationService qua DI (service, model, code-behind).
/// LocalizationService nạp bản sao từ điển mỗi lần đổi ngôn ngữ, nên đọc được từ mọi luồng mà không cần gọi về luồng giao diện.
/// </summary>
public static class AppText
{
    private static volatile IReadOnlyDictionary<string, string> s_catalog = new Dictionary<string, string>();

    /// <summary>Phát sau khi đổi ngôn ngữ (trên luồng giao diện): ViewModel làm mới các chuỗi tự ghép.</summary>
    public static event Action? Changed;

    /// <summary>Chuỗi của <paramref name="key"/>; thiếu khoá thì trả về chính khoá (giống LocalizationService).</summary>
    public static string Get(string key, params object?[] args)
    {
        if (string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        var text = s_catalog.TryGetValue(key, out var value) ? value : key;
        return args.Length == 0 ? text : string.Format(CultureInfo.CurrentCulture, text, args);
    }

    /// <summary>Thay từ điển đang dùng bằng các chuỗi của <paramref name="dictionary"/> (gọi trên luồng giao diện khi đổi ngôn ngữ).</summary>
    public static void Load(ResourceDictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(dictionary);

        var catalog = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in dictionary)
        {
            if (entry.Key is string key && entry.Value is string value)
            {
                catalog[key] = value;
            }
        }

        s_catalog = catalog;
        Changed?.Invoke();
    }

    /// <summary>Nạp sẵn một bộ chuỗi (dùng cho test, nơi không có Application).</summary>
    public static void Load(IReadOnlyDictionary<string, string> catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        s_catalog = catalog;
    }
}
