using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BMSL_Tracker.Migrations;

public partial class AddUserLocationTimestampIndex : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_UserLocations_UserId",
            table: "UserLocations");

        migrationBuilder.CreateIndex(
            name: "IX_UserLocations_UserId_Timestamp",
            table: "UserLocations",
            columns: new[] { "UserId", "Timestamp" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_UserLocations_UserId_Timestamp",
            table: "UserLocations");

        migrationBuilder.CreateIndex(
            name: "IX_UserLocations_UserId",
            table: "UserLocations",
            column: "UserId");
    }
}
