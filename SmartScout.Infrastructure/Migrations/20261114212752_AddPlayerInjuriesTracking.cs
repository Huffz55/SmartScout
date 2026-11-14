using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartScout.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerInjuriesTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE ""SeasonStats"" ADD COLUMN IF NOT EXISTS ""EuroLeagueTransitionIndex"" numeric(5,2);
ALTER TABLE ""SeasonStats"" ADD COLUMN IF NOT EXISTS ""StocksPer36"" numeric(4,1);
ALTER TABLE ""SeasonStats"" ADD COLUMN IF NOT EXISTS ""ThreePointRate"" numeric(4,3);

ALTER TABLE ""Players"" ADD COLUMN IF NOT EXISTS ""AgencyName"" character varying(120);
ALTER TABLE ""Players"" ADD COLUMN IF NOT EXISTS ""ContractExpirySeason"" integer;
ALTER TABLE ""Players"" ADD COLUMN IF NOT EXISTS ""DominantHand"" character varying(10) NOT NULL DEFAULT 'Right';
ALTER TABLE ""Players"" ADD COLUMN IF NOT EXISTS ""EstimatedMarketValueTier"" character varying(60);
ALTER TABLE ""Players"" ADD COLUMN IF NOT EXISTS ""IsWatchlisted"" boolean NOT NULL DEFAULT FALSE;
ALTER TABLE ""Players"" ADD COLUMN IF NOT EXISTS ""ScoutNotes"" text;
ALTER TABLE ""Players"" ADD COLUMN IF NOT EXISTS ""StandingReachCm"" integer;

CREATE TABLE IF NOT EXISTS ""PlayerInjuries"" (
    ""Id"" uuid NOT NULL,
    ""PlayerId"" uuid NOT NULL,
    ""Season"" integer NOT NULL,
    ""InjuryType"" character varying(100) NOT NULL,
    ""GamesMissed"" integer NOT NULL DEFAULT 0,
    ""Status"" character varying(50) NOT NULL DEFAULT 'Recovered',
    ""ReportedAt"" timestamp without time zone NOT NULL,
    ""Source"" character varying(60),
    ""Notes"" character varying(350),
    CONSTRAINT ""PK_PlayerInjuries"" PRIMARY KEY (""Id""),
    CONSTRAINT ""FK_PlayerInjuries_Players_PlayerId"" FOREIGN KEY (""PlayerId"") REFERENCES ""Players"" (""Id"") ON DELETE CASCADE
);

ALTER TABLE ""PlayerInjuries"" ADD COLUMN IF NOT EXISTS ""Source"" character varying(60);
ALTER TABLE ""PlayerInjuries"" ADD COLUMN IF NOT EXISTS ""Notes"" character varying(350);
CREATE INDEX IF NOT EXISTS ""IX_PlayerInjuries_PlayerId"" ON ""PlayerInjuries"" (""PlayerId"");
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerInjuries");

            migrationBuilder.DropColumn(
                name: "EuroLeagueTransitionIndex",
                table: "SeasonStats");

            migrationBuilder.DropColumn(
                name: "StocksPer36",
                table: "SeasonStats");

            migrationBuilder.DropColumn(
                name: "ThreePointRate",
                table: "SeasonStats");

            migrationBuilder.DropColumn(
                name: "AgencyName",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "ContractExpirySeason",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "DominantHand",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "EstimatedMarketValueTier",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "IsWatchlisted",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "ScoutNotes",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "StandingReachCm",
                table: "Players");

            migrationBuilder.AlterColumn<DateTime>(
                name: "BirthDate",
                table: "Players",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "UpdatedAt",
                table: "IngestionJobLogs",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone");
        }
    }
}
