using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DentalConnect.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWebPushNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "pushPreparedAt",
                table: "UserNotification",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("UPDATE \"UserNotification\" SET \"pushPreparedAt\" = CURRENT_TIMESTAMP WHERE \"pushPreparedAt\" IS NULL;");

            migrationBuilder.CreateTable(
                name: "WebPushSubscription",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    recipientType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    recipientKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    endpoint = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    p256dh = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    auth = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    createdAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebPushSubscription", x => x.id);
                    table.CheckConstraint("CK_WebPushSubscription_RecipientType", "\"recipientType\" IN ('PATIENT','CLINIC')");
                });

            migrationBuilder.CreateTable(
                name: "PushNotificationDelivery",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    notificationId = table.Column<string>(type: "text", nullable: false),
                    subscriptionId = table.Column<string>(type: "text", nullable: false),
                    attemptCount = table.Column<int>(type: "integer", nullable: false),
                    nextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lastAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deliveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lastError = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PushNotificationDelivery", x => x.id);
                    table.ForeignKey(
                        name: "FK_PushNotificationDelivery_UserNotification_notificationId",
                        column: x => x.notificationId,
                        principalTable: "UserNotification",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PushNotificationDelivery_WebPushSubscription_subscriptionId",
                        column: x => x.subscriptionId,
                        principalTable: "WebPushSubscription",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PushNotificationDelivery_deliveredAt_nextAttemptAt",
                table: "PushNotificationDelivery",
                columns: new[] { "deliveredAt", "nextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PushNotificationDelivery_notificationId_subscriptionId",
                table: "PushNotificationDelivery",
                columns: new[] { "notificationId", "subscriptionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PushNotificationDelivery_subscriptionId",
                table: "PushNotificationDelivery",
                column: "subscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_WebPushSubscription_endpoint",
                table: "WebPushSubscription",
                column: "endpoint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WebPushSubscription_recipientType_recipientKey",
                table: "WebPushSubscription",
                columns: new[] { "recipientType", "recipientKey" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PushNotificationDelivery");

            migrationBuilder.DropTable(
                name: "WebPushSubscription");

            migrationBuilder.DropColumn(
                name: "pushPreparedAt",
                table: "UserNotification");
        }
    }
}
