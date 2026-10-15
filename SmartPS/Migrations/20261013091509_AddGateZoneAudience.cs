using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartPS.Migrations
{
    /// <inheritdoc />
    public partial class AddGateZoneAudience : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Audience",
                table: "ParkingZones",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Giữ phiên Active mới nhất của mỗi ô, gỡ liên kết ô khỏi các phiên Active cũ hơn (không xoá dữ liệu).
            migrationBuilder.Sql("""
                UPDATE "ParkingSessions" p
                SET "SlotId" = NULL
                WHERE p."Status" = 0 AND p."SlotId" IS NOT NULL
                  AND EXISTS (
                    SELECT 1 FROM "ParkingSessions" n
                    WHERE n."SlotId" = p."SlotId" AND n."Status" = 0
                      AND (n."CheckInTime" > p."CheckInTime"
                           OR (n."CheckInTime" = p."CheckInTime" AND n."SessionId" > p."SessionId")));
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ParkingSessions_SlotId_Active",
                table: "ParkingSessions",
                column: "SlotId",
                unique: true,
                filter: "\"Status\" = 0 AND \"SlotId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ParkingSessions_SlotId_Active",
                table: "ParkingSessions");

            migrationBuilder.DropColumn(
                name: "Audience",
                table: "ParkingZones");
        }
    }
}
