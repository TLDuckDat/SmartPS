using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartPS.Migrations
{
    /// <inheritdoc />
    public partial class AddPricingPolicyAndSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GracePeriodMinutes",
                table: "PricingRules",
                type: "int",
                nullable: false,
                defaultValue: 15);

            migrationBuilder.AddColumn<decimal>(
                name: "OverQuotaDailyPrice",
                table: "PricingRules",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OvernightSurcharge",
                table: "PricingRules",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "FeeBreakdownJson",
                table: "ParkingSessions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PricingPolicyVersionId",
                table: "ParkingSessions",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PricingPolicyVersions",
                columns: table => new
                {
                    VersionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VehicleTypeId = table.Column<int>(type: "int", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    Block4hPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DailyPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Monthly1Price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Monthly3Price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Monthly6Price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    GracePeriodMinutes = table.Column<int>(type: "int", nullable: false),
                    OvernightSurcharge = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    OverQuotaDailyPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    BlockHours = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PricingPolicyVersions", x => x.VersionId);
                    table.ForeignKey(
                        name: "FK_PricingPolicyVersions_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PricingPolicyVersions_VehicleTypes_VehicleTypeId",
                        column: x => x.VehicleTypeId,
                        principalTable: "VehicleTypes",
                        principalColumn: "VehicleTypeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ParkingSessions_PricingPolicyVersionId",
                table: "ParkingSessions",
                column: "PricingPolicyVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_PricingPolicyVersions_CreatedByUserId",
                table: "PricingPolicyVersions",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PricingPolicyVersions_VehicleTypeId_VersionNumber",
                table: "PricingPolicyVersions",
                columns: new[] { "VehicleTypeId", "VersionNumber" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ParkingSessions_PricingPolicyVersions_PricingPolicyVersionId",
                table: "ParkingSessions",
                column: "PricingPolicyVersionId",
                principalTable: "PricingPolicyVersions",
                principalColumn: "VersionId",
                onDelete: ReferentialAction.Restrict);

            // Dữ liệu cũ: giá vượt định mức bắt đầu bằng giá ngày hiện hành, rồi chụp phiên bản 1 từ PricingRules.
            migrationBuilder.Sql("UPDATE [PricingRules] SET [OverQuotaDailyPrice] = [DailyPrice];");
            migrationBuilder.Sql(@"
INSERT INTO [PricingPolicyVersions]
    ([VehicleTypeId], [VersionNumber], [Block4hPrice], [DailyPrice], [Monthly1Price], [Monthly3Price], [Monthly6Price],
     [GracePeriodMinutes], [OvernightSurcharge], [OverQuotaDailyPrice], [BlockHours], [CreatedAtUtc], [CreatedByUserId])
SELECT [VehicleTypeId], 1, [Block4hPrice], [DailyPrice], [Monthly1Price], [Monthly3Price], [Monthly6Price],
       [GracePeriodMinutes], [OvernightSurcharge], [OverQuotaDailyPrice], 4, SYSUTCDATETIME(), NULL
FROM [PricingRules];");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ParkingSessions_PricingPolicyVersions_PricingPolicyVersionId",
                table: "ParkingSessions");

            migrationBuilder.DropTable(
                name: "PricingPolicyVersions");

            migrationBuilder.DropIndex(
                name: "IX_ParkingSessions_PricingPolicyVersionId",
                table: "ParkingSessions");

            migrationBuilder.DropColumn(
                name: "GracePeriodMinutes",
                table: "PricingRules");

            migrationBuilder.DropColumn(
                name: "OverQuotaDailyPrice",
                table: "PricingRules");

            migrationBuilder.DropColumn(
                name: "OvernightSurcharge",
                table: "PricingRules");

            migrationBuilder.DropColumn(
                name: "FeeBreakdownJson",
                table: "ParkingSessions");

            migrationBuilder.DropColumn(
                name: "PricingPolicyVersionId",
                table: "ParkingSessions");
        }
    }
}
