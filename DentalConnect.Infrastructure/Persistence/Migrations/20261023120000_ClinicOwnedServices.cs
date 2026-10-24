using DentalConnect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DentalConnect.Infrastructure.Persistence.Migrations;

[DbContext(typeof(DentalConnectDbContext))]
[Migration("20261023120000_ClinicOwnedServices")]
public partial class ClinicOwnedServices : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "clinicId", table: "DentalService", type: "text", nullable: true);

        // Copia cada servicio compartido a cada consultorio que lo usa (oferta o dentista) y remapea las referencias.
        migrationBuilder.Sql("""
            CREATE TEMP TABLE "ServiceMap" AS
            SELECT x."clinicId", x."oldId",
                   'c' || substr(md5(random()::text || clock_timestamp()::text || x."clinicId" || x."oldId"), 1, 24) AS "newId"
            FROM (
                SELECT cs."clinicId", cs."dentalServiceId" AS "oldId" FROM "ClinicService" cs
                UNION
                SELECT d."clinicId", ds."DentalServiceId" FROM "DentistService" ds JOIN "Dentist" d ON d."id" = ds."DentistId"
            ) x;

            INSERT INTO "DentalService" ("id", "clinicId", "name", "description", "estimatedDurationMinutes", "isActive")
            SELECT m."newId", m."clinicId", s."name", s."description", s."estimatedDurationMinutes", s."isActive"
            FROM "ServiceMap" m JOIN "DentalService" s ON s."id" = m."oldId";

            UPDATE "ClinicService" cs SET "dentalServiceId" = m."newId"
            FROM "ServiceMap" m
            WHERE m."clinicId" = cs."clinicId" AND m."oldId" = cs."dentalServiceId";

            UPDATE "DentistService" ds SET "DentalServiceId" = m."newId"
            FROM "Dentist" d, "ServiceMap" m
            WHERE d."id" = ds."DentistId" AND m."clinicId" = d."clinicId" AND m."oldId" = ds."DentalServiceId";

            DELETE FROM "DentalService" s
            WHERE s."clinicId" IS NULL
              AND NOT EXISTS (SELECT 1 FROM "ClinicService" cs WHERE cs."dentalServiceId" = s."id")
              AND NOT EXISTS (SELECT 1 FROM "DentistService" ds WHERE ds."DentalServiceId" = s."id");

            DROP TABLE "ServiceMap";
            """);

        migrationBuilder.AlterColumn<string>(name: "clinicId", table: "DentalService", type: "text", nullable: false, oldClrType: typeof(string), oldType: "text", oldNullable: true);
        migrationBuilder.CreateIndex(name: "DentalService_clinicId_idx", table: "DentalService", column: "clinicId");
        migrationBuilder.AddForeignKey(name: "DentalService_clinicId_fkey", table: "DentalService", column: "clinicId", principalTable: "Clinic", principalColumn: "id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.AlterColumn<decimal>(name: "priceFrom", table: "ClinicService", type: "numeric(10,2)", nullable: true, oldClrType: typeof(decimal), oldType: "numeric(10,2)");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""UPDATE "ClinicService" SET "priceFrom" = 0 WHERE "priceFrom" IS NULL;""");
        migrationBuilder.AlterColumn<decimal>(name: "priceFrom", table: "ClinicService", type: "numeric(10,2)", nullable: false, oldClrType: typeof(decimal), oldType: "numeric(10,2)", oldNullable: true);
        migrationBuilder.DropForeignKey(name: "DentalService_clinicId_fkey", table: "DentalService");
        migrationBuilder.DropIndex(name: "DentalService_clinicId_idx", table: "DentalService");
        migrationBuilder.DropColumn(name: "clinicId", table: "DentalService");
    }
}
