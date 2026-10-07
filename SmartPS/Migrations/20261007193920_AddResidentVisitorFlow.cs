using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SmartPS.Migrations
{
    /// <inheritdoc />
    public partial class AddResidentVisitorFlow : Migration
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

            migrationBuilder.AddColumn<string>(
                name: "ApartmentCode",
                table: "Customers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Building",
                table: "Customers",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsResident",
                table: "Customers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "BlacklistEntries",
                columns: table => new
                {
                    BlacklistEntryId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LicensePlate = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    RemovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RemovedByUserId = table.Column<int>(type: "integer", nullable: true),
                    RemoveReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlacklistEntries", x => x.BlacklistEntryId);
                    table.ForeignKey(
                        name: "FK_BlacklistEntries_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_BlacklistEntries_Users_RemovedByUserId",
                        column: x => x.RemovedByUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "CustomerVehicles",
                columns: table => new
                {
                    CustomerVehicleId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CustomerId = table.Column<int>(type: "integer", nullable: false),
                    LicensePlate = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    VehicleTypeId = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RemovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerVehicles", x => x.CustomerVehicleId);
                    table.ForeignKey(
                        name: "FK_CustomerVehicles_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "CustomerId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerVehicles_VehicleTypes_VehicleTypeId",
                        column: x => x.VehicleTypeId,
                        principalTable: "VehicleTypes",
                        principalColumn: "VehicleTypeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MonthlyTicketPurchases",
                columns: table => new
                {
                    MonthlyTicketPurchaseId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TicketId = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    PlanId = table.Column<int>(type: "integer", nullable: true),
                    Price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PeriodStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PeriodEndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonthlyTicketPurchases", x => x.MonthlyTicketPurchaseId);
                    table.ForeignKey(
                        name: "FK_MonthlyTicketPurchases_MonthlyTicketPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "MonthlyTicketPlans",
                        principalColumn: "PlanId",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MonthlyTicketPurchases_MonthlyTickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "MonthlyTickets",
                        principalColumn: "TicketId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MonthlyTicketPurchases_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_ApartmentCode",
                table: "Customers",
                column: "ApartmentCode");

            migrationBuilder.CreateIndex(
                name: "IX_BlacklistEntries_CreatedByUserId",
                table: "BlacklistEntries",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BlacklistEntries_LicensePlate",
                table: "BlacklistEntries",
                column: "LicensePlate");

            migrationBuilder.CreateIndex(
                name: "IX_BlacklistEntries_LicensePlate_Active",
                table: "BlacklistEntries",
                column: "LicensePlate",
                unique: true,
                filter: "\"IsActive\"");

            migrationBuilder.CreateIndex(
                name: "IX_BlacklistEntries_RemovedByUserId",
                table: "BlacklistEntries",
                column: "RemovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerVehicles_CustomerId",
                table: "CustomerVehicles",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerVehicles_LicensePlate_Active",
                table: "CustomerVehicles",
                column: "LicensePlate",
                unique: true,
                filter: "\"IsActive\"");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerVehicles_VehicleTypeId",
                table: "CustomerVehicles",
                column: "VehicleTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyTicketPurchases_CreatedAtUtc",
                table: "MonthlyTicketPurchases",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyTicketPurchases_CreatedByUserId",
                table: "MonthlyTicketPurchases",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyTicketPurchases_PlanId",
                table: "MonthlyTicketPurchases",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyTicketPurchases_TicketId",
                table: "MonthlyTicketPurchases",
                column: "TicketId");

            // Gỡ liên kết ô của các phiên đang hoạt động trùng ô (giữ phiên mới nhất) trước khi tạo chỉ mục duy nhất.
            migrationBuilder.Sql("""
                UPDATE "ParkingSessions" ps SET "SlotId" = NULL
                WHERE ps."Status" = 0 AND ps."SlotId" IS NOT NULL AND EXISTS (
                  SELECT 1 FROM "ParkingSessions" o
                  WHERE o."Status" = 0 AND o."SlotId" = ps."SlotId"
                    AND (o."CheckInTime" > ps."CheckInTime" OR (o."CheckInTime" = ps."CheckInTime" AND o."SessionId" > ps."SessionId")));
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ParkingSessions_SlotId_Active",
                table: "ParkingSessions",
                column: "SlotId",
                unique: true,
                filter: "\"Status\" = 0 AND \"SlotId\" IS NOT NULL");

            // Chuẩn hoá biển số vé tháng và chuyển DefaultLicensePlate thành CustomerVehicles.
            migrationBuilder.Sql("""
                UPDATE "MonthlyTickets"
                SET "RegisteredLicensePlate" = upper(regexp_replace("RegisteredLicensePlate", '[^a-zA-Z0-9]', '', 'g'))
                WHERE "RegisteredLicensePlate" <> upper(regexp_replace("RegisteredLicensePlate", '[^a-zA-Z0-9]', '', 'g'));
                """);

            migrationBuilder.Sql("""
                WITH src AS (
                  SELECT c."CustomerId", left(upper(regexp_replace(c."DefaultLicensePlate", '[^a-zA-Z0-9]', '', 'g')), 20) AS plate,
                         COALESCE(c."VehicleTypeId",
                                  (SELECT t."VehicleTypeId" FROM "MonthlyTickets" t WHERE t."CustomerId" = c."CustomerId" ORDER BY t."TicketId" DESC LIMIT 1),
                                  (SELECT min(v."VehicleTypeId") FROM "VehicleTypes" v)) AS vt,
                         c."CreatedAt" AS created, 0 AS prio
                  FROM "Customers" c
                  WHERE c."DefaultLicensePlate" IS NOT NULL AND btrim(c."DefaultLicensePlate") <> ''
                  UNION ALL
                  SELECT t."CustomerId", left(t."RegisteredLicensePlate", 20), t."VehicleTypeId", t."CreatedAt", 1
                  FROM "MonthlyTickets" t WHERE t."RegisteredLicensePlate" <> ''
                ),
                dedup AS (SELECT DISTINCT ON ("CustomerId", plate) * FROM src WHERE plate <> '' AND vt IS NOT NULL ORDER BY "CustomerId", plate, prio),
                ranked AS (SELECT d.*, row_number() OVER (PARTITION BY plate ORDER BY prio, "CustomerId") AS rn FROM dedup d)
                INSERT INTO "CustomerVehicles" ("CustomerId", "LicensePlate", "VehicleTypeId", "IsActive", "CreatedAt")
                SELECT "CustomerId", plate, vt, rn = 1, created FROM ranked;
                """);

            // Quyền mới: Admin có tất cả, Manager có cả 3, Operator chỉ xem.
            migrationBuilder.Sql("""
                INSERT INTO "Permissions" ("PermissionName","Description") VALUES
                ('Customer.View','Xem khách hàng, vé tháng và danh sách đen'),
                ('Customer.Manage','Quản lý khách hàng, phương tiện và vé tháng'),
                ('Blacklist.Manage','Quản lý danh sách đen biển số')
                ON CONFLICT ("PermissionName") DO NOTHING;

                INSERT INTO "RolePermissions" ("RoleId","PermissionId")
                SELECT r."RoleId", p."PermissionId" FROM "Roles" r CROSS JOIN "Permissions" p WHERE r."RoleName"='Admin'
                ON CONFLICT ("RoleId","PermissionId") DO NOTHING;

                INSERT INTO "RolePermissions" ("RoleId","PermissionId")
                SELECT r."RoleId", p."PermissionId" FROM "Roles" r JOIN "Permissions" p
                  ON p."PermissionName" IN ('Customer.View','Customer.Manage','Blacklist.Manage')
                WHERE r."RoleName"='Manager' ON CONFLICT ("RoleId","PermissionId") DO NOTHING;

                INSERT INTO "RolePermissions" ("RoleId","PermissionId")
                SELECT r."RoleId", p."PermissionId" FROM "Roles" r JOIN "Permissions" p ON p."PermissionName" = 'Customer.View'
                WHERE r."RoleName"='Operator' ON CONFLICT ("RoleId","PermissionId") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM "RolePermissions" WHERE "PermissionId" IN (SELECT "PermissionId" FROM "Permissions"
                  WHERE "PermissionName" IN ('Customer.View','Customer.Manage','Blacklist.Manage'));
                DELETE FROM "Permissions" WHERE "PermissionName" IN ('Customer.View','Customer.Manage','Blacklist.Manage');
                UPDATE "Customers" SET "Type" = 2 WHERE "Type" = 3;
                UPDATE "ParkingSessions" SET "CustomerType" = 2 WHERE "CustomerType" = 3;
                DELETE FROM "CustomerTiers" WHERE "CustomerType" = 3;
                UPDATE "Customers" c SET "DefaultLicensePlate" = v."LicensePlate"
                FROM (SELECT DISTINCT ON ("CustomerId") "CustomerId", "LicensePlate" FROM "CustomerVehicles"
                      ORDER BY "CustomerId", "IsActive" DESC, "CreatedAt") v
                WHERE c."CustomerId" = v."CustomerId" AND (c."DefaultLicensePlate" IS NULL OR c."DefaultLicensePlate" = '');
                """);

            migrationBuilder.DropTable(
                name: "MonthlyTicketPurchases");

            migrationBuilder.DropIndex(
                name: "IX_ParkingSessions_SlotId_Active",
                table: "ParkingSessions");

            migrationBuilder.DropTable(
                name: "BlacklistEntries");

            migrationBuilder.DropTable(
                name: "CustomerVehicles");

            migrationBuilder.DropIndex(
                name: "IX_Customers_ApartmentCode",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Audience",
                table: "ParkingZones");

            migrationBuilder.DropColumn(
                name: "ApartmentCode",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Building",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "IsResident",
                table: "Customers");
        }
    }
}
