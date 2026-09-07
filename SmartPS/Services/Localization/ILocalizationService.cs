using System.Globalization;

namespace SmartPS.Services.Localization;

/// <summary>
/// Dịch vụ quản lý đa ngôn ngữ và chuyển đổi runtime
/// </summary>
public interface ILocalizationService
{
    /// <summary>
    /// Mã ngôn ngữ hiện tại (ví dụ: "vi-VN", "en-US", "ja-JP")
    /// </summary>
    string CurrentLanguage { get; }

    /// <summary>
    /// Đối tượng CultureInfo tương ứng
    /// </summary>
    CultureInfo CurrentCulture { get; }

    /// <summary>
    /// Chuyển đổi ngôn ngữ runtime tức thì (<10ms, Zero-flicker)
    /// </summary>
    /// <param name="cultureCode">"vi-VN" hoặc "en-US" hoặc "ja-JP"</param>
    void SetLanguage(string cultureCode);

    /// <summary>
    /// Lấy chuỗi bản dịch theo key và tham số truyền vào
    /// </summary>
    string GetString(string key, params object[] args);

    /// <summary>
    /// Indexer tra cứu chuỗi nhanh
    /// </summary>
    string this[string key] { get; }

    /// <summary>
    /// Sự kiện thông báo khi ngôn ngữ thay đổi
    /// </summary>
    event Action? LanguageChanged;
}

