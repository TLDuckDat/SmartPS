namespace SmartPS.Services.Dialog;

/// <summary>Save-file dialog kept apart from <see cref="IDialogService"/>, whose contract other features depend on.</summary>
public interface IFileDialogService
{
    /// <returns>The chosen path, or null when the user cancelled.</returns>
    string? ShowSaveFileDialog(string filter, string defaultFileName, string? title = null, string? initialDirectory = null);
}
