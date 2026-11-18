using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using SmartPS.Services.Localization;

namespace SmartPS.Views.Common;

/// <summary>
/// Phân loại thông báo hiển thị trên Dialog
/// </summary>
public enum NotificationType
{
    Information,
    Success,
    Warning,
    Error,
    Question
}

/// <summary>
/// Các kiểu nút bấm trên Dialog
/// </summary>
public enum NotificationButtons
{
    Ok,
    OkCancel,
    YesNo
}

/// <summary>
/// Kết quả người dùng phản hồi từ Dialog
/// </summary>
public enum NotificationResult
{
    Ok,
    Cancel,
    Yes,
    No
}

/// <summary>
/// Cửa sổ Dialog thông báo chuẩn mực, chuyên nghiệp, độ tương phản cao, dễ nhìn
/// </summary>
public partial class NotificationDialog : Window
{
    public NotificationResult Result { get; private set; } = NotificationResult.Cancel;

    // Bộ Vector SVG Icon tự vẽ nét mảnh (đồng bộ với Resources/Icons.xaml)
    private const string IconDataCheckmark = "M4.5,12.5 L9.5,17.5 L19.5,6.5";
    private const string IconDataInfo = "M12,21 A9,9 0 1,0 12,3 A9,9 0 1,0 12,21 Z M12,16 V11.5 M12,8.2 H12.01";
    private const string IconDataWarning = "M10.3,4.8 L3.4,17 A2,2 0 0,0 5.1,20 H18.9 A2,2 0 0,0 20.6,17 L13.7,4.8 A2,2 0 0,0 10.3,4.8 Z M12,9.5 V13.5 M12,16.8 H12.01";
    private const string IconDataError = "M12,21 A9,9 0 1,0 12,3 A9,9 0 1,0 12,21 Z M9.2,9.2 L14.8,14.8 M14.8,9.2 L9.2,14.8";
    private const string IconDataQuestion = "M12,21 A9,9 0 1,0 12,3 A9,9 0 1,0 12,21 Z M9.8,9.2 A2.4,2.4 0 0,1 14.4,10.2 C14.4,11.8 12,12.2 12,14 M12,17.2 H12.01";

    private readonly NotificationButtons _buttons;

    public NotificationDialog(
        string message,
        string? title = null,
        NotificationType type = NotificationType.Information,
        NotificationButtons buttons = NotificationButtons.Ok,
        string? primaryButtonText = null,
        string? secondaryButtonText = null)
    {
        InitializeComponent();

        title ??= AppText.Get("Str_Dialog_Title_Info");
        _buttons = buttons;
        Title = title;
        TitleTextBlock.Text = title;
        
        // Chuẩn hóa ký tự xuống dòng (hỗ trợ cả chuỗi thoát \n lẫn Environment.NewLine)
        var normalizedMessage = message?
            .Replace("\\r\\n", "\n")
            .Replace("\\n", "\n")
            .Replace("\r\n", "\n")
            .Replace("\n", Environment.NewLine) ?? string.Empty;
        MessageTextBlock.Text = normalizedMessage;

        ApplyTheme(type);
        ConfigureButtons(buttons, primaryButtonText, secondaryButtonText);
    }

    /// <summary>
    /// Cấu hình bảng màu chuẩn, biểu tượng trực quan rõ ràng
    /// </summary>
    private void ApplyTheme(NotificationType type)
    {
        switch (type)
        {
            case NotificationType.Success:
                IconBadgeBorder.Background = new SolidColorBrush(Color.FromRgb(0xE6, 0xF9, 0xF0));
                IconBadgeBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0xE6, 0xC1));
                IconPath.Data = Geometry.Parse(IconDataCheckmark);
                IconPath.Stroke = new SolidColorBrush(Color.FromRgb(0x16, 0x8A, 0x5B));
                PrimaryButton.Background = new SolidColorBrush(Color.FromRgb(0x16, 0x8A, 0x5B));
                break;

