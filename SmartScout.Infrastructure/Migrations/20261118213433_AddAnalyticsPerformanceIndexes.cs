using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartScout.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAnalyticsPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_SeasonStats_Season_League",
                table: "SeasonStats",
                columns: new[] { "Season", "League" });

            migrationBuilder.CreateIndex(
                name: "IX_Players_TeamName",
                table: "Players",
                column: "TeamName")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerInjuries_Status",
                table: "PlayerInjuries",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SeasonStats_Season_League",
                table: "SeasonStats");

            migrationBuilder.DropIndex(
                name: "IX_Players_TeamName",
                table: "Players");

            migrationBuilder.DropIndex(
                name: "IX_PlayerInjuries_Status",
                table: "PlayerInjuries");
        }
    }
}
