using DentalConnect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DentalConnect.Infrastructure.Persistence.Migrations;

[DbContext(typeof(DentalConnectDbContext))]
[Migration("20261109000000_Sprint5Appointments")]
public sealed class Sprint5Appointments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");
        migrationBuilder.Sql("""
            CREATE TABLE "Appointment" (
                "Id" text PRIMARY KEY,
                "Folio" varchar(32) NOT NULL,
                "PatientSubject" varchar(128) NOT NULL,
                "ClinicId" text NOT NULL REFERENCES "Clinic"(id) ON DELETE RESTRICT,
                "DentistId" text NOT NULL REFERENCES "Dentist"(id) ON DELETE RESTRICT,
                "DentalServiceId" text NOT NULL REFERENCES "DentalService"(id) ON DELETE RESTRICT,
                "StartsAt" timestamp with time zone NOT NULL,
                "EndsAt" timestamp with time zone NOT NULL,
                "Status" varchar(24) NOT NULL DEFAULT 'PENDING',
                "IdempotencyKey" varchar(128) NOT NULL,
                "RequestFingerprint" varchar(64) NOT NULL,
                "TimeZoneId" varchar(64) NOT NULL DEFAULT 'America/Mexico_City',
                "CreatedAt" timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
                CONSTRAINT "CK_Appointment_ValidRange" CHECK ("StartsAt" < "EndsAt"),
                CONSTRAINT "CK_Appointment_Status" CHECK ("Status" IN ('PENDING','CONFIRMED','REJECTED','CANCELLED'))
            );
            CREATE UNIQUE INDEX "IX_Appointment_Folio" ON "Appointment" ("Folio");
            CREATE UNIQUE INDEX "IX_Appointment_PatientSubject_IdempotencyKey" ON "Appointment" ("PatientSubject", "IdempotencyKey");
            CREATE INDEX "IX_Appointment_DentistId_StartsAt_EndsAt_Status" ON "Appointment" ("DentistId", "StartsAt", "EndsAt", "Status");
            ALTER TABLE "Appointment" ADD CONSTRAINT "EX_Appointment_ConfirmedDentistTime"
                EXCLUDE USING gist ("DentistId" WITH =, tstzrange("StartsAt", "EndsAt", '[)') WITH &&)
                WHERE ("Status" = 'CONFIRMED');
            CREATE TABLE "ClinicNotificationEvent" (
                "Id" text PRIMARY KEY,
                "AppointmentId" text NOT NULL REFERENCES "Appointment"("Id") ON DELETE CASCADE,
                "EventType" varchar(64) NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
                "ProcessedAt" timestamp with time zone NULL
            );
            CREATE UNIQUE INDEX "IX_ClinicNotificationEvent_AppointmentId_EventType"
                ON "ClinicNotificationEvent" ("AppointmentId", "EventType");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""DROP TABLE IF EXISTS "ClinicNotificationEvent"; DROP TABLE IF EXISTS "Appointment";""");
    }
}
