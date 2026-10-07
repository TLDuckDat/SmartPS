using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SmartPS.Migrations
{
    /// <inheritdoc />
    public partial class AddShiftManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Old pending QR payments have no checkout user/shift to attach to the new ledger.
            migrationBuilder.Sql("""
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM "Payments" WHERE "Status" IN (0, 1)) THEN
        RAISE EXCEPTION 'Resolve existing Created/Pending payments before applying AddShiftManagement.';
    END IF;
END $$;
""");

            migrationBuilder.AddColumn<int>(
                name: "CheckoutUserId",
                table: "Payments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ShiftId",
                table: "Payments",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Shifts",
                columns: table => new
                {
                    ShiftId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OpenedByUserId = table.Column<int>(type: "integer", nullable: false),
                    OpenedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BeginningCash = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpectedCash = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    ActualCash = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Difference = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    ReviewedByUserId = table.Column<int>(type: "integer", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ManagerNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shifts", x => x.ShiftId);
                    table.ForeignKey(
                        name: "FK_Shifts_Users_OpenedByUserId",
                        column: x => x.OpenedByUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Shifts_Users_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "FinancialTransactions",
                columns: table => new
                {
                    TransactionId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TransactionCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ShiftId = table.Column<int>(type: "integer", nullable: false),
                    ParkingSessionId = table.Column<int>(type: "integer", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    PaymentMethod = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReferenceCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialTransactions", x => x.TransactionId);
                    table.ForeignKey(
                        name: "FK_FinancialTransactions_ParkingSessions_ParkingSessionId",
                        column: x => x.ParkingSessionId,
                        principalTable: "ParkingSessions",
                        principalColumn: "SessionId",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_FinancialTransactions_Shifts_ShiftId",
                        column: x => x.ShiftId,
                        principalTable: "Shifts",
                        principalColumn: "ShiftId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinancialTransactions_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_CheckoutUserId",
                table: "Payments",
                column: "CheckoutUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_ShiftId",
                table: "Payments",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialTransactions_CreatedAt",
                table: "FinancialTransactions",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialTransactions_CreatedByUserId",
                table: "FinancialTransactions",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialTransactions_OneParkingFeePerSession",
                table: "FinancialTransactions",
                columns: new[] { "ParkingSessionId", "Type" },
                unique: true,
                filter: "\"ParkingSessionId\" IS NOT NULL AND \"Type\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialTransactions_ParkingSessionId",
                table: "FinancialTransactions",
                column: "ParkingSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialTransactions_PaymentMethod",
                table: "FinancialTransactions",
                column: "PaymentMethod");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialTransactions_ShiftId",
                table: "FinancialTransactions",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialTransactions_TransactionCode",
                table: "FinancialTransactions",
                column: "TransactionCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinancialTransactions_Type",
                table: "FinancialTransactions",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_Shifts_OneActivePerUser",
                table: "Shifts",
                column: "OpenedByUserId",
                unique: true,
                filter: "\"Status\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Shifts_OpenedAt",
                table: "Shifts",
                column: "OpenedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Shifts_OpenedByUserId_Status",
                table: "Shifts",
                columns: new[] { "OpenedByUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Shifts_ReviewedByUserId",
                table: "Shifts",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Shifts_Status",
                table: "Shifts",
                column: "Status");

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_Shifts_ShiftId",
                table: "Payments",
                column: "ShiftId",
                principalTable: "Shifts",
                principalColumn: "ShiftId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_Users_CheckoutUserId",
                table: "Payments",
                column: "CheckoutUserId",
                principalTable: "Users",
                principalColumn: "UserId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
INSERT INTO "Permissions" ("PermissionName", "Description") VALUES
('Shift.View', 'Xem ca trực và lịch sử đối soát'),
('Shift.Open', 'Mở ca trực'),
('Shift.Close', 'Đóng ca trực'),
('Shift.Review', 'Quản lý xác nhận ca'),
('Shift.Adjust', 'Điều chỉnh ca đã khóa')
ON CONFLICT ("PermissionName") DO NOTHING;

INSERT INTO "RolePermissions" ("RoleId", "PermissionId")
SELECT r."RoleId", p."PermissionId" FROM "Roles" r CROSS JOIN "Permissions" p
WHERE r."RoleName" IN ('Admin', 'Manager') AND p."PermissionName" LIKE 'Shift.%'
ON CONFLICT ("RoleId", "PermissionId") DO NOTHING;

INSERT INTO "RolePermissions" ("RoleId", "PermissionId")
SELECT r."RoleId", p."PermissionId" FROM "Roles" r JOIN "Permissions" p
ON p."PermissionName" IN ('Shift.View', 'Shift.Open', 'Shift.Close')
WHERE r."RoleName" = 'Operator'
ON CONFLICT ("RoleId", "PermissionId") DO NOTHING;
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
DELETE FROM "RolePermissions" WHERE "PermissionId" IN
    (SELECT "PermissionId" FROM "Permissions" WHERE "PermissionName" LIKE 'Shift.%');
DELETE FROM "Permissions" WHERE "PermissionName" LIKE 'Shift.%';
""");
            migrationBuilder.DropForeignKey(
                name: "FK_Payments_Shifts_ShiftId",
                table: "Payments");

            migrationBuilder.DropForeignKey(
                name: "FK_Payments_Users_CheckoutUserId",
                table: "Payments");

            migrationBuilder.DropTable(
                name: "FinancialTransactions");

            migrationBuilder.DropTable(
                name: "Shifts");

            migrationBuilder.DropIndex(
                name: "IX_Payments_CheckoutUserId",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_ShiftId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "CheckoutUserId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ShiftId",
                table: "Payments");
        }
    }
}
