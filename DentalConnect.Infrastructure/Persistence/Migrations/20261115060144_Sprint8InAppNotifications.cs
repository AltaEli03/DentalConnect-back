using Microsoft.EntityFrameworkCore.Migrations;

namespace DentalConnect.Infrastructure.Persistence.Migrations;

public partial class Sprint8InAppNotifications : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE "UserNotification" (
                "id" text NOT NULL CONSTRAINT "UserNotification_pkey" PRIMARY KEY,
                "recipientType" character varying(16) NOT NULL,
                "recipientKey" character varying(256) NOT NULL,
                "appointmentId" text NOT NULL,
                "eventType" character varying(64) NOT NULL,
                "title" character varying(120) NOT NULL,
                "message" character varying(500) NOT NULL,
                "createdAt" timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
                "readAt" timestamp with time zone NULL,
                CONSTRAINT "CK_UserNotification_RecipientType"
                    CHECK ("recipientType" IN ('PATIENT','CLINIC')),
                CONSTRAINT "FK_UserNotification_Appointment_appointmentId"
                    FOREIGN KEY ("appointmentId") REFERENCES "Appointment" ("Id") ON DELETE CASCADE
            );
            CREATE INDEX "IX_UserNotification_Recipient_CreatedAt"
                ON "UserNotification" ("recipientType", "recipientKey", "createdAt");
            CREATE UNIQUE INDEX "IX_UserNotification_Recipient_Appointment_Event"
                ON "UserNotification" ("recipientType", "recipientKey", "appointmentId", "eventType");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE IF EXISTS \"UserNotification\";");
    }
}
