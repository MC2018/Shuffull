using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shuffull.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSongMerges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SongMerges",
                columns: table => new
                {
                    LoserSongId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SurvivorSongId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LoserExternalSongId = table.Column<string>(type: "text", nullable: true),
                    LoserFileHash = table.Column<string>(type: "text", nullable: false),
                    MergedByUserId = table.Column<string>(type: "text", nullable: false),
                    MergedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SongMerges", x => x.LoserSongId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SongMerges_LoserExternalSongId",
                table: "SongMerges",
                column: "LoserExternalSongId");

            migrationBuilder.CreateIndex(
                name: "IX_SongMerges_LoserFileHash",
                table: "SongMerges",
                column: "LoserFileHash");

            migrationBuilder.CreateIndex(
                name: "IX_SongMerges_SurvivorSongId",
                table: "SongMerges",
                column: "SurvivorSongId");

            migrationBuilder.CreateIndex(
                name: "IX_SongMerges_Version_LoserSongId",
                table: "SongMerges",
                columns: new[] { "Version", "LoserSongId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SongMerges");
        }
    }
}
