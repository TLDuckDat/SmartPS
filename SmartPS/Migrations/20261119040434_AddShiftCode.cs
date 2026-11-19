using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartPS.Migrations
{
    /// <inheritdoc />
    public partial class AddShiftCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ShiftCode",
                table: "Shifts",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            // Điền mã cho các ca đã có: CA-yyMMdd-NN theo thứ tự giờ mở ca trong ngày (giờ Việt Nam)
            migrationBuilder.Sql("""
                WITH c AS (
                    SELECT ShiftId,
                           CONVERT(char(6), DATEADD(HOUR, 7, OpenedAt), 12) AS D,
                           ROW_NUMBER() OVER (PARTITION BY CAST(DATEADD(HOUR, 7, OpenedAt) AS date) ORDER BY OpenedAt, ShiftId) AS N
                    FROM dbo.Shifts
                )
                UPDATE s SET s.ShiftCode = N'CA-' + c.D + N'-' + RIGHT(N'0' + CAST(c.N AS nvarchar(10)), CASE WHEN c.N > 99 THEN LEN(CAST(c.N AS nvarchar(10))) ELSE 2 END)
                FROM dbo.Shifts s JOIN c ON c.ShiftId = s.ShiftId;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Shifts_ShiftCode",
                table: "Shifts",
                column: "ShiftCode",
                unique: true,
                filter: "[ShiftCode] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Shifts_ShiftCode",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "ShiftCode",
                table: "Shifts");
        }
    }
}
