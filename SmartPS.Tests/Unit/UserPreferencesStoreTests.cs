using SmartPS.Services.Preferences;

namespace SmartPS.Tests.Unit;

/// <summary>
/// Lựa chọn của người dùng (ngôn ngữ, công tắc màn Cổng, cửa sổ chính) được lưu ra tệp và đọc lại ở lần mở sau;
/// tệp hỏng hoặc chưa có thì dùng mặc định.
/// </summary>
public sealed class UserPreferencesStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "SmartPS.Tests", "prefs-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_folder, "preferences.json");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void Missing_file_gives_defaults()
    {
        var prefs = new UserPreferencesStore(FilePath).Current;

        Assert.Null(prefs.Language);
        Assert.Null(prefs.GateAutoMode);
        Assert.Null(prefs.GateCameraWatcher);
        Assert.Null(prefs.MainWindow);
    }

    [Fact]
    public void Saved_choices_survive_a_restart()
    {
        var store = new UserPreferencesStore(FilePath);
        store.Update(p => p.Language = "ja-JP");
        store.Update(p => p.GateAutoMode = false);
        store.Update(p => p.GateCameraWatcher = true);
        store.Update(p => p.MainWindow = new WindowPlacement { Left = 10, Top = 20, Width = 1200, Height = 700, IsMaximized = true });

        var reopened = new UserPreferencesStore(FilePath).Current;

        Assert.Equal("ja-JP", reopened.Language);
        Assert.False(reopened.GateAutoMode);
        Assert.True(reopened.GateCameraWatcher);
        Assert.NotNull(reopened.MainWindow);
        Assert.Equal(1200, reopened.MainWindow!.Width);
        Assert.True(reopened.MainWindow.IsMaximized);
    }

    [Fact]
    public void Corrupt_file_falls_back_to_defaults_and_is_overwritten_on_next_save()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, "{ not json");

        var store = new UserPreferencesStore(FilePath);
        Assert.Null(store.Current.Language);

        store.Update(p => p.Language = "en-US");
        Assert.Equal("en-US", new UserPreferencesStore(FilePath).Current.Language);
    }

    [Fact]
    public void Current_returns_a_copy()
    {
        var store = new UserPreferencesStore(FilePath);
        store.Current.Language = "en-US";

        Assert.Null(store.Current.Language);
    }
}
