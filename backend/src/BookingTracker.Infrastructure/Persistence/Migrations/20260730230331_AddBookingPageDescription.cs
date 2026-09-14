using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookingTracker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingPageDescription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "BookingPages",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Description",
                table: "BookingPages");
        }
    }
}
