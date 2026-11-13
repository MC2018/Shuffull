using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shuffull.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSongTombstoneUserIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_SongTombstones_DeletedByUserId_DeletedAt_SongId",
                table: "SongTombstones",
                columns: new[] { "DeletedByUserId", "DeletedAt", "SongId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SongTombstones_DeletedByUserId_DeletedAt_SongId",
                table: "SongTombstones");
        }
    }
}
