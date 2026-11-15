using DentalConnect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DentalConnect.Infrastructure.Persistence.Migrations;

[DbContext(typeof(DentalConnectDbContext))]
[Migration("20261110120000_Sprint7AppointmentProposal")]
public sealed class Sprint7AppointmentProposal : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "Appointment" DROP CONSTRAINT "CK_Appointment_Status";
            ALTER TABLE "Appointment" ADD CONSTRAINT "CK_Appointment_Status"
                CHECK ("Status" IN ('PENDING','CONFIRMED','REJECTED','CANCELLED','PROPOSED'));
            ALTER TABLE "Appointment"
                ADD COLUMN "ProposedStartsAt" timestamp with time zone NULL,
                ADD COLUMN "ProposedEndsAt" timestamp with time zone NULL,
                ADD COLUMN "ProposalExpiresAt" timestamp with time zone NULL;
            ALTER TABLE "Appointment" ADD CONSTRAINT "CK_Appointment_Proposal" CHECK (
                ("ProposedStartsAt" IS NULL AND "ProposedEndsAt" IS NULL AND "ProposalExpiresAt" IS NULL)
                OR ("ProposedStartsAt" IS NOT NULL AND "ProposedEndsAt" IS NOT NULL
                    AND "ProposalExpiresAt" IS NOT NULL AND "ProposedStartsAt" < "ProposedEndsAt"));
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE "Appointment" SET "Status" = 'PENDING' WHERE "Status" = 'PROPOSED';
            ALTER TABLE "Appointment" DROP CONSTRAINT IF EXISTS "CK_Appointment_Proposal";
            ALTER TABLE "Appointment"
                DROP COLUMN IF EXISTS "ProposedStartsAt",
                DROP COLUMN IF EXISTS "ProposedEndsAt",
                DROP COLUMN IF EXISTS "ProposalExpiresAt";
            ALTER TABLE "Appointment" DROP CONSTRAINT "CK_Appointment_Status";
            ALTER TABLE "Appointment" ADD CONSTRAINT "CK_Appointment_Status"
                CHECK ("Status" IN ('PENDING','CONFIRMED','REJECTED','CANCELLED'));
            """);
    }
}
