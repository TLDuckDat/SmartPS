using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SmartPS.Migrations
{
    /// <inheritdoc />
    public partial class AddRbacAndAuditTrail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    AuditLogId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: true),
                    Username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RoleName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    EntityId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Details = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    MachineName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PrevHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.AuditLogId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Action_OccurredAtUtc",
                table: "AuditLogs",
                columns: new[] { "Action", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EntityType_EntityId",
                table: "AuditLogs",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_OccurredAtUtc",
                table: "AuditLogs",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_PrevHash",
                table: "AuditLogs",
                column: "PrevHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_UserId",
                table: "AuditLogs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Username",
                table: "AuditLogs",
                column: "Username");

            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION ""fn_AuditLogs_BlockMutation""() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'AuditLogs is append-only: % is not allowed.', TG_OP;
END;
$$;");

            migrationBuilder.Sql(@"CREATE TRIGGER ""TR_AuditLogs_NoUpdate"" BEFORE UPDATE ON ""AuditLogs"" FOR EACH STATEMENT EXECUTE FUNCTION ""fn_AuditLogs_BlockMutation""();");
            migrationBuilder.Sql(@"CREATE TRIGGER ""TR_AuditLogs_NoDelete"" BEFORE DELETE ON ""AuditLogs"" FOR EACH STATEMENT EXECUTE FUNCTION ""fn_AuditLogs_BlockMutation""();");
            migrationBuilder.Sql(@"CREATE TRIGGER ""TR_AuditLogs_NoTruncate"" BEFORE TRUNCATE ON ""AuditLogs"" FOR EACH STATEMENT EXECUTE FUNCTION ""fn_AuditLogs_BlockMutation""();");

            migrationBuilder.Sql(@"
INSERT INTO ""Permissions"" (""PermissionName"", ""Description"") VALUES
('Audit.View', 'Xem nhật ký hệ thống'),
('Audit.Verify', 'Kiểm tra toàn vẹn nhật ký'),
('Payment.Refund', 'Hoàn tiền / huỷ thanh toán'),
('Settings.Manage', 'Quản lý cài đặt hệ thống'),
('Incident.Manage', 'Xử lý sự cố')
ON CONFLICT (""PermissionName"") DO NOTHING;");

            migrationBuilder.Sql(@"
INSERT INTO ""RolePermissions"" (""RoleId"", ""PermissionId"")
SELECT r.""RoleId"", p.""PermissionId"" FROM ""Roles"" r CROSS JOIN ""Permissions"" p
WHERE r.""RoleName"" = 'Admin'
ON CONFLICT (""RoleId"", ""PermissionId"") DO NOTHING;");

            migrationBuilder.Sql(@"
INSERT INTO ""RolePermissions"" (""RoleId"", ""PermissionId"")
SELECT r.""RoleId"", p.""PermissionId"" FROM ""Roles"" r CROSS JOIN ""Permissions"" p
WHERE r.""RoleName"" = 'Manager'
  AND (p.""PermissionName"" IN ('Report.View', 'Report.Export', 'Pricing.Manage', 'Parking.View', 'Parking.CheckIn',
       'Parking.CheckOut', 'Parking.Configure', 'Payment.Refund', 'User.View', 'Role.View', 'Audit.View', 'Incident.Manage')
       OR p.""PermissionName"" LIKE 'Shift.%')
ON CONFLICT (""RoleId"", ""PermissionId"") DO NOTHING;");

            // ENABLE ALWAYS keeps the append-only triggers firing even under session_replication_role = replica.
            migrationBuilder.Sql(@"ALTER TABLE ""AuditLogs"" ENABLE ALWAYS TRIGGER ""TR_AuditLogs_NoUpdate"";");
            migrationBuilder.Sql(@"ALTER TABLE ""AuditLogs"" ENABLE ALWAYS TRIGGER ""TR_AuditLogs_NoDelete"";");
            migrationBuilder.Sql(@"ALTER TABLE ""AuditLogs"" ENABLE ALWAYS TRIGGER ""TR_AuditLogs_NoTruncate"";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS ""TR_AuditLogs_NoUpdate"" ON ""AuditLogs"";");
            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS ""TR_AuditLogs_NoDelete"" ON ""AuditLogs"";");
            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS ""TR_AuditLogs_NoTruncate"" ON ""AuditLogs"";");
            migrationBuilder.Sql(@"DROP FUNCTION IF EXISTS ""fn_AuditLogs_BlockMutation""();");

            migrationBuilder.Sql(@"
DELETE FROM ""RolePermissions""
WHERE ""PermissionId"" IN (SELECT ""PermissionId"" FROM ""Permissions""
    WHERE ""PermissionName"" IN ('Audit.View', 'Audit.Verify', 'Payment.Refund', 'Settings.Manage', 'Incident.Manage'));");

            migrationBuilder.Sql(@"
DELETE FROM ""Permissions""
WHERE ""PermissionName"" IN ('Audit.View', 'Audit.Verify', 'Payment.Refund', 'Settings.Manage', 'Incident.Manage');");

            // Restore Manager to exactly the Shift.* grants it had before this migration.
            migrationBuilder.Sql(@"
DELETE FROM ""RolePermissions"" rp
USING ""Roles"" r, ""Permissions"" p
WHERE rp.""RoleId"" = r.""RoleId"" AND rp.""PermissionId"" = p.""PermissionId""
  AND r.""RoleName"" = 'Manager' AND p.""PermissionName"" NOT LIKE 'Shift.%';");

            migrationBuilder.Sql(@"
INSERT INTO ""RolePermissions"" (""RoleId"", ""PermissionId"")
SELECT r.""RoleId"", p.""PermissionId"" FROM ""Roles"" r CROSS JOIN ""Permissions"" p
WHERE r.""RoleName"" = 'Manager' AND p.""PermissionName"" LIKE 'Shift.%'
ON CONFLICT DO NOTHING;");

            migrationBuilder.DropTable(
                name: "AuditLogs");
        }
    }
}
