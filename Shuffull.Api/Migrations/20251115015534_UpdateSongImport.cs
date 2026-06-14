using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shuffull.Api.Migrations
{
    /// <inheritdoc />
    public partial class UpdateSongImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SongUploads");

            migrationBuilder.CreateTable(
                name: "SongImports",
                columns: table => new
                {
                    SongImportId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ImportFolder = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FileType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    PlaylistId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExternalSongId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExternalPlaylistId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SongId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    LastUpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SongImports", x => x.SongImportId);
                    table.ForeignKey(
                        name: "FK_SongImports_Songs_SongId",
                        column: x => x.SongId,
                        principalTable: "Songs",
                        principalColumn: "SongId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SongImports_SongId",
                table: "SongImports",
                column: "SongId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SongImports");

            migrationBuilder.CreateTable(
                name: "SongUploads",
                columns: table => new
                {
                    SongUploadId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    FileType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LastUpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PlaylistId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    State = table.Column<int>(type: "int", nullable: false),
                    UploadFolder = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SongUploads", x => x.SongUploadId);
                });
        }
    }
}
