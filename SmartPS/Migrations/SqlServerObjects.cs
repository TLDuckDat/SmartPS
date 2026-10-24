using Microsoft.EntityFrameworkCore.Migrations;
using SmartPS.Data;
using SmartPS.Data.Configurations.Audit;

namespace SmartPS.Migrations;

/// <summary>
/// Đối tượng SQL Server viết tay mà EF Core không tự sinh: collation + RCSI của database, hàm chuẩn hoá biển số,
/// và cơ chế chỉ-ghi-thêm của bảng AuditLogs (trigger chặn UPDATE/DELETE, bảng có khoá ngoại chặn TRUNCATE).
/// </summary>
internal static class SqlServerObjects
{
    public const string NormalizePlateFunction = "fn_NormalizePlate";
    public const string AuditLogTruncateGuardTable = "AuditLogsTruncateGuard";

    /// <summary>
    /// Chạy trước khi tạo bảng. Ứng dụng đã cấu hình sẵn trong DbInitializer.EnsureDatabaseConfiguredAsync nên ở đây chỉ
    /// ALTER khi còn sai (trường hợp chạy "dotnet ef database update" trên database tạo tay từ SSMS).
    /// ALTER DATABASE không chạy được trong giao dịch.
    /// </summary>
    public static void ConfigureDatabase(MigrationBuilder migrationBuilder)
    {
        // Database tạo sẵn từ SSMS vẫn mang collation mặc định của máy chủ: đặt lại cho đúng model
        migrationBuilder.Sql($"""
            IF CONVERT(nvarchar(128), DATABASEPROPERTYEX(DB_NAME(), 'Collation')) <> N'{SmartPsDbContext.DatabaseCollation}'
                EXEC(N'ALTER DATABASE CURRENT COLLATE {SmartPsDbContext.DatabaseCollation}');
            """, suppressTransaction: true);

        // Đọc không chặn ghi (giống MVCC của PostgreSQL)
        migrationBuilder.Sql("""
            IF (SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = DB_NAME()) = 0
                EXEC(N'ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE');
            """, suppressTransaction: true);
    }

    /// <summary>Chạy sau khi tạo bảng.</summary>
    public static void Create(MigrationBuilder migrationBuilder)
    {
        // Giống LicensePlateNormalizer.Normalize: chỉ giữ chữ cái/chữ số ASCII rồi viết hoa.
        // So khớp nhị phân để [a-zA-Z0-9] không khớp chữ có dấu.
        migrationBuilder.Sql($"""
            CREATE FUNCTION dbo.{NormalizePlateFunction}(@plate nvarchar(100))
            RETURNS nvarchar(100)
            WITH SCHEMABINDING, RETURNS NULL ON NULL INPUT
            AS
            BEGIN
                DECLARE @i int = PATINDEX(N'%[^a-zA-Z0-9]%', @plate COLLATE Latin1_General_BIN2);
                WHILE @i > 0
                BEGIN
                    SET @plate = STUFF(@plate, @i, 1, N'');
                    SET @i = PATINDEX(N'%[^a-zA-Z0-9]%', @plate COLLATE Latin1_General_BIN2);
                END;
                RETURN UPPER(@plate);
            END;
            """);

        // Nhật ký kiểm toán chỉ được ghi thêm: mọi câu lệnh UPDATE/DELETE (kể cả không chạm dòng nào) bị từ chối
        // và giao dịch bị huỷ, giống trigger FOR EACH STATEMENT trước đây
        migrationBuilder.Sql($"""
            CREATE TRIGGER dbo.{AuditLogConfiguration.AuditLogTriggerName} ON dbo.AuditLogs
            INSTEAD OF UPDATE, DELETE
            AS
            BEGIN
                SET NOCOUNT ON;
                THROW 50001, N'AuditLogs is append-only: UPDATE/DELETE is not allowed.', 1;
            END;
            """);

        // TRUNCATE không kích hoạt trigger. SQL Server từ chối TRUNCATE bảng đang được khoá ngoại tham chiếu,
        // nên một bảng rỗng có khoá ngoại tới AuditLogs đóng vai trò chốt chặn
        migrationBuilder.Sql($"""
            CREATE TABLE dbo.{AuditLogTruncateGuardTable} (
                AuditLogId bigint NULL CONSTRAINT FK_{AuditLogTruncateGuardTable}_AuditLogs REFERENCES dbo.AuditLogs (AuditLogId),
                CONSTRAINT CK_{AuditLogTruncateGuardTable}_AlwaysEmpty CHECK (AuditLogId IS NULL AND 1 = 0)
            );
            """);

        // Chi tiết nhật ký phải là JSON hợp lệ (như kiểu jsonb trước đây)
        migrationBuilder.Sql("ALTER TABLE dbo.AuditLogs ADD CONSTRAINT CK_AuditLogs_Details_IsJson CHECK (ISJSON(Details) = 1);");
    }

    public static void Drop(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql($"DROP TABLE IF EXISTS dbo.{AuditLogTruncateGuardTable};");
        migrationBuilder.Sql($"DROP TRIGGER IF EXISTS dbo.{AuditLogConfiguration.AuditLogTriggerName};");
        migrationBuilder.Sql($"DROP FUNCTION IF EXISTS dbo.{NormalizePlateFunction};");
    }
}
