using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DentalConnect.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClinicScopedDentalServices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "clinicId",
                table: "DentalService",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DentalService_ClinicId",
                table: "DentalService",
                column: "clinicId");

            migrationBuilder.AddForeignKey(
                name: "FK_DentalService_Clinic_ClinicId",
                table: "DentalService",
                column: "clinicId",
                principalTable: "Clinic",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DentalService_Clinic_ClinicId",
                table: "DentalService");

            migrationBuilder.DropIndex(
                name: "IX_DentalService_ClinicId",
                table: "DentalService");

            migrationBuilder.DropColumn(
                name: "clinicId",
                table: "DentalService");
        }
    }
}
