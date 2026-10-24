using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartScout.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ChangeMinutesToDecimal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SeasonStats_PlayerId",
                table: "SeasonStats");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.Sql(@"
                ALTER TABLE ""SeasonStats"" 
                ALTER COLUMN ""Minutes"" TYPE numeric(4,1) 
                USING NULLIF(TRIM(""Minutes""), '')::numeric(4,1);
            ");

            migrationBuilder.CreateIndex(
                name: "IX_SeasonStats_PlayerId_Season",
                table: "SeasonStats",
                columns: new[] { "PlayerId", "Season" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Players_FirstName",
                table: "Players",
                column: "FirstName")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_Players_LastName",
                table: "Players",
                column: "LastName")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SeasonStats_PlayerId_Season",
                table: "SeasonStats");

            migrationBuilder.DropIndex(
                name: "IX_Players_FirstName",
                table: "Players");

            migrationBuilder.DropIndex(
                name: "IX_Players_LastName",
                table: "Players");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.Sql(@"
                ALTER TABLE ""SeasonStats"" 
                ALTER COLUMN ""Minutes"" TYPE character varying(10) 
                USING ""Minutes""::text;
            ");

            migrationBuilder.CreateIndex(
                name: "IX_SeasonStats_PlayerId",
                table: "SeasonStats",
                column: "PlayerId");
        }
    }
}
