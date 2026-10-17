using SmartPS.Services.Dialog;

namespace SmartPS.Tests.TestSupport;

/// <summary>Returns <see cref="SaveFilePath"/> (null = user cancelled) and records the arguments of each call.</summary>
public sealed class FakeFileDialogService : IFileDialogService
{
    public string? SaveFilePath { get; set; }

    public List<(string Filter, string DefaultFileName, string? Title, string? InitialDirectory)> SaveCalls { get; } = new();

    public string? ShowSaveFileDialog(string filter, string defaultFileName, string? title = null, string? initialDirectory = null)
    {
        SaveCalls.Add((filter, defaultFileName, title, initialDirectory));
        return SaveFilePath;
    }
}
