using SmartPS.Models.Auth;

namespace SmartPS.Services.Dialog;

/// <summary>
/// Dịch vụ quản lý hiển thị thông báo và các hộp thoại trong ứng dụng
/// Giúp ViewModel tương tác với người dùng mà không phụ thuộc trực tiếp vào WPF Views
/// Hỗ trợ đa ngôn ngữ tự động qua ILocalizationService
/// </summary>
public interface IDialogService
{
    void ShowSuccess(string message, string? title = null);
    void ShowInfo(string message, string? title = null);
    void ShowWarning(string message, string? title = null);
    void ShowError(string message, string? title = null);

    bool ShowConfirm(string message, string? title = null, string? confirmText = null, string? cancelText = null);
    bool ShowYesNo(string message, string? title = null, string? yesText = null, string? noText = null);

    Task<bool> ShowEditUserDialogAsync(User user, IEnumerable<Role> availableRoles);
}

