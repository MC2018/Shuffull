using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shuffull.Api.Migrations
{
    /// <inheritdoc />
    public partial class RemoveSongArtistId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The new key is (SongId, ArtistId), so a pair linked twice would fail it. Keep one row per pair. Prod had
            // none on 2026-11-13; this only guards rows written since.
            migrationBuilder.Sql("""
                DELETE FROM "SongArtists" a
                USING "SongArtists" b
                WHERE a."SongId" = b."SongId"
                  AND a."ArtistId" = b."ArtistId"
                  AND a."SongArtistId" > b."SongArtistId";
                """);

            migrationBuilder.DropPrimaryKey(
                name: "PK_SongArtists",
                table: "SongArtists");

            migrationBuilder.DropIndex(
                name: "IX_SongArtists_SongId",
                table: "SongArtists");

            migrationBuilder.DropColumn(
                name: "SongArtistId",
                table: "SongArtists");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SongArtists",
                table: "SongArtists",
                columns: new[] { "SongId", "ArtistId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_SongArtists",
                table: "SongArtists");

            migrationBuilder.AddColumn<string>(
                name: "SongArtistId",
                table: "SongArtists",
                type: "text",
                nullable: false,
                defaultValue: "");

            // Every row needs its own id before it can be the key again; the pair is unique, so derive it from that.
            migrationBuilder.Sql("""UPDATE "SongArtists" SET "SongArtistId" = "SongId" || ':' || "ArtistId";""");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SongArtists",
                table: "SongArtists",
                column: "SongArtistId");

            migrationBuilder.CreateIndex(
                name: "IX_SongArtists_SongId",
                table: "SongArtists",
                column: "SongId");
        }
    }
}
