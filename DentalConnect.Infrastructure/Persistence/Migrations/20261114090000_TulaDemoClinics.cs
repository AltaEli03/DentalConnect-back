using DentalConnect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DentalConnect.Infrastructure.Persistence.Migrations;

[DbContext(typeof(DentalConnectDbContext))]
[Migration("20261114090000_TulaDemoClinics")]
public sealed class TulaDemoClinics : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            -- All records created here are synthetic test data. Stable IDs make the migration repeatable.
            INSERT INTO "DentalService" ("id", "name", "description", "estimatedDurationMinutes", "isActive")
            SELECT 'ctulademovaloracion00001', 'Consulta de valoración', 'Servicio ficticio para pruebas de DentalConnect.', 30, TRUE
            WHERE NOT EXISTS (SELECT 1 FROM "DentalService" WHERE lower(btrim("name")) = lower('Consulta de valoración'));

            INSERT INTO "Clinic" ("id", "name", "description", "phone", "email", "address", "municipality", "state", "postalCode", "latitude", "longitude", "imageUrl", "isActive", "createdAt", "updatedAt")
            VALUES
              ('ctuladentaltest00000001', 'DentalConnect Tula Centro (PRUEBA)', 'Consultorio totalmente ficticio para pruebas de búsqueda y solicitud de citas. No corresponde a un negocio real.', '771-000-0001', 'tula-centro@example.invalid', 'Domicilio ficticio para pruebas, zona Centro', 'Tula de Allende', 'Hidalgo', '42800', 20.0530, -99.3430, NULL, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
              ('ctuladentaltest00000002', 'DentalConnect San Marcos (PRUEBA)', 'Consultorio totalmente ficticio para pruebas de búsqueda y solicitud de citas. No corresponde a un negocio real.', '771-000-0002', 'san-marcos@example.invalid', 'Domicilio ficticio para pruebas, zona San Marcos', 'Tula de Allende', 'Hidalgo', '42803', 20.0570, -99.3290, NULL, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
              ('ctuladentaltest00000003', 'DentalConnect El Llano (PRUEBA)', 'Consultorio totalmente ficticio para pruebas de búsqueda y solicitud de citas. No corresponde a un negocio real.', '771-000-0003', 'el-llano@example.invalid', 'Domicilio ficticio para pruebas, zona El Llano', 'Tula de Allende', 'Hidalgo', '42804', 20.0660, -99.3180, NULL, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
            ON CONFLICT ("id") DO UPDATE SET "isActive"=TRUE, "updatedAt"=CURRENT_TIMESTAMP;

            INSERT INTO "ClinicService" ("id", "clinicId", "dentalServiceId", "priceFrom", "isActive")
            SELECT 'c' || substr(md5(c."id" || ':tula-demo-service:' || s."id"), 1, 24), c."id", s."id", 450.00, TRUE
            FROM "Clinic" c CROSS JOIN "DentalService" s
            WHERE c."id" IN ('ctuladentaltest00000001','ctuladentaltest00000002','ctuladentaltest00000003')
              AND lower(btrim(s."name")) = lower('Consulta de valoración')
            ON CONFLICT ("clinicId", "dentalServiceId") DO UPDATE SET "isActive"=TRUE;

            INSERT INTO "Dentist" ("id", "clinicId", "firstName", "lastName", "professionalLicense", "specialty", "biography", "imageUrl", "isActive", "CreatedAt", "UpdatedAt")
            VALUES
              ('ctuladentisttest00000001','ctuladentaltest00000001','Dentista','Prueba Tula Centro','CEDULA-FICTICIA-TULA-001','Consulta de valoración','Perfil ficticio para pruebas. No es un profesional real.',NULL,TRUE,CURRENT_TIMESTAMP,CURRENT_TIMESTAMP),
              ('ctuladentisttest00000002','ctuladentaltest00000002','Dentista','Prueba San Marcos','CEDULA-FICTICIA-TULA-002','Consulta de valoración','Perfil ficticio para pruebas. No es un profesional real.',NULL,TRUE,CURRENT_TIMESTAMP,CURRENT_TIMESTAMP),
              ('ctuladentisttest00000003','ctuladentaltest00000003','Dentista','Prueba El Llano','CEDULA-FICTICIA-TULA-003','Consulta de valoración','Perfil ficticio para pruebas. No es un profesional real.',NULL,TRUE,CURRENT_TIMESTAMP,CURRENT_TIMESTAMP)
            ON CONFLICT ("id") DO UPDATE SET "isActive"=TRUE, "UpdatedAt"=CURRENT_TIMESTAMP;

            INSERT INTO "DentistService" ("Id", "DentistId", "DentalServiceId")
            SELECT 'c' || substr(md5(d."id" || ':' || s."id"), 1, 24), d."id", s."id"
            FROM "Dentist" d CROSS JOIN "DentalService" s
            WHERE d."id" IN ('ctuladentisttest00000001','ctuladentisttest00000002','ctuladentisttest00000003')
              AND lower(btrim(s."name")) = lower('Consulta de valoración')
            ON CONFLICT ("DentistId", "DentalServiceId") DO NOTHING;

            INSERT INTO "DentistWeeklySchedule" ("Id", "DentistId", "DayOfWeek", "StartsAt", "EndsAt", "BreakStartsAt", "BreakEndsAt", "TimeZoneId", "CreatedAt", "UpdatedAt")
            SELECT 'c' || substr(md5(d."id" || ':tula-demo-schedule:' || weekday::text), 1, 24), d."id", weekday,
                   TIME '09:00', TIME '17:00', TIME '13:00', TIME '14:00', 'America/Mexico_City', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            FROM "Dentist" d CROSS JOIN generate_series(1, 6) AS weekday
            WHERE d."id" IN ('ctuladentisttest00000001','ctuladentisttest00000002','ctuladentisttest00000003')
              AND NOT EXISTS (SELECT 1 FROM "DentistWeeklySchedule" x WHERE x."DentistId"=d."id" AND x."DayOfWeek"=weekday);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            -- Preserve referential integrity if someone has already created a demo appointment.
            UPDATE "Clinic" SET "isActive"=FALSE, "updatedAt"=CURRENT_TIMESTAMP
              WHERE "id" IN ('ctuladentaltest00000001','ctuladentaltest00000002','ctuladentaltest00000003');
            UPDATE "Dentist" SET "isActive"=FALSE, "UpdatedAt"=CURRENT_TIMESTAMP
              WHERE "id" IN ('ctuladentisttest00000001','ctuladentisttest00000002','ctuladentisttest00000003');
            UPDATE "ClinicService" SET "isActive"=FALSE
              WHERE "clinicId" IN ('ctuladentaltest00000001','ctuladentaltest00000002','ctuladentaltest00000003');
            """);
    }
}

