using DentalConnect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DentalConnect.Infrastructure.Persistence.Migrations;

[DbContext(typeof(DentalConnectDbContext))]
[Migration("20261022120000_ClinicProfileFields")]
public partial class ClinicProfileFields : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "city", table: "Clinic", type: "text", nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<string>(name: "neighborhood", table: "Clinic", type: "text", nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<string>(name: "websiteUrl", table: "Clinic", type: "text", nullable: true);
        migrationBuilder.AddColumn<string>(name: "facebookUrl", table: "Clinic", type: "text", nullable: true);
        migrationBuilder.AddColumn<string>(name: "instagramUrl", table: "Clinic", type: "text", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "city", table: "Clinic");
        migrationBuilder.DropColumn(name: "neighborhood", table: "Clinic");
        migrationBuilder.DropColumn(name: "websiteUrl", table: "Clinic");
        migrationBuilder.DropColumn(name: "facebookUrl", table: "Clinic");
        migrationBuilder.DropColumn(name: "instagramUrl", table: "Clinic");
    }
}