            case NotificationType.Warning:
                IconBadgeBorder.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF3, 0xD6));
                IconBadgeBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFC, 0xD9, 0x8B));
                IconPath.Data = Geometry.Parse(IconDataWarning);
                IconPath.Stroke = new SolidColorBrush(Color.FromRgb(0xB8, 0x6B, 0x14));
                PrimaryButton.Background = new SolidColorBrush(Color.FromRgb(0xB8, 0x6B, 0x14));
                break;

            case NotificationType.Error:
                IconBadgeBorder.Background = new SolidColorBrush(Color.FromRgb(0xFD, 0xE8, 0xE8));
                IconBadgeBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xF5, 0xB4, 0xB4));
                IconPath.Data = Geometry.Parse(IconDataError);
                IconPath.Stroke = new SolidColorBrush(Color.FromRgb(0xC9, 0x3B, 0x4B));
                PrimaryButton.Background = new SolidColorBrush(Color.FromRgb(0xC9, 0x3B, 0x4B));
                break;

            case NotificationType.Question:
            case NotificationType.Information:
            default:
                IconBadgeBorder.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF0, 0xE8));
                IconBadgeBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xF7, 0xC8, 0xB4));
                IconPath.Data = Geometry.Parse(type == NotificationType.Question ? IconDataQuestion : IconDataInfo);
                IconPath.Stroke = new SolidColorBrush(Color.FromRgb(0xE8, 0x73, 0x5A));
                PrimaryButton.Background = new SolidColorBrush(Color.FromRgb(0xE8, 0x73, 0x5A));
                break;
        }
    }

    private void ConfigureButtons(NotificationButtons buttons, string? primaryText, string? secondaryText)
    {
        switch (buttons)
        {
            case NotificationButtons.Ok:
                PrimaryButton.Content = primaryText ?? AppText.Get("Str_Btn_Confirm");
                SecondaryButton.Visibility = Visibility.Collapsed;
                break;

            case NotificationButtons.OkCancel:
                PrimaryButton.Content = primaryText ?? AppText.Get("Str_Btn_Confirm");
                SecondaryButton.Content = secondaryText ?? AppText.Get("Str_Btn_Cancel");
                SecondaryButton.Visibility = Visibility.Visible;
                break;

            case NotificationButtons.YesNo:
                PrimaryButton.Content = primaryText ?? AppText.Get("Str_Btn_Yes");
                SecondaryButton.Content = secondaryText ?? AppText.Get("Str_Btn_No");
                SecondaryButton.Visibility = Visibility.Visible;
                break;
        }
    }

    private void PrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        Result = _buttons == NotificationButtons.YesNo ? NotificationResult.Yes : NotificationResult.Ok;
        DialogResult = true;
        Close();
    }

    private void SecondaryButton_Click(object sender, RoutedEventArgs e)
    {
        Result = _buttons == NotificationButtons.YesNo ? NotificationResult.No : NotificationResult.Cancel;
        DialogResult = false;
        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Result = _buttons == NotificationButtons.YesNo ? NotificationResult.No : NotificationResult.Cancel;
            DialogResult = false;
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            Result = _buttons == NotificationButtons.YesNo ? NotificationResult.Yes : NotificationResult.Ok;
            DialogResult = true;
            Close();
            e.Handled = true;
        }
    }

    // =========================================================================
    // STATIC CONVENIENCE METHODS
    // =========================================================================

    private static Window? GetDefaultOwner(Window? specifiedOwner)
    {
        if (specifiedOwner != null) return specifiedOwner;

        return Application.Current.Windows
            .OfType<Window>()
            .FirstOrDefault(w => w.IsActive)
            ?? Application.Current.MainWindow;
    }

    /// <summary>
    /// Hiển thị Dialog thông báo đầy đủ tùy biến
    /// </summary>
    public static NotificationResult Show(
        string message,
        string? title = null,
        NotificationType type = NotificationType.Information,
        NotificationButtons buttons = NotificationButtons.Ok,
        Window? owner = null,
        string? primaryButtonText = null,
        string? secondaryButtonText = null)
    {
        var dialog = new NotificationDialog(message, title, type, buttons, primaryButtonText, secondaryButtonText);
        var targetOwner = GetDefaultOwner(owner);

        if (targetOwner != null && targetOwner.IsVisible)
        {
            dialog.Owner = targetOwner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        dialog.ShowDialog();
        return dialog.Result;
    }

    public static void ShowSuccess(string message, string? title = null, Window? owner = null, string? buttonText = null)
    {
        Show(message, title ?? AppText.Get("Str_Dialog_Title_Success"), NotificationType.Success, NotificationButtons.Ok, owner,
            buttonText ?? AppText.Get("Str_Btn_Confirm"));
    }

    public static void ShowInfo(string message, string? title = null, Window? owner = null, string? buttonText = null)
    {
        Show(message, title ?? AppText.Get("Str_Dialog_Title_Info"), NotificationType.Information, NotificationButtons.Ok, owner,
            buttonText ?? AppText.Get("Str_Btn_Confirm"));
    }

    public static void ShowWarning(string message, string? title = null, Window? owner = null, string? buttonText = null)
    {
        Show(message, title ?? AppText.Get("Str_Dialog_Title_Warning"), NotificationType.Warning, NotificationButtons.Ok, owner,
            buttonText ?? AppText.Get("Str_Btn_Understood"));
    }

    public static void ShowError(string message, string? title = null, Window? owner = null, string? buttonText = null)
    {
        Show(message, title ?? AppText.Get("Str_Dialog_Title_Error"), NotificationType.Error, NotificationButtons.Ok, owner,
            buttonText ?? AppText.Get("Str_Btn_Close"));
    }

    public static bool ShowConfirm(
        string message,
        string? title = null,
        string? confirmText = null,
        string? cancelText = null,
        Window? owner = null)
    {
        var result = Show(message, title ?? AppText.Get("Str_Dialog_Title_Confirm"), NotificationType.Question, NotificationButtons.OkCancel, owner, confirmText, cancelText);
        return result == NotificationResult.Ok || result == NotificationResult.Yes;
    }

    public static bool ShowYesNo(
        string message,
        string? title = null,
        NotificationType type = NotificationType.Warning,
        Window? owner = null,
        string? yesText = null,
        string? noText = null)
    {
        var result = Show(message, title ?? AppText.Get("Str_Dialog_Title_Confirm"), type, NotificationButtons.YesNo, owner, yesText, noText);
        return result == NotificationResult.Yes;
    }
}
