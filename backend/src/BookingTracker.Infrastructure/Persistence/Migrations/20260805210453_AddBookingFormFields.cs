using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookingTracker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingFormFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BookingFormFields",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingPageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Type = table.Column<byte>(type: "tinyint", nullable: false),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingFormFields", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookingFormFields_BookingPages_BookingPageId",
                        column: x => x.BookingPageId,
                        principalTable: "BookingPages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BookingSessionAnswers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingFormFieldId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingSessionAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookingSessionAnswers_BookingSessions_BookingSessionId",
                        column: x => x.BookingSessionId,
                        principalTable: "BookingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookingFormFields_BookingPageId_DisplayOrder",
                table: "BookingFormFields",
                columns: new[] { "BookingPageId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_BookingSessionAnswers_BookingSessionId_BookingFormFieldId",
                table: "BookingSessionAnswers",
                columns: new[] { "BookingSessionId", "BookingFormFieldId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookingFormFields");

            migrationBuilder.DropTable(
                name: "BookingSessionAnswers");
        }
    }
}
