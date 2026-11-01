using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shuffull.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSongRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SongRequestId",
                table: "SongImports",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SongRequests",
                columns: table => new
                {
                    SongRequestId = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    ExternalSongId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ArtistsJson = table.Column<string>(type: "text", nullable: false),
                    ThumbnailUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    TargetPlaylistName = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SongId = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SongRequests", x => x.SongRequestId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Playlists_UserId_Name_SongRequests",
                table: "Playlists",
                columns: new[] { "UserId", "Name" },
                unique: true,
                filter: "\"Name\" LIKE 'Requests · week of %'");

            migrationBuilder.CreateIndex(
                name: "IX_SongRequests_Status_CreatedAt",
                table: "SongRequests",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SongRequests_UserId_CreatedAt",
                table: "SongRequests",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SongRequests_UserId_ExternalSongId_Open",
                table: "SongRequests",
                columns: new[] { "UserId", "ExternalSongId" },
                unique: true,
                filter: "\"Status\" IN (0, 1, 2, 3, 4)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SongRequests");

            migrationBuilder.DropIndex(
                name: "IX_Playlists_UserId_Name_SongRequests",
                table: "Playlists");

            migrationBuilder.DropColumn(
                name: "SongRequestId",
                table: "SongImports");
        }
    }
}
