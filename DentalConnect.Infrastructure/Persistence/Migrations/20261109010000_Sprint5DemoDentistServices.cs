using DentalConnect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DentalConnect.Infrastructure.Persistence.Migrations;

[DbContext(typeof(DentalConnectDbContext))]
[Migration("20261109010000_Sprint5DemoDentistServices")]
public sealed class Sprint5DemoDentistServices : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            INSERT INTO "DentistService" ("Id", "DentistId", "DentalServiceId")
            SELECT 'c' || substr(md5(d."id" || ':' || s."id"), 1, 24), d."id", s."id"
            FROM "Dentist" d
            JOIN "Clinic" c ON c."id" = d."clinicId" AND c."isActive" = TRUE
            JOIN "ClinicService" cs ON cs."clinicId" = c."id" AND cs."isActive" = TRUE
            JOIN "DentalService" s ON s."id" = cs."dentalServiceId" AND s."isActive" = TRUE
            WHERE d."isActive" = TRUE
              AND lower(btrim(d."specialty")) = lower(btrim(s."name"))
              AND NOT EXISTS (
                  SELECT 1 FROM "DentistService" existing
                  WHERE existing."DentistId" = d."id"
                    AND existing."DentalServiceId" = s."id"
              );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DELETE FROM "DentistService" link
            USING "Dentist" d, "DentalService" s
            WHERE link."DentistId" = d."id"
              AND link."DentalServiceId" = s."id"
              AND link."Id" = 'c' || substr(md5(d."id" || ':' || s."id"), 1, 24)
              AND lower(btrim(d."specialty")) = lower(btrim(s."name"));
            """);
    }
}
