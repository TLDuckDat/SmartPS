using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SmartPS.Services.Auth;

/// <summary>Thông tin đăng nhập đã ghi nhớ trên máy này.</summary>
public sealed record SavedCredentials(string Username, string Password);

/// <summary>Ghi nhớ tên đăng nhập và mật khẩu cho lần đăng nhập sau.</summary>
public interface ICredentialStore
{
    /// <summary>Thông tin đã lưu; null khi chưa lưu hoặc không giải mã được (khác tài khoản Windows, tệp hỏng).</summary>
    SavedCredentials? Load();

    void Save(string username, string password);

    void Clear();
}

/// <summary>
/// Lưu vào %LOCALAPPDATA%\SmartPS\credentials.dat. Mật khẩu được mã hoá bằng Windows DPAPI (phạm vi CurrentUser):
/// chỉ chính tài khoản Windows đó trên máy đó giải mã được, tệp bị chép sang máy khác thì vô dụng.
/// </summary>
public sealed class CredentialStore : ICredentialStore
{
    // Entropy riêng của ứng dụng: ứng dụng khác cùng tài khoản Windows không giải mã được bằng DPAPI mặc định
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SmartPS.RememberedLogin.v1");

    private readonly string _filePath;

    public CredentialStore()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SmartPS", "credentials.dat"))
    {
    }

    public CredentialStore(string filePath)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    }

    public SavedCredentials? Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return null;
            }

            var stored = JsonSerializer.Deserialize<StoredCredentials>(File.ReadAllText(_filePath));
            if (stored is null || string.IsNullOrWhiteSpace(stored.Username) || string.IsNullOrEmpty(stored.ProtectedPassword))
            {
                return null;
            }

            var password = ProtectedData.Unprotect(Convert.FromBase64String(stored.ProtectedPassword), Entropy, DataProtectionScope.CurrentUser);
            return new SavedCredentials(stored.Username, Encoding.UTF8.GetString(password));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException or CryptographicException)
        {
            // Tệp hỏng hoặc của tài khoản Windows khác: coi như chưa ghi nhớ và xoá để không thử lại mỗi lần mở
            Clear();
            return null;
        }
    }

    public void Save(string username, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentNullException.ThrowIfNull(password);

        var protectedPassword = ProtectedData.Protect(Encoding.UTF8.GetBytes(password), Entropy, DataProtectionScope.CurrentUser);
        var json = JsonSerializer.Serialize(new StoredCredentials(username.Trim(), Convert.ToBase64String(protectedPassword)));

        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        File.WriteAllText(_filePath, json);
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"[CredentialStore] Could not delete saved credentials: {ex.Message}");
        }
    }

    private sealed record StoredCredentials(string Username, string ProtectedPassword);
}
