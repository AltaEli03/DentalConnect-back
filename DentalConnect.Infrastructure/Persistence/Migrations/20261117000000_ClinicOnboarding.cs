using DentalConnect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DentalConnect.Infrastructure.Persistence.Migrations;

[DbContext(typeof(DentalConnectDbContext))]
[Migration("20261117000000_ClinicOnboarding")]
public sealed class ClinicOnboarding : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE "ClinicApplication" (
            "id" text NOT NULL CONSTRAINT "ClinicApplication_pkey" PRIMARY KEY,
            "ownerSubject" character varying(128) NOT NULL,
            "ownerEmail" character varying(320) NOT NULL,
            "clinicName" character varying(120) NOT NULL,
            "phone" character varying(24) NOT NULL,
            "address" character varying(200) NOT NULL,
            "municipality" character varying(100) NOT NULL,
            "state" character varying(100) NOT NULL,
            "postalCode" character varying(12) NOT NULL,
            "status" character varying(24) NOT NULL,
            "reviewNote" character varying(250) NULL,
            "reviewedBy" character varying(128) NULL,
            "clinicId" text NULL,
            "createdAt" timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
            "reviewedAt" timestamp with time zone NULL,
            CONSTRAINT "FK_ClinicApplication_Clinic_clinicId" FOREIGN KEY ("clinicId") REFERENCES "Clinic" ("id") ON DELETE RESTRICT,
            CONSTRAINT "CK_ClinicApplication_Status" CHECK ("status" IN ('PENDING_REVIEW','PROVISIONING','APPROVED','REJECTED'))
        );
        CREATE INDEX "IX_ClinicApplication_Status_CreatedAt" ON "ClinicApplication" ("status","createdAt");
        CREATE UNIQUE INDEX "UX_ClinicApplication_Owner_Active" ON "ClinicApplication" ("ownerSubject") WHERE "status" IN ('PENDING_REVIEW','PROVISIONING','APPROVED');
        CREATE TABLE "ClinicMembership" (
            "id" text NOT NULL CONSTRAINT "ClinicMembership_pkey" PRIMARY KEY,
            "userSubject" character varying(128) NOT NULL,
            "clinicId" text NOT NULL,
            "role" character varying(32) NOT NULL,
            "createdAt" timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
            CONSTRAINT "FK_ClinicMembership_Clinic_clinicId" FOREIGN KEY ("clinicId") REFERENCES "Clinic" ("id") ON DELETE CASCADE,
            CONSTRAINT "CK_ClinicMembership_Role" CHECK ("role" = 'CLINIC_ADMIN')
        );
        CREATE UNIQUE INDEX "UX_ClinicMembership_UserSubject" ON "ClinicMembership" ("userSubject");
        CREATE INDEX "IX_ClinicMembership_ClinicId" ON "ClinicMembership" ("clinicId");
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP TABLE IF EXISTS "ClinicApplication";
        DROP TABLE IF EXISTS "ClinicMembership";
        """);
}
