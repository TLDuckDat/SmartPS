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

    // Bộ Vector SVG Icon sắc nét chuẩn độ phân giải cao
    private const string IconDataCheckmark = "M9 16.17L4.83 12l-1.42 1.41L9 19 21 7l-1.41-1.41z";
    private const string IconDataInfo = "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-6h2v6zm0-8h-2V7h2v2z";
    private const string IconDataWarning = "M1 21h22L12 2 1 21zm12-3h-2v-2h2v2zm0-4h-2v-4h2v4z";
    private const string IconDataError = "M12 2C6.47 2 2 6.48 2 12s4.47 10 10 10 10-4.48 10-10S17.53 2 12 2zm5 13.59L15.59 17 12 13.41 8.41 17 7 15.59 10.59 12 7 8.41 8.41 7 12 10.59 15.59 7 17 8.41 13.41 12 17 15.59z";
    private const string IconDataQuestion = "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 16h-2v-2h2v2zm1.07-7.75l-.9.92C12.45 11.9 12 12.5 12 14h-2v-.5c0-1.1.45-2.1 1.17-2.83l1.24-1.26c.37-.36.59-.86.59-1.41 0-1.1-.9-2-2-2s-2 .9-2 2H7c0-2.76 2.24-5 5-5s5 2.24 5 5c0 1.04-.42 1.99-1.07 2.75z";

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
                IconBadgeBorder.Background = new SolidColorBrush(Color.FromRgb(0xDC, 0xFC, 0xE7));
                IconBadgeBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x86, 0xEF, 0xAC));
                IconPath.Data = Geometry.Parse(IconDataCheckmark);
                IconPath.Fill = new SolidColorBrush(Color.FromRgb(0x15, 0x80, 0x3D));
                PrimaryButton.Background = new SolidColorBrush(Color.FromRgb(0x15, 0x80, 0x3D));
                break;

            case NotificationType.Warning:
                IconBadgeBorder.Background = new SolidColorBrush(Color.FromRgb(0xFE, 0xF3, 0xC7));
                IconBadgeBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFD, 0xE6, 0x8A));
                IconPath.Data = Geometry.Parse(IconDataWarning);
                IconPath.Fill = new SolidColorBrush(Color.FromRgb(0xB4, 0x53, 0x09));
                PrimaryButton.Background = new SolidColorBrush(Color.FromRgb(0xB4, 0x53, 0x09));
                break;

            case NotificationType.Error:
                IconBadgeBorder.Background = new SolidColorBrush(Color.FromRgb(0xFE, 0xE2, 0xE2));
                IconBadgeBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFC, 0xA5, 0xA5));
                IconPath.Data = Geometry.Parse(IconDataError);
                IconPath.Fill = new SolidColorBrush(Color.FromRgb(0xB9, 0x1C, 0x1C));
                PrimaryButton.Background = new SolidColorBrush(Color.FromRgb(0xB9, 0x1C, 0x1C));
                break;

            case NotificationType.Question:
            case NotificationType.Information:
            default:
                IconBadgeBorder.Background = new SolidColorBrush(Color.FromRgb(0xEF, 0xF6, 0xFF));
                IconBadgeBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xBF, 0xDB, 0xFE));
                IconPath.Data = Geometry.Parse(type == NotificationType.Question ? IconDataQuestion : IconDataInfo);
                IconPath.Fill = new SolidColorBrush(Color.FromRgb(0x1D, 0x4E, 0xD8));
                PrimaryButton.Background = new SolidColorBrush(Color.FromRgb(0x1D, 0x4E, 0xD8));
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
