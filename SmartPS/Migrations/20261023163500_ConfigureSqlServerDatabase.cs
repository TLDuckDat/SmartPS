using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SmartPS.Data;

#nullable disable

namespace SmartPS.Migrations
{
    /// <summary>
    /// Cấu hình database trước khi tạo bảng: collation phân biệt hoa/thường và READ_COMMITTED_SNAPSHOT.
    /// Tách riêng vì ALTER DATABASE không chạy được trong giao dịch (migration chỉ gồm thao tác không giao dịch).
    /// </summary>
    [DbContext(typeof(SmartPsDbContext))]
    [Migration("20261023163500_ConfigureSqlServerDatabase")]
    public partial class ConfigureSqlServerDatabase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            SqlServerObjects.ConfigureDatabase(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT OFF WITH ROLLBACK IMMEDIATE;",
                suppressTransaction: true);
        }
    }
}
