using Microsoft.EntityFrameworkCore;

namespace SmartPS.Data;

/// <summary>
/// Khởi tạo cơ sở dữ liệu Code First: Tự động kiểm tra và áp dụng toàn bộ Migrations
/// </summary>
public static class DbInitializer
{
    public static async Task InitializeAsync(SmartPsDbContext context)
    {
        try
        {
            // 1. Tự động áp dụng Migration và tạo Database nếu chưa tồn tại
            await context.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            // Bắt lỗi nếu bảng hoặc đối tượng đã tồn tại sẵn, ghi nhận log nhưng không cản trở ứng dụng
            System.Diagnostics.Debug.WriteLine($"[SmartPS DbInitializer Warning]: {ex.Message}");
        }
    }
}
