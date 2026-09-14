using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookingTracker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NarrowBookingConflictLockFootprint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BookingSessions_BookingPageId_Status",
                table: "BookingSessions");

            migrationBuilder.CreateIndex(
                name: "IX_BookingSessions_BookingPageId_Status_SelectedDate",
                table: "BookingSessions",
                columns: new[] { "BookingPageId", "Status", "SelectedDate" })
                .Annotation("SqlServer:Include", new[] { "SelectedTime" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BookingSessions_BookingPageId_Status_SelectedDate",
                table: "BookingSessions");

            migrationBuilder.CreateIndex(
                name: "IX_BookingSessions_BookingPageId_Status",
                table: "BookingSessions",
                columns: new[] { "BookingPageId", "Status" });
        }
    }
}
