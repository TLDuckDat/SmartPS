using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SmartPS.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerTiers",
                columns: table => new
                {
                    TierId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CustomerType = table.Column<int>(type: "integer", nullable: false),
                    TierName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DiscountPercentage = table.Column<double>(type: "double precision", nullable: false, defaultValue: 0.0),
                    BadgeColor = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "#64748B"),
                    BadgeIcon = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "👤"),
                    Description = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerTiers", x => x.TierId);
                });

            migrationBuilder.CreateTable(
                name: "Permissions",
                columns: table => new
                {
                    PermissionId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PermissionName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Permissions", x => x.PermissionId);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    RoleId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.RoleId);
                });

            migrationBuilder.CreateTable(
                name: "VehicleTypes",
                columns: table => new
                {
                    VehicleTypeId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TypeName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleTypes", x => x.VehicleTypeId);
                });

            migrationBuilder.CreateTable(
                name: "RolePermissions",
                columns: table => new
                {
                    RoleId = table.Column<int>(type: "integer", nullable: false),
                    PermissionId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePermissions", x => new { x.RoleId, x.PermissionId });
                    table.ForeignKey(
                        name: "FK_RolePermissions_Permissions_PermissionId",
                        column: x => x.PermissionId,
                        principalTable: "Permissions",
                        principalColumn: "PermissionId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RolePermissions_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "RoleId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Username = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    FullName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    RoleId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_Users_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "RoleId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    CustomerId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FullName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Email = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IdentityCard = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    DefaultLicensePlate = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    VehicleTypeId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.CustomerId);
                    table.ForeignKey(
                        name: "FK_Customers_VehicleTypes_VehicleTypeId",
                        column: x => x.VehicleTypeId,
                        principalTable: "VehicleTypes",
                        principalColumn: "VehicleTypeId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "MonthlyTicketPlans",
                columns: table => new
                {
                    PlanId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlanName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    VehicleTypeId = table.Column<int>(type: "integer", nullable: false),
                    DurationMonths = table.Column<int>(type: "integer", nullable: false),
                    PricePerMonth = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DiscountPercentage = table.Column<double>(type: "double precision", nullable: false, defaultValue: 0.0),
                    TotalPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    Description = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonthlyTicketPlans", x => x.PlanId);
                    table.ForeignKey(
                        name: "FK_MonthlyTicketPlans_VehicleTypes_VehicleTypeId",
                        column: x => x.VehicleTypeId,
                        principalTable: "VehicleTypes",
                        principalColumn: "VehicleTypeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ParkingZones",
                columns: table => new
                {
                    ZoneId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ZoneCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ZoneName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    VehicleTypeId = table.Column<int>(type: "integer", nullable: true),
                    TotalCapacity = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    Description = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParkingZones", x => x.ZoneId);
                    table.ForeignKey(
                        name: "FK_ParkingZones_VehicleTypes_VehicleTypeId",
                        column: x => x.VehicleTypeId,
                        principalTable: "VehicleTypes",
                        principalColumn: "VehicleTypeId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PricingRules",
                columns: table => new
                {
                    RuleId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    VehicleTypeId = table.Column<int>(type: "integer", nullable: false),
                    FirstBlockMinutes = table.Column<int>(type: "integer", nullable: false, defaultValue: 120),
                    FirstBlockPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AdditionalPricePerHour = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OvernightPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Description = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PricingRules", x => x.RuleId);
                    table.ForeignKey(
                        name: "FK_PricingRules_VehicleTypes_VehicleTypeId",
                        column: x => x.VehicleTypeId,
                        principalTable: "VehicleTypes",
                        principalColumn: "VehicleTypeId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MonthlyTickets",
                columns: table => new
                {
                    TicketId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TicketCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CustomerId = table.Column<int>(type: "integer", nullable: false),
                    RegisteredLicensePlate = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PlanId = table.Column<int>(type: "integer", nullable: true),
                    VehicleTypeId = table.Column<int>(type: "integer", nullable: false),
                    StartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MonthlyPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonthlyTickets", x => x.TicketId);
                    table.ForeignKey(
                        name: "FK_MonthlyTickets_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "CustomerId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MonthlyTickets_MonthlyTicketPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "MonthlyTicketPlans",
                        principalColumn: "PlanId",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MonthlyTickets_VehicleTypes_VehicleTypeId",
                        column: x => x.VehicleTypeId,
                        principalTable: "VehicleTypes",
                        principalColumn: "VehicleTypeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ParkingSlots",
                columns: table => new
                {
                    SlotId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SlotCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ZoneName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ZoneId = table.Column<int>(type: "integer", nullable: true),
                    VehicleTypeId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CoordX = table.Column<double>(type: "double precision", nullable: false, defaultValue: 0.0),
                    CoordY = table.Column<double>(type: "double precision", nullable: false, defaultValue: 0.0),
                    Width = table.Column<double>(type: "double precision", nullable: false, defaultValue: 80.0),
                    Height = table.Column<double>(type: "double precision", nullable: false, defaultValue: 120.0),
                    CurrentLicensePlate = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParkingSlots", x => x.SlotId);
                    table.ForeignKey(
                        name: "FK_ParkingSlots_ParkingZones_ZoneId",
                        column: x => x.ZoneId,
                        principalTable: "ParkingZones",
                        principalColumn: "ZoneId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ParkingSlots_VehicleTypes_VehicleTypeId",
                        column: x => x.VehicleTypeId,
                        principalTable: "VehicleTypes",
                        principalColumn: "VehicleTypeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ParkingSessions",
                columns: table => new
                {
                    SessionId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TicketCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    LicensePlate = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    VehicleTypeId = table.Column<int>(type: "integer", nullable: false),
                    SlotId = table.Column<int>(type: "integer", nullable: true),
                    CheckInTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CheckOutTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CheckInImagePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CheckOutImagePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TotalFee = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0.0m),
                    Status = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    PaymentMethod = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CustomerId = table.Column<int>(type: "integer", nullable: true),
                    IsMonthlyPass = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CustomerType = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParkingSessions", x => x.SessionId);
                    table.ForeignKey(
                        name: "FK_ParkingSessions_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "CustomerId",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ParkingSessions_ParkingSlots_SlotId",
                        column: x => x.SlotId,
                        principalTable: "ParkingSlots",
                        principalColumn: "SlotId",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ParkingSessions_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ParkingSessions_VehicleTypes_VehicleTypeId",
                        column: x => x.VehicleTypeId,
                        principalTable: "VehicleTypes",
                        principalColumn: "VehicleTypeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_PhoneNumber",
                table: "Customers",
                column: "PhoneNumber");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_VehicleTypeId",
                table: "Customers",
                column: "VehicleTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerTiers_CustomerType",
                table: "CustomerTiers",
                column: "CustomerType",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyTicketPlans_VehicleTypeId",
                table: "MonthlyTicketPlans",
                column: "VehicleTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyTickets_CustomerId",
                table: "MonthlyTickets",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyTickets_PlanId",
                table: "MonthlyTickets",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyTickets_RegisteredLicensePlate",
                table: "MonthlyTickets",
                column: "RegisteredLicensePlate");

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyTickets_TicketCode",
                table: "MonthlyTickets",
                column: "TicketCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyTickets_VehicleTypeId",
                table: "MonthlyTickets",
                column: "VehicleTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_ParkingSessions_CheckInTime",
                table: "ParkingSessions",
                column: "CheckInTime");

            migrationBuilder.CreateIndex(
                name: "IX_ParkingSessions_CreatedByUserId",
                table: "ParkingSessions",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ParkingSessions_CustomerId",
                table: "ParkingSessions",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_ParkingSessions_LicensePlate",
                table: "ParkingSessions",
                column: "LicensePlate");

            migrationBuilder.CreateIndex(
                name: "IX_ParkingSessions_SlotId",
                table: "ParkingSessions",
                column: "SlotId");

            migrationBuilder.CreateIndex(
                name: "IX_ParkingSessions_Status",
                table: "ParkingSessions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ParkingSessions_TicketCode",
                table: "ParkingSessions",
                column: "TicketCode");

            migrationBuilder.CreateIndex(
                name: "IX_ParkingSessions_VehicleTypeId",
                table: "ParkingSessions",
                column: "VehicleTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_ParkingSlots_SlotCode",
                table: "ParkingSlots",
                column: "SlotCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ParkingSlots_VehicleTypeId",
                table: "ParkingSlots",
                column: "VehicleTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_ParkingSlots_ZoneId",
                table: "ParkingSlots",
                column: "ZoneId");

            migrationBuilder.CreateIndex(
                name: "IX_ParkingZones_VehicleTypeId",
                table: "ParkingZones",
                column: "VehicleTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_ParkingZones_ZoneCode",
                table: "ParkingZones",
                column: "ZoneCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Permissions_PermissionName",
                table: "Permissions",
                column: "PermissionName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PricingRules_VehicleTypeId",
                table: "PricingRules",
                column: "VehicleTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_PermissionId",
                table: "RolePermissions",
                column: "PermissionId");

            migrationBuilder.CreateIndex(
                name: "IX_Roles_RoleName",
                table: "Roles",
                column: "RoleName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_RoleId",
                table: "Users",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VehicleTypes_TypeName",
                table: "VehicleTypes",
                column: "TypeName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerTiers");

            migrationBuilder.DropTable(
                name: "MonthlyTickets");

            migrationBuilder.DropTable(
                name: "ParkingSessions");

            migrationBuilder.DropTable(
                name: "PricingRules");

            migrationBuilder.DropTable(
                name: "RolePermissions");

            migrationBuilder.DropTable(
                name: "MonthlyTicketPlans");

            migrationBuilder.DropTable(
                name: "Customers");

            migrationBuilder.DropTable(
                name: "ParkingSlots");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Permissions");

            migrationBuilder.DropTable(
                name: "ParkingZones");

            migrationBuilder.DropTable(
                name: "Roles");

            migrationBuilder.DropTable(
                name: "VehicleTypes");
        }
    }
}
