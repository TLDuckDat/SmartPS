using Microsoft.EntityFrameworkCore;
using System.IO;

namespace SmartPS.Data;

/// <summary>
/// Áp dụng migrations rồi nạp dữ liệu mặc định bằng script seed có thể chạy lại.
/// </summary>
public static class DbInitializer
{
    public static async Task InitializeAsync(SmartPsDbContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        await context.Database.MigrateAsync(cancellationToken);

        var seedPath = Path.Combine(AppContext.BaseDirectory, "seed_data.sql");
        if (!File.Exists(seedPath))
            throw new FileNotFoundException("Không tìm thấy seed_data.sql để khởi tạo dữ liệu mặc định.", seedPath);

        var seedSql = await File.ReadAllTextAsync(seedPath, cancellationToken);
        await context.Database.ExecuteSqlRawAsync(seedSql, cancellationToken);
    }
}
