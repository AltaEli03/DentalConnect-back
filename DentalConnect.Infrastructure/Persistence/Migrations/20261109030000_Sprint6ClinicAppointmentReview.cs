using DentalConnect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DentalConnect.Infrastructure.Persistence.Migrations;

[DbContext(typeof(DentalConnectDbContext))]
[Migration("20261109030000_Sprint6ClinicAppointmentReview")]
public sealed class Sprint6ClinicAppointmentReview : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");
        migrationBuilder.Sql("""
            ALTER TABLE "Appointment"
                ADD COLUMN "PatientFirstName" varchar(100) NULL,
                ADD COLUMN "PatientLastName" varchar(100) NULL,
                ADD COLUMN "PatientEmail" varchar(320) NULL,
                ADD COLUMN "PatientPhone" varchar(32) NULL,
                ADD COLUMN "RejectionReason" varchar(250) NULL,
                ADD COLUMN "RejectedAt" timestamp with time zone NULL,
                ADD COLUMN "RejectedBy" varchar(128) NULL;

            ALTER TABLE "Appointment" ADD CONSTRAINT "EX_Appointment_ConfirmedPatientTime"
                EXCLUDE USING gist ("PatientSubject" WITH =, tstzrange("StartsAt", "EndsAt", '[)') WITH &&)
                WHERE ("Status" = 'CONFIRMED');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "Appointment" DROP CONSTRAINT IF EXISTS "EX_Appointment_ConfirmedPatientTime";
            ALTER TABLE "Appointment"
                DROP COLUMN IF EXISTS "PatientFirstName",
                DROP COLUMN IF EXISTS "PatientLastName",
                DROP COLUMN IF EXISTS "PatientEmail",
                DROP COLUMN IF EXISTS "PatientPhone",
                DROP COLUMN IF EXISTS "RejectionReason",
                DROP COLUMN IF EXISTS "RejectedAt",
                DROP COLUMN IF EXISTS "RejectedBy";
            """);
    }
}
