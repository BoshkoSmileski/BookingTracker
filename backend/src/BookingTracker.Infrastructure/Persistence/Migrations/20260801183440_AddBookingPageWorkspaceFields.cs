using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookingTracker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingPageWorkspaceFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxBookingWindowDays",
                table: "BookingPages",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxBookingsPerDay",
                table: "BookingPages",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MinNoticeMinutes",
                table: "BookingPages",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BookingQuestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingPageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Prompt = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookingQuestions_BookingPages_BookingPageId",
                        column: x => x.BookingPageId,
                        principalTable: "BookingPages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookingQuestions_BookingPageId_DisplayOrder",
                table: "BookingQuestions",
                columns: new[] { "BookingPageId", "DisplayOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookingQuestions");

            migrationBuilder.DropColumn(
                name: "MaxBookingWindowDays",
                table: "BookingPages");

            migrationBuilder.DropColumn(
                name: "MaxBookingsPerDay",
                table: "BookingPages");

            migrationBuilder.DropColumn(
                name: "MinNoticeMinutes",
                table: "BookingPages");
        }
    }
}
