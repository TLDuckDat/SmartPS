using System.Windows;

namespace SmartPS.Views.Common;

/// <summary>
/// Drop-in replacement hiện đại cho System.Windows.MessageBox
/// </summary>
public static class SmartMessageBox
{
    public static MessageBoxResult Show(string messageBoxText)
    {
        return Show(messageBoxText, "Thông báo", MessageBoxButton.OK, MessageBoxImage.None);
    }

    public static MessageBoxResult Show(string messageBoxText, string caption)
    {
        return Show(messageBoxText, caption, MessageBoxButton.OK, MessageBoxImage.None);
    }

    public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button)
    {
        return Show(messageBoxText, caption, button, MessageBoxImage.None);
    }

    public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon)
    {
        return Show(null, messageBoxText, caption, button, icon);
    }

    public static MessageBoxResult Show(Window? owner, string messageBoxText, string caption = "Thông báo", MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None)
    {
        var notificationType = icon switch
        {
            MessageBoxImage.Information => NotificationType.Information,
            MessageBoxImage.Warning => NotificationType.Warning,
            MessageBoxImage.Error => NotificationType.Error,
            MessageBoxImage.Question => NotificationType.Question,
            _ => NotificationType.Information
        };

        var notificationButtons = button switch
        {
            MessageBoxButton.OK => NotificationButtons.Ok,
            MessageBoxButton.OKCancel => NotificationButtons.OkCancel,
            MessageBoxButton.YesNo => NotificationButtons.YesNo,
            MessageBoxButton.YesNoCancel => NotificationButtons.YesNo,
            _ => NotificationButtons.Ok
        };

        var result = NotificationDialog.Show(
            messageBoxText,
            caption,
            notificationType,
            notificationButtons,
            owner);

        return result switch
        {
            NotificationResult.Ok => MessageBoxResult.OK,
            NotificationResult.Cancel => MessageBoxResult.Cancel,
            NotificationResult.Yes => MessageBoxResult.Yes,
            NotificationResult.No => MessageBoxResult.No,
            _ => MessageBoxResult.None
        };
    }

    // Các hàm tiện ích nhanh:
    public static void ShowSuccess(string message, string title = "Thành công", Window? owner = null)
        => NotificationDialog.ShowSuccess(message, title, owner);

    public static void ShowInfo(string message, string title = "Thông báo", Window? owner = null)
        => NotificationDialog.ShowInfo(message, title, owner);

    public static void ShowWarning(string message, string title = "Cảnh báo", Window? owner = null)
        => NotificationDialog.ShowWarning(message, title, owner);

    public static void ShowError(string message, string title = "Lỗi", Window? owner = null)
        => NotificationDialog.ShowError(message, title, owner);

    public static bool ShowConfirm(string message, string title = "Xác nhận", Window? owner = null)
        => NotificationDialog.ShowConfirm(message, title, owner: owner);
}
