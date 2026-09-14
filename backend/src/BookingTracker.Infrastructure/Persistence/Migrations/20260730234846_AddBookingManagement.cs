using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookingTracker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BookingReference",
                table: "BookingSessions",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "BookingSessions",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "BookingSessions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "CancelledBy",
                table: "BookingSessions",
                type: "tinyint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublicToken",
                table: "BookingSessions",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RescheduleCount",
                table: "BookingSessions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "RescheduledAt",
                table: "BookingSessions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookingSessions_BookingReference",
                table: "BookingSessions",
                column: "BookingReference",
                unique: true,
                filter: "[BookingReference] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BookingSessions_PublicToken",
                table: "BookingSessions",
                column: "PublicToken",
                unique: true,
                filter: "[PublicToken] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BookingSessions_Status_SelectedDate_SelectedTime",
                table: "BookingSessions",
                columns: new[] { "Status", "SelectedDate", "SelectedTime" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BookingSessions_BookingReference",
                table: "BookingSessions");

            migrationBuilder.DropIndex(
                name: "IX_BookingSessions_PublicToken",
                table: "BookingSessions");

            migrationBuilder.DropIndex(
                name: "IX_BookingSessions_Status_SelectedDate_SelectedTime",
                table: "BookingSessions");

            migrationBuilder.DropColumn(
                name: "BookingReference",
                table: "BookingSessions");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "BookingSessions");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "BookingSessions");

            migrationBuilder.DropColumn(
                name: "CancelledBy",
                table: "BookingSessions");

            migrationBuilder.DropColumn(
                name: "PublicToken",
                table: "BookingSessions");

            migrationBuilder.DropColumn(
                name: "RescheduleCount",
                table: "BookingSessions");

            migrationBuilder.DropColumn(
                name: "RescheduledAt",
                table: "BookingSessions");
        }
    }
}
