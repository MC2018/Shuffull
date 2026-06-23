using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shuffull.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSongEnrichment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Bpm",
                table: "Songs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LyricsInstrumental",
                table: "Songs",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "LyricsOffsetMs",
                table: "Songs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "LyricsSource",
                table: "Songs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlainLyrics",
                table: "Songs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SyncedLyrics",
                table: "Songs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Bpm",
                table: "SongImports",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LyricsJson",
                table: "SongImports",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Bpm",
                table: "Songs");

            migrationBuilder.DropColumn(
                name: "LyricsInstrumental",
                table: "Songs");

            migrationBuilder.DropColumn(
                name: "LyricsOffsetMs",
                table: "Songs");

            migrationBuilder.DropColumn(
                name: "LyricsSource",
                table: "Songs");

            migrationBuilder.DropColumn(
                name: "PlainLyrics",
                table: "Songs");

            migrationBuilder.DropColumn(
                name: "SyncedLyrics",
                table: "Songs");

            migrationBuilder.DropColumn(
                name: "Bpm",
                table: "SongImports");

            migrationBuilder.DropColumn(
                name: "LyricsJson",
                table: "SongImports");
        }
    }
}
