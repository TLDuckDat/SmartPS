using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SmartPS.Migrations
{
    /// <inheritdoc />
    public partial class AddHouseholdVehicleAndPricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Customers_VehicleTypes_VehicleTypeId",
                table: "Customers");

            migrationBuilder.DropForeignKey(
                name: "FK_MonthlyTickets_MonthlyTicketPlans_PlanId",
                table: "MonthlyTickets");

            migrationBuilder.DropTable(
                name: "CustomerTiers");

            migrationBuilder.DropTable(
                name: "MonthlyTicketPlans");

            migrationBuilder.DropIndex(
                name: "IX_PricingRules_VehicleTypeId",
                table: "PricingRules");

            migrationBuilder.DropIndex(
                name: "IX_MonthlyTickets_PlanId",
                table: "MonthlyTickets");

            migrationBuilder.DropColumn(
                name: "FirstBlockMinutes",
                table: "PricingRules");

            migrationBuilder.DropColumn(
                name: "PlanId",
                table: "MonthlyTickets");

            migrationBuilder.DropColumn(
                name: "DefaultLicensePlate",
                table: "Customers");

            migrationBuilder.RenameColumn(
                name: "OvernightPrice",
                table: "PricingRules",
                newName: "Monthly6Price");

            migrationBuilder.RenameColumn(
                name: "FirstBlockPrice",
                table: "PricingRules",
                newName: "Monthly3Price");

            migrationBuilder.RenameColumn(
                name: "AdditionalPricePerHour",
                table: "PricingRules",
                newName: "Monthly1Price");

            migrationBuilder.RenameColumn(
                name: "VehicleTypeId",
                table: "Customers",
                newName: "HouseholdId");

            migrationBuilder.RenameIndex(
                name: "IX_Customers_VehicleTypeId",
                table: "Customers",
                newName: "IX_Customers_HouseholdId");

            migrationBuilder.AddColumn<decimal>(
                name: "Block4hPrice",
                table: "PricingRules",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DailyPrice",
                table: "PricingRules",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AlterColumn<int>(
                name: "CustomerType",
                table: "ParkingSessions",
                type: "integer",
                nullable: false,
                defaultValue: 1,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DurationMonths",
                table: "MonthlyTickets",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "VehicleId",
                table: "MonthlyTickets",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<int>(
                name: "Type",
                table: "Customers",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Households",
                columns: table => new
                {
                    HouseholdId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ApartmentCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    MaxVehicles = table.Column<int>(type: "integer", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Households", x => x.HouseholdId);
                });

            migrationBuilder.CreateTable(
                name: "ParkingSettings",
                columns: table => new
                {
                    SettingsId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DefaultMaxVehiclesPerHousehold = table.Column<int>(type: "integer", nullable: false, defaultValue: 2)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParkingSettings", x => x.SettingsId);
                });

            migrationBuilder.CreateTable(
                name: "Vehicles",
                columns: table => new
                {
                    VehicleId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LicensePlate = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    VehicleTypeId = table.Column<int>(type: "integer", nullable: false),
                    Color = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Brand = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    OwnerCustomerId = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vehicles", x => x.VehicleId);
                    table.ForeignKey(
                        name: "FK_Vehicles_Customers_OwnerCustomerId",
                        column: x => x.OwnerCustomerId,
                        principalTable: "Customers",
                        principalColumn: "CustomerId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Vehicles_VehicleTypes_VehicleTypeId",
                        column: x => x.VehicleTypeId,
                        principalTable: "VehicleTypes",
                        principalColumn: "VehicleTypeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PricingRules_VehicleTypeId",
                table: "PricingRules",
                column: "VehicleTypeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyTickets_VehicleId",
                table: "MonthlyTickets",
                column: "VehicleId");

            migrationBuilder.CreateIndex(
                name: "IX_Households_ApartmentCode",
                table: "Households",
                column: "ApartmentCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_LicensePlate",
                table: "Vehicles",
                column: "LicensePlate",
                unique: true,
                filter: "\"IsActive\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_OwnerCustomerId",
                table: "Vehicles",
                column: "OwnerCustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_VehicleTypeId",
                table: "Vehicles",
                column: "VehicleTypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_Customers_Households_HouseholdId",
                table: "Customers",
                column: "HouseholdId",
                principalTable: "Households",
                principalColumn: "HouseholdId",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_MonthlyTickets_Vehicles_VehicleId",
                table: "MonthlyTickets",
                column: "VehicleId",
                principalTable: "Vehicles",
                principalColumn: "VehicleId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Customers_Households_HouseholdId",
                table: "Customers");

            migrationBuilder.DropForeignKey(
                name: "FK_MonthlyTickets_Vehicles_VehicleId",
                table: "MonthlyTickets");

            migrationBuilder.DropTable(
                name: "Households");

            migrationBuilder.DropTable(
                name: "ParkingSettings");

            migrationBuilder.DropTable(
                name: "Vehicles");

            migrationBuilder.DropIndex(
                name: "IX_PricingRules_VehicleTypeId",
                table: "PricingRules");

            migrationBuilder.DropIndex(
                name: "IX_MonthlyTickets_VehicleId",
                table: "MonthlyTickets");

            migrationBuilder.DropColumn(
                name: "Block4hPrice",
                table: "PricingRules");

            migrationBuilder.DropColumn(
                name: "DailyPrice",
                table: "PricingRules");

            migrationBuilder.DropColumn(
                name: "DurationMonths",
                table: "MonthlyTickets");

            migrationBuilder.DropColumn(
                name: "VehicleId",
                table: "MonthlyTickets");

            migrationBuilder.RenameColumn(
                name: "Monthly6Price",
                table: "PricingRules",
                newName: "OvernightPrice");

            migrationBuilder.RenameColumn(
                name: "Monthly3Price",
                table: "PricingRules",
                newName: "FirstBlockPrice");

            migrationBuilder.RenameColumn(
                name: "Monthly1Price",
                table: "PricingRules",
                newName: "AdditionalPricePerHour");

            migrationBuilder.RenameColumn(
                name: "HouseholdId",
                table: "Customers",
                newName: "VehicleTypeId");

            migrationBuilder.RenameIndex(
                name: "IX_Customers_HouseholdId",
                table: "Customers",
                newName: "IX_Customers_VehicleTypeId");

            migrationBuilder.AddColumn<int>(
                name: "FirstBlockMinutes",
                table: "PricingRules",
                type: "integer",
                nullable: false,
                defaultValue: 120);

            migrationBuilder.AlterColumn<int>(
                name: "CustomerType",
                table: "ParkingSessions",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "PlanId",
                table: "MonthlyTickets",
                type: "integer",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Type",
                table: "Customers",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<string>(
                name: "DefaultLicensePlate",
                table: "Customers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "CustomerTiers",
                columns: table => new
                {
                    TierId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BadgeColor = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "#64748B"),
                    BadgeIcon = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "👤"),
                    CustomerType = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    DiscountPercentage = table.Column<double>(type: "double precision", nullable: false, defaultValue: 0.0),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    TierName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerTiers", x => x.TierId);
                });

            migrationBuilder.CreateTable(
                name: "MonthlyTicketPlans",
                columns: table => new
                {
                    PlanId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    VehicleTypeId = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    DiscountPercentage = table.Column<double>(type: "double precision", nullable: false, defaultValue: 0.0),
                    DurationMonths = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    PlanName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PricePerMonth = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
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

            migrationBuilder.CreateIndex(
                name: "IX_PricingRules_VehicleTypeId",
                table: "PricingRules",
                column: "VehicleTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyTickets_PlanId",
                table: "MonthlyTickets",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerTiers_CustomerType",
                table: "CustomerTiers",
                column: "CustomerType",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyTicketPlans_VehicleTypeId",
                table: "MonthlyTicketPlans",
                column: "VehicleTypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_Customers_VehicleTypes_VehicleTypeId",
                table: "Customers",
                column: "VehicleTypeId",
                principalTable: "VehicleTypes",
                principalColumn: "VehicleTypeId",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_MonthlyTickets_MonthlyTicketPlans_PlanId",
                table: "MonthlyTickets",
                column: "PlanId",
                principalTable: "MonthlyTicketPlans",
                principalColumn: "PlanId",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
