using DentalConnect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DentalConnect.Infrastructure.Persistence.Migrations;

[DbContext(typeof(DentalConnectDbContext))]
[Migration("20261109020000_Sprint5DemoDentistSchedule")]
public sealed class Sprint5DemoDentistSchedule : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            INSERT INTO "DentistWeeklySchedule"
                ("Id", "DentistId", "DayOfWeek", "StartsAt", "EndsAt", "BreakStartsAt", "BreakEndsAt", "TimeZoneId", "CreatedAt", "UpdatedAt")
            SELECT 'c' || substr(md5(d."id" || ':demo-schedule:' || weekday::text), 1, 24),
                   d."id", weekday, TIME '09:00', TIME '17:00', TIME '13:00', TIME '14:00',
                   'America/Mexico_City', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            FROM "Dentist" d
            CROSS JOIN generate_series(1, 5) AS weekday
            WHERE d."isActive" = TRUE
              AND d."biography" ILIKE '%ficticia%'
              AND EXISTS (
                  SELECT 1 FROM "DentistService" ds
                  JOIN "DentalService" s ON s."id" = ds."DentalServiceId" AND s."isActive" = TRUE
                  WHERE ds."DentistId" = d."id"
                    AND lower(btrim(d."specialty")) = lower(btrim(s."name"))
              )
              AND NOT EXISTS (
                  SELECT 1 FROM "DentistWeeklySchedule" existing
                  WHERE existing."DentistId" = d."id" AND existing."DayOfWeek" = weekday
              );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DELETE FROM "DentistWeeklySchedule" schedule
            USING "Dentist" d, generate_series(1, 5) AS weekday
            WHERE schedule."DentistId" = d."id"
              AND schedule."DayOfWeek" = weekday
              AND schedule."Id" = 'c' || substr(md5(d."id" || ':demo-schedule:' || weekday::text), 1, 24);
            """);
    }
}
