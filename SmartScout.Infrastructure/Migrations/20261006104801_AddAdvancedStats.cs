using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartScout.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAdvancedStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AssistPct",
                table: "SeasonStats",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BoxPlusMinus",
                table: "SeasonStats",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PlayerEfficiencyRating",
                table: "SeasonStats",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalReboundPct",
                table: "SeasonStats",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TrueShootingPct",
                table: "SeasonStats",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UsagePct",
                table: "SeasonStats",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WinShares",
                table: "SeasonStats",
                type: "numeric",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AssistPct",
                table: "SeasonStats");

            migrationBuilder.DropColumn(
                name: "BoxPlusMinus",
                table: "SeasonStats");

            migrationBuilder.DropColumn(
                name: "PlayerEfficiencyRating",
                table: "SeasonStats");

            migrationBuilder.DropColumn(
                name: "TotalReboundPct",
                table: "SeasonStats");

            migrationBuilder.DropColumn(
                name: "TrueShootingPct",
                table: "SeasonStats");

            migrationBuilder.DropColumn(
                name: "UsagePct",
                table: "SeasonStats");

            migrationBuilder.DropColumn(
                name: "WinShares",
                table: "SeasonStats");
        }
    }
}
