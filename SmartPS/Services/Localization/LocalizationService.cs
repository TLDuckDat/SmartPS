using System.Globalization;
using System.Windows;
using System.Windows.Markup;

namespace SmartPS.Services.Localization;

/// <summary>
/// Triển khai ILocalizationService quản lý thay đổi ngôn ngữ runtime tức thì (<10ms)
/// Sử dụng cơ chế MergedDictionaries và DynamicResource của WPF
/// </summary>
public class LocalizationService : ILocalizationService
{
    private string _currentLanguage = "vi-VN";
    public string CurrentLanguage => _currentLanguage;

    public CultureInfo CurrentCulture => new CultureInfo(_currentLanguage);

    public event Action? LanguageChanged;

    public LocalizationService()
    {
        // Khởi tạo ngôn ngữ ban đầu
        ApplyLanguage(_currentLanguage);
    }

    public void SetLanguage(string cultureCode)
    {
        if (string.IsNullOrWhiteSpace(cultureCode)) return;

        // Chuẩn hóa mã ngôn ngữ
        var normalizedCode = cultureCode.ToLowerInvariant() switch
        {
            "vi" or "vi-vn" => "vi-VN",
            "en" or "en-us" => "en-US",
            "ja" or "ja-jp" => "ja-JP",
            _ => cultureCode
        };

        if (_currentLanguage == normalizedCode) return;

        _currentLanguage = normalizedCode;
        ApplyLanguage(_currentLanguage);
        LanguageChanged?.Invoke();
    }

    private void ApplyLanguage(string cultureCode)
    {
        var culture = new CultureInfo(cultureCode);
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        void UpdateDictionary()
        {
            var dictUri = new Uri($"Resources/Languages/Strings.{cultureCode}.xaml", UriKind.Relative);
            var newDict = new ResourceDictionary { Source = dictUri };

            var mergedDicts = Application.Current.Resources.MergedDictionaries;

            // Tìm vị trí dictionary ngôn ngữ cũ
            var oldDict = mergedDicts.FirstOrDefault(d =>
                d.Source != null && d.Source.OriginalString.Contains("Resources/Languages/Strings."));

            if (oldDict != null)
            {
                var index = mergedDicts.IndexOf(oldDict);
                mergedDicts[index] = newDict;
            }
            else
            {
                mergedDicts.Add(newDict);
            }

            // Đồng bộ XmlLanguage cho các cửa sổ đang mở để hỗ trợ định dạng số, ngày giờ tự động
            var xmlLanguage = XmlLanguage.GetLanguage(culture.IetfLanguageTag);
            foreach (Window window in Application.Current.Windows)
            {
                window.Language = xmlLanguage;
            }
        }

        if (Application.Current == null) return;

        if (Application.Current.Dispatcher.CheckAccess())
        {
            UpdateDictionary();
        }
        else
        {
            Application.Current.Dispatcher.Invoke(UpdateDictionary);
        }
    }

    public string GetString(string key, params object[] args)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;

        string? resourceText = null;

        if (Application.Current != null)
        {
            if (Application.Current.Dispatcher.CheckAccess())
            {
                resourceText = Application.Current.TryFindResource(key) as string;
            }
            else
            {
                resourceText = Application.Current.Dispatcher.Invoke(() =>
                    Application.Current.TryFindResource(key) as string);
            }
        }

        if (resourceText == null)
        {
            return key; // Trả về key làm fallback
        }

        // Chuẩn hóa ký tự xuống dòng: chuyển \n, \r\n hoặc chuỗi thoát literal "\n" thành Environment.NewLine
        resourceText = resourceText
            .Replace("\\r\\n", "\n")
            .Replace("\\n", "\n")
            .Replace("\r\n", "\n")
            .Replace("\n", Environment.NewLine);

        if (args != null && args.Length > 0)
        {
            try
            {
                return string.Format(CurrentCulture, resourceText, args);
            }
            catch
            {
                return resourceText;
            }
        }

        return resourceText;
    }

    public string this[string key] => GetString(key);
}
