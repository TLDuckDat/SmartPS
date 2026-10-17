using System.Windows;

namespace SmartPS.Services.Dialog;

/// <summary>WPF implementation of <see cref="IFileDialogService"/>; the dialog always runs on the UI dispatcher.</summary>
public sealed class FileDialogService : IFileDialogService
{
    public string? ShowSaveFileDialog(string filter, string defaultFileName, string? title = null, string? initialDirectory = null)
    {
        var dispatcher = Application.Current.Dispatcher;
        return dispatcher.CheckAccess()
            ? Show(filter, defaultFileName, title, initialDirectory)
            : dispatcher.Invoke(() => Show(filter, defaultFileName, title, initialDirectory));
    }

    private static string? Show(string filter, string defaultFileName, string? title, string? initialDirectory)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = filter,
            FileName = defaultFileName,
            DefaultExt = ".xlsx",
            AddExtension = true,
            OverwritePrompt = true,
            Title = title ?? string.Empty,
            InitialDirectory = initialDirectory ?? string.Empty
        };

        var owner = Application.Current.Windows
            .OfType<Window>()
            .FirstOrDefault(w => w.IsActive && w.IsVisible)
            ?? Application.Current.MainWindow;

        var result = owner != null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
        return result == true ? dialog.FileName : null;
    }
}
