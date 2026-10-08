using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shuffull.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSongTombstone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SongTombstones",
                columns: table => new
                {
                    SongId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    ExternalSongId = table.Column<string>(type: "text", nullable: true),
                    FileHash = table.Column<string>(type: "text", nullable: false),
                    FileExtension = table.Column<string>(type: "text", nullable: false),
                    DeletedByUserId = table.Column<string>(type: "text", nullable: false),
                    PlaylistId = table.Column<string>(type: "text", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MediaSweptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MediaDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    MediaSweepFailures = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SongTombstones", x => x.SongId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SongTombstones_DeletedAt",
                table: "SongTombstones",
                column: "DeletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SongTombstones_FileHash",
                table: "SongTombstones",
                column: "FileHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SongTombstones");
        }
    }
}
