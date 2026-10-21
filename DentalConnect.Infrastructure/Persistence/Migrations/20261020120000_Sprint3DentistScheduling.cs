using DentalConnect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DentalConnect.Infrastructure.Persistence.Migrations;

[DbContext(typeof(DentalConnectDbContext))]
[Migration("20261020120000_Sprint3DentistScheduling")]
public partial class Sprint3DentistScheduling : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(name: "CreatedAt", table: "Dentist", type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP");
        migrationBuilder.AddColumn<DateTime>(name: "UpdatedAt", table: "Dentist", type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP");

        migrationBuilder.CreateTable(name: "DentistService", columns: table => new
        {
            Id = table.Column<string>(type: "text", nullable: false),
            DentistId = table.Column<string>(type: "text", nullable: false),
            DentalServiceId = table.Column<string>(type: "text", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_DentistService", x => x.Id);
            table.ForeignKey("FK_DentistService_Dentist_DentistId", x => x.DentistId, "Dentist", "id", onDelete: ReferentialAction.Cascade);
            table.ForeignKey("FK_DentistService_DentalService_DentalServiceId", x => x.DentalServiceId, "DentalService", "id", onDelete: ReferentialAction.Restrict);
        });

        migrationBuilder.CreateTable(name: "DentistWeeklySchedule", columns: table => new
        {
            Id = table.Column<string>(type: "text", nullable: false),
            DentistId = table.Column<string>(type: "text", nullable: false),
            DayOfWeek = table.Column<int>(type: "integer", nullable: false),
            StartsAt = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
            EndsAt = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
            BreakStartsAt = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
            BreakEndsAt = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
            TimeZoneId = table.Column<string>(type: "text", nullable: false),
            CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_DentistWeeklySchedule", x => x.Id);
            table.ForeignKey("FK_DentistWeeklySchedule_Dentist_DentistId", x => x.DentistId, "Dentist", "id", onDelete: ReferentialAction.Cascade);
        });

        migrationBuilder.CreateTable(name: "DentistScheduleBlock", columns: table => new
        {
            Id = table.Column<string>(type: "text", nullable: false),
            DentistId = table.Column<string>(type: "text", nullable: false),
            StartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            EndsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            Reason = table.Column<string>(type: "text", nullable: true),
            CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_DentistScheduleBlock", x => x.Id);
            table.ForeignKey("FK_DentistScheduleBlock_Dentist_DentistId", x => x.DentistId, "Dentist", "id", onDelete: ReferentialAction.Cascade);
        });

        migrationBuilder.CreateIndex(name: "IX_DentistService_DentistId_DentalServiceId", table: "DentistService", columns: new[] { "DentistId", "DentalServiceId" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_DentistService_DentalServiceId", table: "DentistService", column: "DentalServiceId");
        migrationBuilder.CreateIndex(name: "IX_DentistWeeklySchedule_DentistId", table: "DentistWeeklySchedule", column: "DentistId");
        migrationBuilder.CreateIndex(name: "IX_DentistScheduleBlock_DentistId", table: "DentistScheduleBlock", column: "DentistId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "DentistService");
        migrationBuilder.DropTable(name: "DentistWeeklySchedule");
        migrationBuilder.DropTable(name: "DentistScheduleBlock");
        migrationBuilder.DropColumn(name: "CreatedAt", table: "Dentist");
        migrationBuilder.DropColumn(name: "UpdatedAt", table: "Dentist");
    }
}
