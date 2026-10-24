using System.IO;
using System.Text.Json;

namespace SmartPS.Services.Preferences;

/// <summary>Kích thước, vị trí và trạng thái cửa sổ chính lần đóng gần nhất.</summary>
public sealed class WindowPlacement
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool IsMaximized { get; set; }
}

/// <summary>Các lựa chọn của người dùng trên máy này. Giá trị null = chưa chọn, dùng mặc định của ứng dụng.</summary>
public sealed class UserPreferences
{
    /// <summary>"vi-VN", "en-US" hoặc "ja-JP".</summary>
    public string? Language { get; set; }

    public bool? GateAutoMode { get; set; }
    public bool? GateAutoCheckoutAllVehicles { get; set; }
    public bool? GateCameraWatcher { get; set; }

    public WindowPlacement? MainWindow { get; set; }

    public UserPreferences Clone() => new()
    {
        Language = Language,
        GateAutoMode = GateAutoMode,
        GateAutoCheckoutAllVehicles = GateAutoCheckoutAllVehicles,
        GateCameraWatcher = GateCameraWatcher,
        MainWindow = MainWindow is null ? null : new WindowPlacement
        {
            Left = MainWindow.Left,
            Top = MainWindow.Top,
            Width = MainWindow.Width,
            Height = MainWindow.Height,
            IsMaximized = MainWindow.IsMaximized
        }
    };
}

/// <summary>Lưu các lựa chọn của người dùng để lần mở ứng dụng sau giữ nguyên.</summary>
public interface IUserPreferencesStore
{
    /// <summary>Bản sao các lựa chọn hiện tại (sửa bản sao không ảnh hưởng tới dữ liệu đã lưu).</summary>
    UserPreferences Current { get; }

    /// <summary>Sửa và ghi ngay xuống đĩa.</summary>
    void Update(Action<UserPreferences> change);
}

/// <summary>
/// Lưu vào %APPDATA%\SmartPS\preferences.json (thư mục Roaming của tài khoản Windows). Tệp hỏng hoặc không đọc được
/// thì dùng mặc định; lỗi ghi tệp không làm gián đoạn ứng dụng.
/// </summary>
public sealed class UserPreferencesStore : IUserPreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly object _gate = new();
    private UserPreferences _preferences;

    public UserPreferencesStore()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SmartPS", "preferences.json"))
    {
    }

    public UserPreferencesStore(string filePath)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        _preferences = Load();
    }

    public string FilePath => _filePath;

    public UserPreferences Current
    {
        get
        {
            lock (_gate) return _preferences.Clone();
        }
    }

    public void Update(Action<UserPreferences> change)
    {
        ArgumentNullException.ThrowIfNull(change);

        lock (_gate)
        {
            change(_preferences);
            Save(_preferences);
        }
    }

    private UserPreferences Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return new UserPreferences();

            return JsonSerializer.Deserialize<UserPreferences>(File.ReadAllText(_filePath)) ?? new UserPreferences();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new UserPreferences();
        }
    }

    private void Save(UserPreferences preferences)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

            // Ghi ra tệp tạm rồi thay thế để tắt máy giữa chừng không làm hỏng tệp cũ
            var tempPath = _filePath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(preferences, JsonOptions));
            File.Move(tempPath, _filePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"[UserPreferencesStore]: Không ghi được {_filePath}: {ex.Message}");
        }
    }
}
