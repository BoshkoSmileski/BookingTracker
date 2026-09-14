using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookingTracker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingMeetingProvider : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "MeetingProvider",
                table: "BookingSessions",
                type: "tinyint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MeetingUrl",
                table: "BookingSessions",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "MeetingProvider",
                table: "BookingPages",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MeetingProvider",
                table: "BookingSessions");

            migrationBuilder.DropColumn(
                name: "MeetingUrl",
                table: "BookingSessions");

            migrationBuilder.DropColumn(
                name: "MeetingProvider",
                table: "BookingPages");
        }
    }
}
