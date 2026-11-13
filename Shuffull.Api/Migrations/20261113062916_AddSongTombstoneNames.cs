using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shuffull.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSongTombstoneNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ArtistNames",
                table: "SongTombstones",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlaylistName",
                table: "SongTombstones",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArtistNames",
                table: "SongTombstones");

            migrationBuilder.DropColumn(
                name: "PlaylistName",
                table: "SongTombstones");
        }
    }
}
