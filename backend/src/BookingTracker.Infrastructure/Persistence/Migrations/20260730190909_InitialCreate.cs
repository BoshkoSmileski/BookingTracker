using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookingTracker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Organizers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organizers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BookingPages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingPages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookingPages_Organizers_OrganizerId",
                        column: x => x.OrganizerId,
                        principalTable: "Organizers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BookingSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingPageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SelectedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    SelectedTime = table.Column<TimeOnly>(type: "time", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastActivityAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AbandonedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastClientSequenceNumber = table.Column<int>(type: "int", nullable: false),
                    LastClientIp = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    LastUserAgent = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookingSessions_BookingPages_BookingPageId",
                        column: x => x.BookingPageId,
                        principalTable: "BookingPages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BookingSessionEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingPageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<byte>(type: "tinyint", nullable: false),
                    FieldName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    OldValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClientSequenceNumber = table.Column<int>(type: "int", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClientIp = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingSessionEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookingSessionEvents_BookingPages_BookingPageId",
                        column: x => x.BookingPageId,
                        principalTable: "BookingPages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BookingSessionEvents_BookingSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "BookingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookingPages_OrganizerId",
                table: "BookingPages",
                column: "OrganizerId");

            migrationBuilder.CreateIndex(
                name: "IX_BookingPages_Slug",
                table: "BookingPages",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookingSessionEvents_BookingPageId_Timestamp",
                table: "BookingSessionEvents",
                columns: new[] { "BookingPageId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_BookingSessionEvents_SessionId_ClientSequenceNumber",
                table: "BookingSessionEvents",
                columns: new[] { "SessionId", "ClientSequenceNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_BookingSessions_BookingPageId_Status",
                table: "BookingSessions",
                columns: new[] { "BookingPageId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_BookingSessions_Status_LastActivityAt",
                table: "BookingSessions",
                columns: new[] { "Status", "LastActivityAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookingSessionEvents");

            migrationBuilder.DropTable(
                name: "BookingSessions");

            migrationBuilder.DropTable(
                name: "BookingPages");

            migrationBuilder.DropTable(
                name: "Organizers");
        }
    }
}
