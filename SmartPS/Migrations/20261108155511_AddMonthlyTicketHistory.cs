using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartPS.Migrations
{
    /// <inheritdoc />
    public partial class AddMonthlyTicketHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MonthlyTicketHistories",
                columns: table => new
                {
                    HistoryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TicketId = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<int>(type: "int", nullable: false),
                    OldVehicleId = table.Column<int>(type: "int", nullable: true),
                    NewVehicleId = table.Column<int>(type: "int", nullable: true),
                    OldLicensePlate = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    NewLicensePlate = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    OldEndDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NewEndDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AddedMonths = table.Column<int>(type: "int", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PaymentMethod = table.Column<int>(type: "int", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PerformedByUserId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonthlyTicketHistories", x => x.HistoryId);
                    table.ForeignKey(
                        name: "FK_MonthlyTicketHistories_MonthlyTickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "MonthlyTickets",
                        principalColumn: "TicketId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyTicketHistories_CreatedAt",
                table: "MonthlyTicketHistories",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyTicketHistories_TicketId",
                table: "MonthlyTicketHistories",
                column: "TicketId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MonthlyTicketHistories");
        }
    }
}
