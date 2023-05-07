using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Shuffull.Shared.Migrations
{
    public partial class InitRecentlyPlayedSong : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Playlists",
                columns: table => new
                {
                    PlaylistId = table.Column<long>(nullable: false),
                    UserId = table.Column<long>(nullable: false),
                    Name = table.Column<string>(nullable: false),
                    CurrentSongId = table.Column<long>(nullable: false),
                    PercentUntilReplayable = table.Column<decimal>(nullable: false),
                    VersionCounter = table.Column<long>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Playlists", x => x.PlaylistId);
                });

            migrationBuilder.CreateTable(
                name: "Requests",
                columns: table => new
                {
                    Guid = table.Column<string>(nullable: false),
                    TimeRequested = table.Column<DateTime>(nullable: false),
                    RequestType = table.Column<int>(nullable: false),
                    RequestName = table.Column<string>(nullable: false),
                    SongId = table.Column<long>(nullable: true),
                    LastPlayed = table.Column<DateTime>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Requests", x => x.Guid);
                });

            migrationBuilder.CreateTable(
                name: "Songs",
                columns: table => new
                {
                    SongId = table.Column<long>(nullable: false),
                    Directory = table.Column<string>(nullable: false),
                    Name = table.Column<string>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Songs", x => x.SongId);
                });

            migrationBuilder.CreateTable(
                name: "PlaylistSongs",
                columns: table => new
                {
                    PlaylistSongId = table.Column<long>(nullable: false),
                    PlaylistId = table.Column<long>(nullable: false),
                    SongId = table.Column<long>(nullable: false),
                    LastAddedToQueue = table.Column<DateTime>(nullable: false),
                    LastPlayed = table.Column<DateTime>(nullable: false),
                    InQueue = table.Column<bool>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaylistSongs", x => x.PlaylistSongId);
                    table.ForeignKey(
                        name: "FK_PlaylistSongs_Playlists_PlaylistId",
                        column: x => x.PlaylistId,
                        principalTable: "Playlists",
                        principalColumn: "PlaylistId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlaylistSongs_Songs_SongId",
                        column: x => x.SongId,
                        principalTable: "Songs",
                        principalColumn: "SongId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecentlyPlayedSongs",
                columns: table => new
                {
                    RecentlyPlayedSongGuid = table.Column<string>(nullable: false),
                    SongId = table.Column<long>(nullable: false),
                    TimestampSeconds = table.Column<int>(nullable: true),
                    LastPlayed = table.Column<DateTime>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecentlyPlayedSongs", x => x.RecentlyPlayedSongGuid);
                    table.ForeignKey(
                        name: "FK_RecentlyPlayedSongs_Songs_SongId",
                        column: x => x.SongId,
                        principalTable: "Songs",
                        principalColumn: "SongId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistSongs_PlaylistId",
                table: "PlaylistSongs",
                column: "PlaylistId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistSongs_SongId",
                table: "PlaylistSongs",
                column: "SongId");

            migrationBuilder.CreateIndex(
                name: "IX_RecentlyPlayedSongs_SongId",
                table: "RecentlyPlayedSongs",
                column: "SongId");

            migrationBuilder.CreateIndex(
                name: "IX_RecentlyPlayedSongs_TimestampSeconds",
                table: "RecentlyPlayedSongs",
                column: "TimestampSeconds");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlaylistSongs");

            migrationBuilder.DropTable(
                name: "RecentlyPlayedSongs");

            migrationBuilder.DropTable(
                name: "Requests");

            migrationBuilder.DropTable(
                name: "Playlists");

            migrationBuilder.DropTable(
                name: "Songs");
        }
    }
}
