using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartPS.Migrations
{
    /// <inheritdoc />
    public partial class HardenAuditTriggers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ENABLE ALWAYS keeps the append-only triggers firing even under session_replication_role = replica.
            migrationBuilder.Sql(@"ALTER TABLE ""AuditLogs"" ENABLE ALWAYS TRIGGER ""TR_AuditLogs_NoUpdate"";");
            migrationBuilder.Sql(@"ALTER TABLE ""AuditLogs"" ENABLE ALWAYS TRIGGER ""TR_AuditLogs_NoDelete"";");
            migrationBuilder.Sql(@"ALTER TABLE ""AuditLogs"" ENABLE ALWAYS TRIGGER ""TR_AuditLogs_NoTruncate"";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE ""AuditLogs"" ENABLE TRIGGER ""TR_AuditLogs_NoUpdate"";");
            migrationBuilder.Sql(@"ALTER TABLE ""AuditLogs"" ENABLE TRIGGER ""TR_AuditLogs_NoDelete"";");
            migrationBuilder.Sql(@"ALTER TABLE ""AuditLogs"" ENABLE TRIGGER ""TR_AuditLogs_NoTruncate"";");
        }
    }
}
