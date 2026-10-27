using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartScout.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpgradeToComprehensiveStintSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SeasonStats_PlayerId_Season",
                table: "SeasonStats");

            migrationBuilder.AlterColumn<string>(
                name: "TeamName",
                table: "SeasonStats",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "TOT",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "ReboundsPer40",
                table: "SeasonStats",
                type: "numeric(5,1)",
                precision: 5,
                scale: 1,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(4,1)",
                oldPrecision: 4,
                oldScale: 1,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "PointsPer40",
                table: "SeasonStats",
                type: "numeric(5,1)",
                precision: 5,
                scale: 1,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(4,1)",
                oldPrecision: 4,
                oldScale: 1,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Pir",
                table: "SeasonStats",
                type: "numeric(5,1)",
                precision: 5,
                scale: 1,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(4,1)",
                oldPrecision: 4,
                oldScale: 1,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Minutes",
                table: "SeasonStats",
                type: "numeric(5,1)",
                precision: 5,
                scale: 1,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(4,1)",
                oldPrecision: 4,
                oldScale: 1,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "League",
                table: "SeasonStats",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "NBA",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "FoulsDrawn",
                table: "SeasonStats",
                type: "numeric(5,1)",
                precision: 5,
                scale: 1,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(4,1)",
                oldPrecision: 4,
                oldScale: 1,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "AssistsPer40",
                table: "SeasonStats",
                type: "numeric(5,1)",
                precision: 5,
                scale: 1,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(4,1)",
                oldPrecision: 4,
                oldScale: 1,
                oldNullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AssistToTurnoverRatio",
                table: "SeasonStats",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AstPer36",
                table: "SeasonStats",
                type: "numeric(5,1)",
                precision: 5,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BlkPer36",
                table: "SeasonStats",
                type: "numeric(5,1)",
                precision: 5,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompetitionPhase",
                table: "SeasonStats",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Regular Season");

            migrationBuilder.AddColumn<decimal>(
                name: "DefensiveRebounds",
                table: "SeasonStats",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EffectiveFgPct",
                table: "SeasonStats",
                type: "numeric(5,3)",
                precision: 5,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Fg2Pct",
                table: "SeasonStats",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FieldGoalsAttempted",
                table: "SeasonStats",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FieldGoalsMade",
                table: "SeasonStats",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FreeThrowsAttempted",
                table: "SeasonStats",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FreeThrowsMade",
                table: "SeasonStats",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GameScoreAverage",
                table: "SeasonStats",
                type: "numeric(5,1)",
                precision: 5,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GamesStarted",
                table: "SeasonStats",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LeagueDifficultyCoefficient",
                table: "SeasonStats",
                type: "numeric(4,2)",
                precision: 4,
                scale: 2,
                nullable: false,
                defaultValue: 1.00m);

            migrationBuilder.AddColumn<decimal>(
                name: "MinutesPlayedTotal",
                table: "SeasonStats",
                type: "numeric(6,1)",
                precision: 6,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OffensiveRebounds",
                table: "SeasonStats",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PersonalFoulsCommitted",
                table: "SeasonStats",
                type: "numeric(5,1)",
                precision: 5,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PlusMinus",
                table: "SeasonStats",
                type: "numeric(5,1)",
                precision: 5,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PtsPer36",
                table: "SeasonStats",
                type: "numeric(5,1)",
                precision: 5,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RawScrapedPayload",
                table: "SeasonStats",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RebPer36",
                table: "SeasonStats",
                type: "numeric(5,1)",
                precision: 5,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StlPer36",
                table: "SeasonStats",
                type: "numeric(5,1)",
                precision: 5,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ThreePointsAttempted",
                table: "SeasonStats",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ThreePointsMade",
                table: "SeasonStats",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TovPer36",
                table: "SeasonStats",
                type: "numeric(5,1)",
                precision: 5,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TwoPointsAttempted",
                table: "SeasonStats",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TwoPointsMade",
                table: "SeasonStats",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DraftPick",
                table: "Players",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DraftYear",
                table: "Players",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalIdsJson",
                table: "Players",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FullName",
                table: "Players",
                type: "character varying(205)",
                maxLength: 205,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "HeightCm",
                table: "Players",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedName",
                table: "Players",
                type: "character varying(205)",
                maxLength: 205,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PrimaryPosition",
                table: "Players",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecondaryPosition",
                table: "Players",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WeightKg",
                table: "Players",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WingspanCm",
                table: "Players",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "IngestionJobLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LeagueCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Season = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    LastScrapedPage = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngestionJobLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Leagues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Country = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Region = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TierLevel = table.Column<int>(type: "integer", nullable: false),
                    DifficultyCoefficient = table.Column<decimal>(type: "numeric(4,2)", precision: 4, scale: 2, nullable: false),
                    ExternalIdsJson = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Leagues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Seasons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StartYear = table.Column<int>(type: "integer", nullable: false),
                    EndYear = table.Column<int>(type: "integer", nullable: false),
                    IsCurrent = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Seasons", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SeasonStats_PlayerId_Season_League_TeamName_CompetitionPhase",
                table: "SeasonStats",
                columns: new[] { "PlayerId", "Season", "League", "TeamName", "CompetitionPhase" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Players_NormalizedName",
                table: "Players",
                column: "NormalizedName")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_IngestionJobLogs_LeagueCode_Season",
                table: "IngestionJobLogs",
                columns: new[] { "LeagueCode", "Season" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Leagues_Code",
                table: "Leagues",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Seasons_EndYear",
                table: "Seasons",
                column: "EndYear",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Seasons_Label",
                table: "Seasons",
                column: "Label",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IngestionJobLogs");

            migrationBuilder.DropTable(
                name: "Leagues");

            migrationBuilder.DropTable(
                name: "Seasons");

            migrationBuilder.DropIndex(
                name: "IX_SeasonStats_PlayerId_Season_League_TeamName_CompetitionPhase",
                table: "SeasonStats");

            migrationBuilder.DropIndex(
                name: "IX_Players_NormalizedName",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "AssistToTurnoverRatio",
                table: "SeasonStats");

            migrationBuilder.DropColumn(
                name: "AstPer36",
                table: "SeasonStats");

            migrationBuilder.DropColumn(
                name: "BlkPer36",
                table: "SeasonStats");

            migrationBuilder.DropColumn(
                name: "CompetitionPhase",
                table: "SeasonStats");

            migrationBuilder.DropColumn(
                name: "DefensiveRebounds",
                table: "SeasonStats");

            migrationBuilder.DropColumn(
                name: "EffectiveFgPct",
                table: "SeasonStats");

            migrationBuilder.DropColumn(
                name: "Fg2Pct",
                table: "SeasonStats");

            migrationBuilder.DropColumn(
                name: "FieldGoalsAttempted",
                table: "SeasonStats");

            migrationBuilder.DropColumn(
                name: "FieldGoalsMade",
                table: "SeasonStats");

            migrationBuilder.DropColumn(
                name: "FreeThrowsAttempted",
                table: "SeasonStats");

            migrationBuilder.DropColumn(
                name: "FreeThrowsMade",
                table: "SeasonStats");

            migrationBuilder.DropColumn(
                name: "GameScoreAverage",
                table: "SeasonStats");

            migrationBuilder.DropColumn(
                name: "GamesStarted",
                table: "SeasonStats");

            migrationBuilder.DropColumn(
                name: "LeagueDifficultyCoefficient",
                table: "SeasonStats");

            migrationBuilder.DropColumn(
                name: "MinutesPlayedTotal",
                table: "SeasonStats");

            migrationBuilder.DropColumn(
                name: "OffensiveRebounds",
                table: "SeasonStats");

            migrationBuilder.DropColumn(
                name: "PersonalFoulsCommitted",
                table: "SeasonStats");

            migrationBuilder.DropColumn(
                name: "PlusMinus",
                table: "SeasonStats");

            migrationBuilder.DropColumn(
                name: "PtsPer36",
                table: "SeasonStats");

            migrationBuilder.DropColumn(
                name: "RawScrapedPayload",
                table: "SeasonStats");

            migrationBuilder.DropColumn(
                name: "RebPer36",
                table: "SeasonStats");

            migrationBuilder.DropColumn(
                name: "StlPer36",
                table: "SeasonStats");

            migrationBuilder.DropColumn(
                name: "ThreePointsAttempted",
                table: "SeasonStats");

            migrationBuilder.DropColumn(
                name: "ThreePointsMade",
                table: "SeasonStats");

            migrationBuilder.DropColumn(
                name: "TovPer36",
                table: "SeasonStats");

            migrationBuilder.DropColumn(
                name: "TwoPointsAttempted",
                table: "SeasonStats");

            migrationBuilder.DropColumn(
                name: "TwoPointsMade",
                table: "SeasonStats");

            migrationBuilder.DropColumn(
                name: "DraftPick",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "DraftYear",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "ExternalIdsJson",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "FullName",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "HeightCm",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "NormalizedName",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "PrimaryPosition",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "SecondaryPosition",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "WeightKg",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "WingspanCm",
                table: "Players");

            migrationBuilder.AlterColumn<string>(
                name: "TeamName",
                table: "SeasonStats",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldDefaultValue: "TOT");

            migrationBuilder.AlterColumn<decimal>(
                name: "ReboundsPer40",
                table: "SeasonStats",
                type: "numeric(4,1)",
                precision: 4,
                scale: 1,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(5,1)",
                oldPrecision: 5,
                oldScale: 1,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "PointsPer40",
                table: "SeasonStats",
                type: "numeric(4,1)",
                precision: 4,
                scale: 1,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(5,1)",
                oldPrecision: 5,
                oldScale: 1,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Pir",
                table: "SeasonStats",
                type: "numeric(4,1)",
                precision: 4,
                scale: 1,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(5,1)",
                oldPrecision: 5,
                oldScale: 1,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Minutes",
                table: "SeasonStats",
                type: "numeric(4,1)",
                precision: 4,
                scale: 1,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(5,1)",
                oldPrecision: 5,
                oldScale: 1,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "League",
                table: "SeasonStats",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldDefaultValue: "NBA");

            migrationBuilder.AlterColumn<decimal>(
                name: "FoulsDrawn",
                table: "SeasonStats",
                type: "numeric(4,1)",
                precision: 4,
                scale: 1,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(5,1)",
                oldPrecision: 5,
                oldScale: 1,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "AssistsPer40",
                table: "SeasonStats",
                type: "numeric(4,1)",
                precision: 4,
                scale: 1,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(5,1)",
                oldPrecision: 5,
                oldScale: 1,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeasonStats_PlayerId_Season",
                table: "SeasonStats",
                columns: new[] { "PlayerId", "Season" },
                unique: true);
        }
    }
}
