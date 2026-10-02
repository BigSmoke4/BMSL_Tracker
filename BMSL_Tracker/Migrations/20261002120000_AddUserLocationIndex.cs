using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BMSL_Tracker.Migrations
{
    /// <inheritdoc />
    public partial class AddUserLocationIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_UserLocations_UserId_Timestamp",
                table: "UserLocations",
                columns: new[] { "UserId", "Timestamp" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserLocations_UserId_Timestamp",
                table: "UserLocations");
        }
    }
}
