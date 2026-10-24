using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.IO;

namespace SmartPS.Data;

/// <summary>
/// Tạo/cấu hình database, áp dụng migrations rồi nạp dữ liệu mặc định bằng script seed có thể chạy lại.
/// </summary>
public static class DbInitializer
{
    public static async Task InitializeAsync(SmartPsDbContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        await EnsureDatabaseConfiguredAsync(context.Database.GetConnectionString()!, cancellationToken);
        await context.Database.MigrateAsync(cancellationToken);

        var seedPath = Path.Combine(AppContext.BaseDirectory, "seed_data.sql");
        if (!File.Exists(seedPath))
            throw new FileNotFoundException("Không tìm thấy seed_data.sql để khởi tạo dữ liệu mặc định.", seedPath);

        var seedSql = await File.ReadAllTextAsync(seedPath, cancellationToken);
        await context.Database.ExecuteSqlRawAsync(seedSql, cancellationToken);
    }

    /// <summary>
    /// Tạo database với collation của model (nếu chưa có) và bật READ_COMMITTED_SNAPSHOT trước khi migrate.
    /// Đổi collation/RCSI làm hỏng các kết nối đang nằm trong pool ("Resetting the connection results in a different state
    /// than the initial login"), nên việc này chạy trên kết nối riêng không dùng pool và xoá pool sau đó.
    /// </summary>
    public static async Task EnsureDatabaseConfiguredAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        var target = new SqlConnectionStringBuilder(connectionString);
        var databaseName = target.InitialCatalog;
        if (string.IsNullOrEmpty(databaseName))
        {
            return;
        }

        var master = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master", Pooling = false };
        await using (var connection = new SqlConnection(master.ConnectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(ConfigureDatabaseSql, connection) { CommandTimeout = 120 };
            command.Parameters.AddWithValue("db", databaseName);
            command.Parameters.AddWithValue("collation", SmartPsDbContext.DatabaseCollation);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        SqlConnection.ClearAllPools();
    }

    // Đổi collation chỉ được khi database còn trống; database đã migrate đúng collation thì bỏ qua
    private const string ConfigureDatabaseSql = """
        DECLARE @name nvarchar(260) = QUOTENAME(@db);
        IF DB_ID(@db) IS NULL
            EXEC(N'CREATE DATABASE ' + @name + N' COLLATE ' + @collation);
        IF CONVERT(nvarchar(128), DATABASEPROPERTYEX(@db, 'Collation')) <> @collation
            EXEC(N'ALTER DATABASE ' + @name + N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE; '
               + N'ALTER DATABASE ' + @name + N' COLLATE ' + @collation + N'; '
               + N'ALTER DATABASE ' + @name + N' SET MULTI_USER;');
        IF (SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = @db) = 0
            EXEC(N'ALTER DATABASE ' + @name + N' SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE');
        """;
}
