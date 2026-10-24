using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace SmartPS.Data;

/// <summary>
/// Ghi: giờ địa phương được đổi sang UTC, giờ UTC/không rõ Kind giữ nguyên. Đọc: gắn Kind = Utc
/// (cột datetime2 của SQL Server không lưu múi giờ).
/// </summary>
public sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter()
        : base(
            v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : v,
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
    {
    }
}
