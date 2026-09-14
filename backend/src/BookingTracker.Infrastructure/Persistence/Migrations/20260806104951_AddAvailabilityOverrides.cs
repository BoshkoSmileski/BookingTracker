using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookingTracker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAvailabilityOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AvailabilityOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AvailabilityOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AvailabilityOverrides_Organizers_OrganizerId",
                        column: x => x.OrganizerId,
                        principalTable: "Organizers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AvailabilityOverrideRanges",
                columns: table => new
                {
                    AvailabilityOverrideId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Start = table.Column<TimeOnly>(type: "time", nullable: false),
                    End = table.Column<TimeOnly>(type: "time", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AvailabilityOverrideRanges", x => new { x.AvailabilityOverrideId, x.Id });
                    table.ForeignKey(
                        name: "FK_AvailabilityOverrideRanges_AvailabilityOverrides_AvailabilityOverrideId",
                        column: x => x.AvailabilityOverrideId,
                        principalTable: "AvailabilityOverrides",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AvailabilityOverrides_OrganizerId_Date",
                table: "AvailabilityOverrides",
                columns: new[] { "OrganizerId", "Date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AvailabilityOverrideRanges");

            migrationBuilder.DropTable(
                name: "AvailabilityOverrides");
        }
    }
}
