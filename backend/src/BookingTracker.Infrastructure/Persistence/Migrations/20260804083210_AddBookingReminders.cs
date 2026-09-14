using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookingTracker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "NotifyOrganizerOnReminderSent",
                table: "NotificationSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "BookingReminders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingPageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MinutesBeforeEvent = table.Column<int>(type: "int", nullable: false),
                    ScheduledForUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MeetingStartsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Channel = table.Column<byte>(type: "tinyint", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    EmailNotificationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    QueuedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolutionReason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingReminders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookingReminders_BookingPages_BookingPageId",
                        column: x => x.BookingPageId,
                        principalTable: "BookingPages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BookingReminders_BookingSessions_BookingSessionId",
                        column: x => x.BookingSessionId,
                        principalTable: "BookingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookingReminders_BookingPageId",
                table: "BookingReminders",
                column: "BookingPageId");

            migrationBuilder.CreateIndex(
                name: "IX_BookingReminders_EmailNotificationId",
                table: "BookingReminders",
                column: "EmailNotificationId");

            migrationBuilder.CreateIndex(
                name: "IX_BookingReminders_Status_ScheduledForUtc",
                table: "BookingReminders",
                columns: new[] { "Status", "ScheduledForUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_BookingReminders_Session_Offset_Scheduled",
                table: "BookingReminders",
                columns: new[] { "BookingSessionId", "MinutesBeforeEvent" },
                unique: true,
                filter: "[Status] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookingReminders");

            migrationBuilder.DropColumn(
                name: "NotifyOrganizerOnReminderSent",
                table: "NotificationSettings");
        }
    }
}
