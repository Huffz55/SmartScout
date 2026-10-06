using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartScout.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerSeasonStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SeasonStats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Season = table.Column<int>(type: "integer", nullable: false),
                    GamesPlayed = table.Column<int>(type: "integer", nullable: false),
                    Minutes = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Points = table.Column<decimal>(type: "numeric", nullable: true),
                    Assists = table.Column<decimal>(type: "numeric", nullable: true),
                    Rebounds = table.Column<decimal>(type: "numeric", nullable: true),
                    Steals = table.Column<decimal>(type: "numeric", nullable: true),
                    Blocks = table.Column<decimal>(type: "numeric", nullable: true),
                    FgPct = table.Column<decimal>(type: "numeric", nullable: true),
                    Fg3Pct = table.Column<decimal>(type: "numeric", nullable: true),
                    FtPct = table.Column<decimal>(type: "numeric", nullable: true),
                    Turnovers = table.Column<decimal>(type: "numeric", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeasonStats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeasonStats_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SeasonStats_PlayerId",
                table: "SeasonStats",
                column: "PlayerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SeasonStats");
        }
    }
}
