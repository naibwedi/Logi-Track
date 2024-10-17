using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace logirack.Migrations
{
    /// <inheritdoc />
    public partial class UpdatedPaymentSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedOn",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<bool>(
                name: "IsApproved",
                table: "AspNetUsers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsAvailable",
                table: "AspNetUsers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ModifiedOn",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "PaymentFreq",
                table: "AspNetUsers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PricePerKm",
                table: "AspNetUsers",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RoleType",
                table: "AspNetUsers",
                type: "TEXT",
                maxLength: 21,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "Addresses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DriverId = table.Column<string>(type: "TEXT", nullable: false),
                    Street = table.Column<string>(type: "TEXT", nullable: false),
                    City = table.Column<string>(type: "TEXT", nullable: false),
                    Region = table.Column<string>(type: "TEXT", nullable: false),
                    Country = table.Column<string>(type: "TEXT", nullable: false),
                    CodePostal = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Addresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Addresses_AspNetUsers_DriverId",
                        column: x => x.DriverId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AdminActionLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AdminId = table.Column<string>(type: "TEXT", nullable: false),
                    ASction = table.Column<string>(type: "TEXT", nullable: false),
                    DeTails = table.Column<string>(type: "TEXT", nullable: false),
                    ADate = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminActionLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdminActionLogs_AspNetUsers_AdminId",
                        column: x => x.AdminId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Locations",
                columns: table => new
                {
                    ID = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DriverID = table.Column<string>(type: "TEXT", nullable: false),
                    City = table.Column<string>(type: "TEXT", nullable: false),
                    Street = table.Column<string>(type: "TEXT", nullable: false),
                    Region = table.Column<string>(type: "TEXT", nullable: false),
                    Longitude = table.Column<double>(type: "REAL", nullable: false),
                    Latitude = table.Column<double>(type: "REAL", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Locations", x => x.ID);
                    table.ForeignKey(
                        name: "FK_Locations_AspNetUsers_DriverID",
                        column: x => x.DriverID,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PaymentPeriods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DriverID = table.Column<string>(type: "TEXT", nullable: false),
                    StartDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PaymentFreq = table.Column<int>(type: "INTEGER", nullable: false),
                    Total = table.Column<double>(type: "REAL", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentPeriods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentPeriods_AspNetUsers_DriverID",
                        column: x => x.DriverID,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Trips",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CustomerId = table.Column<string>(type: "TEXT", nullable: false),
                    AdminId = table.Column<string>(type: "TEXT", nullable: false),
                    FromCity = table.Column<string>(type: "TEXT", nullable: false),
                    ToCity = table.Column<string>(type: "TEXT", nullable: false),
                    FromAddress = table.Column<string>(type: "TEXT", nullable: false),
                    ToAddress = table.Column<string>(type: "TEXT", nullable: false),
                    FromZipCode = table.Column<string>(type: "TEXT", nullable: false),
                    ToZipCode = table.Column<string>(type: "TEXT", nullable: false),
                    Weight = table.Column<double>(type: "REAL", nullable: false),
                    Volume = table.Column<double>(type: "REAL", nullable: false),
                    Distance = table.Column<double>(type: "REAL", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    AdminPrice = table.Column<double>(type: "REAL", nullable: false),
                    IsPrAcceptedBeyCustomer = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsAcceptedBeyAdmin = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Trips", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Trips_AspNetUsers_AdminId",
                        column: x => x.AdminId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Trips_AspNetUsers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PaymentPeriodId = table.Column<int>(type: "INTEGER", nullable: false),
                    Amount = table.Column<double>(type: "REAL", nullable: false),
                    PaymentDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ProcByAdminID = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Payments_AspNetUsers_ProcByAdminID",
                        column: x => x.ProcByAdminID,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Payments_PaymentPeriods_PaymentPeriodId",
                        column: x => x.PaymentPeriodId,
                        principalTable: "PaymentPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DriverTrips",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TripId = table.Column<int>(type: "INTEGER", nullable: false),
                    DriverId = table.Column<string>(type: "TEXT", nullable: false),
                    AssignedByAdminId = table.Column<string>(type: "TEXT", nullable: false),
                    DriverPayment = table.Column<double>(type: "REAL", nullable: false),
                    PaymentPeriodId = table.Column<int>(type: "INTEGER", nullable: true),
                    IsCompleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    CompletionDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AssignmentDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdateAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DriverTrips", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DriverTrips_AspNetUsers_AssignedByAdminId",
                        column: x => x.AssignedByAdminId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DriverTrips_AspNetUsers_DriverId",
                        column: x => x.DriverId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DriverTrips_PaymentPeriods_PaymentPeriodId",
                        column: x => x.PaymentPeriodId,
                        principalTable: "PaymentPeriods",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DriverTrips_Trips_TripId",
                        column: x => x.TripId,
                        principalTable: "Trips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Addresses_DriverId",
                table: "Addresses",
                column: "DriverId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdminActionLogs_AdminId",
                table: "AdminActionLogs",
                column: "AdminId");

            migrationBuilder.CreateIndex(
                name: "IX_DriverTrips_AssignedByAdminId",
                table: "DriverTrips",
                column: "AssignedByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_DriverTrips_DriverId",
                table: "DriverTrips",
                column: "DriverId");

            migrationBuilder.CreateIndex(
                name: "IX_DriverTrips_PaymentPeriodId",
                table: "DriverTrips",
                column: "PaymentPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_DriverTrips_TripId",
                table: "DriverTrips",
                column: "TripId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Locations_DriverID",
                table: "Locations",
                column: "DriverID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentPeriods_DriverID",
                table: "PaymentPeriods",
                column: "DriverID");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_PaymentPeriodId",
                table: "Payments",
                column: "PaymentPeriodId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_ProcByAdminID",
                table: "Payments",
                column: "ProcByAdminID");

            migrationBuilder.CreateIndex(
                name: "IX_Trips_AdminId",
                table: "Trips",
                column: "AdminId");

            migrationBuilder.CreateIndex(
                name: "IX_Trips_CustomerId",
                table: "Trips",
                column: "CustomerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Addresses");

            migrationBuilder.DropTable(
                name: "AdminActionLogs");

            migrationBuilder.DropTable(
                name: "DriverTrips");

            migrationBuilder.DropTable(
                name: "Locations");

            migrationBuilder.DropTable(
                name: "Payments");

            migrationBuilder.DropTable(
                name: "Trips");

            migrationBuilder.DropTable(
                name: "PaymentPeriods");

            migrationBuilder.DropColumn(
                name: "CreatedOn",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "IsApproved",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "IsAvailable",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "ModifiedOn",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "PaymentFreq",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "PricePerKm",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "RoleType",
                table: "AspNetUsers");
        }
    }
}
