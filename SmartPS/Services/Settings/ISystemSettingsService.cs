using SmartPS.Services.Common;

namespace SmartPS.Services.Settings;

/// <summary>Tham số vận hành chỉnh được trên màn Cài đặt hệ thống (bảng ParkingSettings).</summary>
public sealed record OperationalSettings(int OverstayThresholdHours, int MaxVehiclesPerHousehold)
{
    public const int MinOverstayHours = 1;
    public const int MaxOverstayHours = 720;
    public const int MinVehiclesPerHousehold = 1;
    public const int MaxVehiclesPerHouseholdLimit = 20;
}

/// <summary>Tình trạng kết nối CSDL; không chứa mật khẩu.</summary>
public sealed record DatabaseStatus(
    bool IsOnline,
    string Host,
    int Port,
    string Database,
    string? ServerVersion,
    int AppliedMigrations,
    int PendingMigrations,
    string? LastMigration,
    string? Error);

public interface ISystemSettingsService
{
    Task<OperationalSettings> GetOperationalSettingsAsync(CancellationToken cancellationToken = default);

    /// <summary>Lưu tham số vận hành (quyền Settings.Manage), ghi nhật ký SETTINGS_UPDATE.</summary>
    Task<OperationResult> UpdateOperationalSettingsAsync(OperationalSettings settings, CancellationToken cancellationToken = default);

    Task<DatabaseStatus> GetDatabaseStatusAsync(CancellationToken cancellationToken = default);
}
