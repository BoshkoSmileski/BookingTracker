using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookingTracker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAvailabilityExceptionEndDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AvailabilityExceptions_OrganizerId_Date",
                table: "AvailabilityExceptions");

            migrationBuilder.AddColumn<DateOnly>(
                name: "EndDate",
                table: "AvailabilityExceptions",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            // Every exception that existed before ranges was a single day, so its
            // inclusive end is its start. Without this backfill the scaffolded
            // default (year 1) would leave EndDate < Date on every existing row,
            // and AvailabilityException.Covers would report that none of them
            // apply to any date - silently un-blocking dates the organizer
            // deliberately blocked.
            migrationBuilder.Sql("UPDATE [AvailabilityExceptions] SET [EndDate] = [Date];");

            migrationBuilder.CreateIndex(
                name: "IX_AvailabilityExceptions_OrganizerId_Date_EndDate",
                table: "AvailabilityExceptions",
                columns: new[] { "OrganizerId", "Date", "EndDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AvailabilityExceptions_OrganizerId_Date_EndDate",
                table: "AvailabilityExceptions");

            migrationBuilder.DropColumn(
                name: "EndDate",
                table: "AvailabilityExceptions");

            migrationBuilder.CreateIndex(
                name: "IX_AvailabilityExceptions_OrganizerId_Date",
                table: "AvailabilityExceptions",
                columns: new[] { "OrganizerId", "Date" });
        }
    }
}
