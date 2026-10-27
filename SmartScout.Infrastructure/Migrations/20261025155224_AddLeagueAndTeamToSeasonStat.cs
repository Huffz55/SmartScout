using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartScout.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLeagueAndTeamToSeasonStat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "League",
                table: "SeasonStats",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TeamName",
                table: "SeasonStats",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "League",
                table: "SeasonStats");

            migrationBuilder.DropColumn(
                name: "TeamName",
                table: "SeasonStats");
        }
    }
}
