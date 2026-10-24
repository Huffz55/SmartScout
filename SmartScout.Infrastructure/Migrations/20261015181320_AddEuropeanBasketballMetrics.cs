using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartScout.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEuropeanBasketballMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AssistsPer40",
                table: "SeasonStats",
                type: "numeric(4,1)",
                precision: 4,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FoulsDrawn",
                table: "SeasonStats",
                type: "numeric(4,1)",
                precision: 4,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Pir",
                table: "SeasonStats",
                type: "numeric(4,1)",
                precision: 4,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PointsPer40",
                table: "SeasonStats",
                type: "numeric(4,1)",
                precision: 4,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ReboundsPer40",
                table: "SeasonStats",
                type: "numeric(4,1)",
                precision: 4,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BirthDate",
                table: "Players",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Nationality",
                table: "Players",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AssistsPer40",
                table: "SeasonStats");

            migrationBuilder.DropColumn(
                name: "FoulsDrawn",
                table: "SeasonStats");

            migrationBuilder.DropColumn(
                name: "Pir",
                table: "SeasonStats");

            migrationBuilder.DropColumn(
                name: "PointsPer40",
                table: "SeasonStats");

            migrationBuilder.DropColumn(
                name: "ReboundsPer40",
                table: "SeasonStats");

            migrationBuilder.DropColumn(
                name: "BirthDate",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "Nationality",
                table: "Players");
        }
    }
}
