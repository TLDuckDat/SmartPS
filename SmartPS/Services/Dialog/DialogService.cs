using System.Windows;
using SmartPS.Models.Auth;
using SmartPS.Services.Auth;
using SmartPS.Services.Localization;
using SmartPS.ViewModels.Dashboard;
using SmartPS.Views.Common;
using SmartPS.Views.Dashboard;

namespace SmartPS.Services.Dialog;

/// <summary>
/// Triển khai IDialogService điều phối hiển thị thông báo và hộp thoại chuẩn WPF
/// Hỗ trợ Dispatcher an toàn luồng, tự động căn chỉnh vị trí và đa ngôn ngữ tức thì
/// </summary>
public class DialogService : IDialogService
{
    private readonly IAuthService _authService;
    private readonly ILocalizationService _localizationService;

    public DialogService(IAuthService authService, ILocalizationService localizationService)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
    }

    private static Window? GetActiveWindow()
    {
        return Application.Current.Dispatcher.Invoke(() =>
        {
            return Application.Current.Windows
                .OfType<Window>()
                .FirstOrDefault(w => w.IsActive && w.IsVisible)
                ?? Application.Current.MainWindow;
        });
    }

    private void InvokeOnUIThread(Action action)
    {
        if (Application.Current.Dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            Application.Current.Dispatcher.Invoke(action);
        }
    }

    private T InvokeOnUIThread<T>(Func<T> func)
    {
        if (Application.Current.Dispatcher.CheckAccess())
        {
            return func();
        }
        return Application.Current.Dispatcher.Invoke(func);
    }

    public void ShowSuccess(string message, string? title = null)
    {
        var resolvedTitle = string.IsNullOrWhiteSpace(title)
            ? _localizationService.GetString("Str_Dialog_Title_Success")
            : title;

        InvokeOnUIThread(() =>
        {
            NotificationDialog.ShowSuccess(message, resolvedTitle, GetActiveWindow());
        });
    }

    public void ShowInfo(string message, string? title = null)
    {
        var resolvedTitle = string.IsNullOrWhiteSpace(title)
            ? _localizationService.GetString("Str_Dialog_Title_Info")
            : title;

        InvokeOnUIThread(() =>
        {
            NotificationDialog.ShowInfo(message, resolvedTitle, GetActiveWindow());
        });
    }

    public void ShowWarning(string message, string? title = null)
    {
        var resolvedTitle = string.IsNullOrWhiteSpace(title)
            ? _localizationService.GetString("Str_Dialog_Title_Warning")
            : title;

        InvokeOnUIThread(() =>
        {
            NotificationDialog.ShowWarning(message, resolvedTitle, GetActiveWindow());
        });
    }

    public void ShowError(string message, string? title = null)
    {
        var resolvedTitle = string.IsNullOrWhiteSpace(title)
            ? _localizationService.GetString("Str_Dialog_Title_Error")
            : title;

        InvokeOnUIThread(() =>
        {
            NotificationDialog.ShowError(message, resolvedTitle, GetActiveWindow());
        });
    }

    public bool ShowConfirm(string message, string? title = null, string? confirmText = null, string? cancelText = null)
    {
        var resolvedTitle = string.IsNullOrWhiteSpace(title)
            ? _localizationService.GetString("Str_Dialog_Title_Confirm")
            : title;
        var resolvedConfirm = string.IsNullOrWhiteSpace(confirmText)
            ? _localizationService.GetString("Str_Btn_Confirm")
            : confirmText;
        var resolvedCancel = string.IsNullOrWhiteSpace(cancelText)
            ? _localizationService.GetString("Str_Btn_Cancel")
            : cancelText;

        return InvokeOnUIThread(() =>
        {
            return NotificationDialog.ShowConfirm(message, resolvedTitle, resolvedConfirm, resolvedCancel, GetActiveWindow());
        });
    }

    public bool ShowYesNo(string message, string? title = null, string? yesText = null, string? noText = null)
    {
        var resolvedTitle = string.IsNullOrWhiteSpace(title)
            ? _localizationService.GetString("Str_Dialog_Title_Confirm")
            : title;
        var resolvedYes = string.IsNullOrWhiteSpace(yesText)
            ? _localizationService.GetString("Str_Btn_Yes")
            : yesText;
        var resolvedNo = string.IsNullOrWhiteSpace(noText)
            ? _localizationService.GetString("Str_Btn_No")
            : noText;

        return InvokeOnUIThread(() =>
        {
            return NotificationDialog.ShowYesNo(message, resolvedTitle, NotificationType.Warning, GetActiveWindow(), resolvedYes, resolvedNo);
        });
    }

    public Task<bool> ShowEditUserDialogAsync(User user, IEnumerable<Role> availableRoles)
    {
        return Task.FromResult(InvokeOnUIThread(() =>
        {
            var editViewModel = new EditUserViewModel(user, availableRoles, _authService, this, _localizationService);
            var dialog = new EditUserDialog(editViewModel)
            {
                Owner = GetActiveWindow()
            };

            return dialog.ShowDialog() == true;
        }));
    }
}

