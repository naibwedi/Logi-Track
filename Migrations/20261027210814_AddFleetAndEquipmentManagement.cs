using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace logirack.Migrations
{
    /// <inheritdoc />
    public partial class AddFleetAndEquipmentManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "VehicleId",
                table: "Trips",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VehicleId",
                table: "DriverTrips",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VehicleId",
                table: "AspNetUsers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Vehicles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UnitNumber = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Vin = table.Column<string>(type: "TEXT", maxLength: 17, nullable: false),
                    LicensePlate = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    LicenseState = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Make = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Model = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    RegistrationExpirationDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AnnualDotInspectionExpiration = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Form2290ExpirationDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PrePassTransponderId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    AssignedDriverId = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vehicles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Vehicles_AspNetUsers_AssignedDriverId",
                        column: x => x.AssignedDriverId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Trips_VehicleId",
                table: "Trips",
                column: "VehicleId");

            migrationBuilder.CreateIndex(
                name: "IX_DriverTrips_VehicleId",
                table: "DriverTrips",
                column: "VehicleId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_VehicleId",
                table: "AspNetUsers",
                column: "VehicleId");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_AssignedDriverId",
                table: "Vehicles",
                column: "AssignedDriverId");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_UnitNumber",
                table: "Vehicles",
                column: "UnitNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_Vin",
                table: "Vehicles",
                column: "Vin",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_Vehicles_VehicleId",
                table: "AspNetUsers",
                column: "VehicleId",
                principalTable: "Vehicles",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_DriverTrips_Vehicles_VehicleId",
                table: "DriverTrips",
                column: "VehicleId",
                principalTable: "Vehicles",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Trips_Vehicles_VehicleId",
                table: "Trips",
                column: "VehicleId",
                principalTable: "Vehicles",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_Vehicles_VehicleId",
                table: "AspNetUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_DriverTrips_Vehicles_VehicleId",
                table: "DriverTrips");

            migrationBuilder.DropForeignKey(
                name: "FK_Trips_Vehicles_VehicleId",
                table: "Trips");

            migrationBuilder.DropTable(
                name: "Vehicles");

            migrationBuilder.DropIndex(
                name: "IX_Trips_VehicleId",
                table: "Trips");

            migrationBuilder.DropIndex(
                name: "IX_DriverTrips_VehicleId",
                table: "DriverTrips");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_VehicleId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "VehicleId",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "VehicleId",
                table: "DriverTrips");

            migrationBuilder.DropColumn(
                name: "VehicleId",
                table: "AspNetUsers");
        }
    }
}
