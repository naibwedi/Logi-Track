using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace logirack.Migrations
{
    /// <inheritdoc />
    public partial class AddBrokersSettlementsAndCompliance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BrokerId",
                table: "Trips",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BrokerLoadNumber",
                table: "Trips",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeductionReason",
                table: "Trips",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Deductions",
                table: "Trips",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DetentionFee",
                table: "Trips",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceNumber",
                table: "Trips",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LumperFee",
                table: "Trips",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OtherAccessorials",
                table: "Trips",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "RateConfirmationAmount",
                table: "Trips",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TonuAmount",
                table: "Trips",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CdlExpirationDate",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CdlNumber",
                table: "AspNetUsers",
                type: "TEXT",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CdlState",
                table: "AspNetUsers",
                type: "TEXT",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MedicalCardExpirationDate",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Brokers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CompanyName = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    McNumber = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    AgentName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    PhoneExtension = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    IsBlacklisted = table.Column<bool>(type: "INTEGER", nullable: false),
                    BlacklistReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    BlacklistedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Brokers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Trips_BrokerId",
                table: "Trips",
                column: "BrokerId");

            migrationBuilder.CreateIndex(
                name: "IX_Trips_InvoiceNumber",
                table: "Trips",
                column: "InvoiceNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Brokers_McNumber",
                table: "Brokers",
                column: "McNumber",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Trips_Brokers_BrokerId",
                table: "Trips",
                column: "BrokerId",
                principalTable: "Brokers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Trips_Brokers_BrokerId",
                table: "Trips");

            migrationBuilder.DropTable(
                name: "Brokers");

            migrationBuilder.DropIndex(
                name: "IX_Trips_BrokerId",
                table: "Trips");

            migrationBuilder.DropIndex(
                name: "IX_Trips_InvoiceNumber",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "BrokerId",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "BrokerLoadNumber",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "DeductionReason",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "Deductions",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "DetentionFee",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "InvoiceNumber",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "LumperFee",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "OtherAccessorials",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "RateConfirmationAmount",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "TonuAmount",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "CdlExpirationDate",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "CdlNumber",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "CdlState",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "MedicalCardExpirationDate",
                table: "AspNetUsers");
        }
    }
}
