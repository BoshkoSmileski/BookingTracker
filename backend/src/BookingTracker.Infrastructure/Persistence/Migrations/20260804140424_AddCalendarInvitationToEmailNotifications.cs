using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookingTracker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCalendarInvitationToEmailNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IcsContent",
                table: "EmailNotifications",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IcsFileName",
                table: "EmailNotifications",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IcsMethod",
                table: "EmailNotifications",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IcsContent",
                table: "EmailNotifications");

            migrationBuilder.DropColumn(
                name: "IcsFileName",
                table: "EmailNotifications");

            migrationBuilder.DropColumn(
                name: "IcsMethod",
                table: "EmailNotifications");
        }
    }
}
