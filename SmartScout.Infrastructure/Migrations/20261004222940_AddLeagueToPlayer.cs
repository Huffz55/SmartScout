using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartScout.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLeagueToPlayer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "League",
                table: "Players",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "League",
                table: "Players");
        }
    }
}
