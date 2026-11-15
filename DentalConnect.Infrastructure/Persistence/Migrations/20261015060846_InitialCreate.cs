using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DentalConnect.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Clinic",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    phone = table.Column<string>(type: "text", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    address = table.Column<string>(type: "text", nullable: false),
                    municipality = table.Column<string>(type: "text", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    postalCode = table.Column<string>(type: "text", nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: true),
                    longitude = table.Column<double>(type: "double precision", nullable: true),
                    imageUrl = table.Column<string>(type: "text", nullable: true),
                    isActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    createdAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("Clinic_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "DentalService",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    estimatedDurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    isActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("DentalService_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "Dentist",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    clinicId = table.Column<string>(type: "text", nullable: false),
                    firstName = table.Column<string>(type: "text", nullable: false),
                    lastName = table.Column<string>(type: "text", nullable: false),
                    professionalLicense = table.Column<string>(type: "text", nullable: false),
                    specialty = table.Column<string>(type: "text", nullable: false),
                    biography = table.Column<string>(type: "text", nullable: false),
                    imageUrl = table.Column<string>(type: "text", nullable: true),
                    isActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("Dentist_pkey", x => x.id);
                    table.ForeignKey(
                        name: "Dentist_clinicId_fkey",
                        column: x => x.clinicId,
                        principalTable: "Clinic",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ClinicService",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    clinicId = table.Column<string>(type: "text", nullable: false),
                    dentalServiceId = table.Column<string>(type: "text", nullable: false),
                    priceFrom = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    isActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("ClinicService_pkey", x => x.id);
                    table.ForeignKey(
                        name: "ClinicService_clinicId_fkey",
                        column: x => x.clinicId,
                        principalTable: "Clinic",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "ClinicService_dentalServiceId_fkey",
                        column: x => x.dentalServiceId,
                        principalTable: "DentalService",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ClinicService_clinicId_dentalServiceId_key",
                table: "ClinicService",
                columns: new[] { "clinicId", "dentalServiceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClinicService_dentalServiceId",
                table: "ClinicService",
                column: "dentalServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_Dentist_clinicId",
                table: "Dentist",
                column: "clinicId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClinicService");

            migrationBuilder.DropTable(
                name: "Dentist");

            migrationBuilder.DropTable(
                name: "DentalService");

            migrationBuilder.DropTable(
                name: "Clinic");
        }
    }
}
