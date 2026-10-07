using SmartPS.Models.Auth;
using SmartPS.Services.Dialog;

namespace SmartPS.Tests.TestSupport;

public sealed class FakeDialogService : IDialogService
{
    public List<string> Successes { get; } = new();
    public List<string> Infos { get; } = new();
    public List<string> Warnings { get; } = new();
    public List<string> Errors { get; } = new();

    public void ShowSuccess(string message, string? title = null) => Successes.Add(message);
    public void ShowInfo(string message, string? title = null) => Infos.Add(message);
    public void ShowWarning(string message, string? title = null) => Warnings.Add(message);
    public void ShowError(string message, string? title = null) => Errors.Add(message);

    public bool ShowConfirm(string message, string? title = null, string? confirmText = null, string? cancelText = null) => true;
    public bool ShowYesNo(string message, string? title = null, string? yesText = null, string? noText = null) => true;

    public Task<bool> ShowEditUserDialogAsync(User user, IEnumerable<Role> availableRoles) => Task.FromResult(false);

    public string? ShowOpenFileDialog(string filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp|All Files|*.*", string? title = null, string? initialDirectory = null) => null;
}
