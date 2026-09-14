using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookingTracker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCalendarProductionHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoDeleteCancelledBookings",
                table: "CalendarConnections",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "AutoUpdateRescheduledBookings",
                table: "CalendarConnections",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "DefaultReminderMinutes",
                table: "CalendarConnections",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EventTitleFormat",
                table: "CalendarConnections",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "{Service Name} with {Guest Name}");

            migrationBuilder.AddColumn<string>(
                name: "EventVisibility",
                table: "CalendarConnections",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "default");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastFailedSyncAtUtc",
                table: "CalendarConnections",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoDeleteCancelledBookings",
                table: "CalendarConnections");

            migrationBuilder.DropColumn(
                name: "AutoUpdateRescheduledBookings",
                table: "CalendarConnections");

            migrationBuilder.DropColumn(
                name: "DefaultReminderMinutes",
                table: "CalendarConnections");

            migrationBuilder.DropColumn(
                name: "EventTitleFormat",
                table: "CalendarConnections");

            migrationBuilder.DropColumn(
                name: "EventVisibility",
                table: "CalendarConnections");

            migrationBuilder.DropColumn(
                name: "LastFailedSyncAtUtc",
                table: "CalendarConnections");
        }
    }
}
