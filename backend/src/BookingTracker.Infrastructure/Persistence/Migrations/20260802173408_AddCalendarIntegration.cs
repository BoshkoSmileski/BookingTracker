using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookingTracker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCalendarIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CalendarConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<byte>(type: "tinyint", nullable: false),
                    ExternalAccountEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    ExternalCalendarId = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ExternalCalendarName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EncryptedAccessToken = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    EncryptedRefreshToken = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    AccessTokenExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    LastSyncError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    LastSyncedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ImportBusyEvents = table.Column<bool>(type: "bit", nullable: false),
                    ExportBookings = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalendarConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CalendarConnections_Organizers_OrganizerId",
                        column: x => x.OrganizerId,
                        principalTable: "Organizers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CalendarSyncedEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CalendarConnectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExternalEventId = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalendarSyncedEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CalendarSyncedEvents_BookingSessions_BookingSessionId",
                        column: x => x.BookingSessionId,
                        principalTable: "BookingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CalendarSyncedEvents_CalendarConnections_CalendarConnectionId",
                        column: x => x.CalendarConnectionId,
                        principalTable: "CalendarConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CalendarConnections_OrganizerId",
                table: "CalendarConnections",
                column: "OrganizerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CalendarSyncedEvents_BookingSessionId",
                table: "CalendarSyncedEvents",
                column: "BookingSessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CalendarSyncedEvents_CalendarConnectionId",
                table: "CalendarSyncedEvents",
                column: "CalendarConnectionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CalendarSyncedEvents");

            migrationBuilder.DropTable(
                name: "CalendarConnections");
        }
    }
}
