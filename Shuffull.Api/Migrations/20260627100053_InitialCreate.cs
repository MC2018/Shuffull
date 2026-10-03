using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shuffull.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Artists",
                columns: table => new
                {
                    ArtistId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Artists", x => x.ArtistId);
                });

            migrationBuilder.CreateTable(
                name: "Songs",
                columns: table => new
                {
                    SongId = table.Column<string>(type: "text", nullable: false),
                    FileExtension = table.Column<string>(type: "text", nullable: false),
                    FileHash = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    ExternalSongId = table.Column<string>(type: "text", nullable: true),
                    SyncedLyrics = table.Column<string>(type: "text", nullable: true),
                    PlainLyrics = table.Column<string>(type: "text", nullable: true),
                    LyricsInstrumental = table.Column<bool>(type: "boolean", nullable: false),
                    LyricsSource = table.Column<string>(type: "text", nullable: true),
                    Bpm = table.Column<int>(type: "integer", nullable: true),
                    Energy = table.Column<int>(type: "integer", nullable: true),
                    Version = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Songs", x => x.SongId);
                });

            migrationBuilder.CreateTable(
                name: "Tags",
                columns: table => new
                {
                    TagId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.TagId);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Username = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ServerHash = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "SongArtists",
                columns: table => new
                {
                    SongArtistId = table.Column<string>(type: "text", nullable: false),
                    SongId = table.Column<string>(type: "text", nullable: false),
                    ArtistId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SongArtists", x => x.SongArtistId);
                    table.ForeignKey(
                        name: "FK_SongArtists_Artists_ArtistId",
                        column: x => x.ArtistId,
                        principalTable: "Artists",
                        principalColumn: "ArtistId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SongArtists_Songs_SongId",
                        column: x => x.SongId,
                        principalTable: "Songs",
                        principalColumn: "SongId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SongImports",
                columns: table => new
                {
                    SongImportId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    ImportFolder = table.Column<string>(type: "text", nullable: false),
                    FileType = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    ExternalSource = table.Column<int>(type: "integer", nullable: false),
                    PlaylistId = table.Column<string>(type: "text", nullable: true),
                    ExternalSongId = table.Column<string>(type: "text", nullable: true),
                    ExternalPlaylistId = table.Column<string>(type: "text", nullable: true),
                    SongId = table.Column<string>(type: "text", nullable: true),
                    ReplacesSongId = table.Column<string>(type: "text", nullable: true),
                    GeneratedTagsJson = table.Column<string>(type: "text", nullable: true),
                    LyricsJson = table.Column<string>(type: "text", nullable: true),
                    Bpm = table.Column<int>(type: "integer", nullable: true),
                    MarkAsLiked = table.Column<bool>(type: "boolean", nullable: false),
                    TargetPlaylistName = table.Column<string>(type: "text", nullable: true),
                    LastUpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "SongReplacements",
                columns: table => new
                {
                    SongReplacementId = table.Column<string>(type: "text", nullable: false),
                    SongId = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true),
                    OriginalExternalSongId = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SongReplacements", x => x.SongReplacementId);
                    table.ForeignKey(
                        name: "FK_SongReplacements_Songs_SongId",
                        column: x => x.SongId,
                        principalTable: "Songs",
                        principalColumn: "SongId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GenreRelations",
                columns: table => new
                {
                    GenreRelationId = table.Column<string>(type: "text", nullable: false),
                    MainGenreId = table.Column<string>(type: "text", nullable: false),
                    SubGenreId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GenreRelations", x => x.GenreRelationId);
                    table.ForeignKey(
                        name: "FK_GenreRelations_Tags_MainGenreId",
                        column: x => x.MainGenreId,
                        principalTable: "Tags",
                        principalColumn: "TagId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GenreRelations_Tags_SubGenreId",
                        column: x => x.SubGenreId,
                        principalTable: "Tags",
                        principalColumn: "TagId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SongTags",
                columns: table => new
                {
                    SongTagId = table.Column<string>(type: "text", nullable: false),
                    SongId = table.Column<string>(type: "text", nullable: false),
                    TagId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SongTags", x => x.SongTagId);
                    table.ForeignKey(
                        name: "FK_SongTags_Songs_SongId",
                        column: x => x.SongId,
                        principalTable: "Songs",
                        principalColumn: "SongId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SongTags_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "TagId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Playlists",
                columns: table => new
                {
                    PlaylistId = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    CurrentSongId = table.Column<string>(type: "text", nullable: true),
                    PercentUntilReplayable = table.Column<decimal>(type: "numeric(2,2)", nullable: false),
                    Version = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Playlists", x => x.PlaylistId);
                    table.ForeignKey(
                        name: "FK_Playlists_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserSongs",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    SongId = table.Column<string>(type: "text", nullable: false),
                    LastPlayed = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LikeStatus = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSongs", x => new { x.UserId, x.SongId });
                    table.ForeignKey(
                        name: "FK_UserSongs_Songs_SongId",
                        column: x => x.SongId,
                        principalTable: "Songs",
                        principalColumn: "SongId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserSongs_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlaylistSongs",
                columns: table => new
                {
                    PlaylistSongId = table.Column<string>(type: "text", nullable: false),
                    PlaylistId = table.Column<string>(type: "text", nullable: false),
                    SongId = table.Column<string>(type: "text", nullable: false)
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

            migrationBuilder.CreateIndex(
                name: "IX_Artists_Name",
                table: "Artists",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_GenreRelations_MainGenreId",
                table: "GenreRelations",
                column: "MainGenreId");

            migrationBuilder.CreateIndex(
                name: "IX_GenreRelations_SubGenreId",
                table: "GenreRelations",
                column: "SubGenreId");

            migrationBuilder.CreateIndex(
                name: "IX_Playlists_PlaylistId",
                table: "Playlists",
                column: "PlaylistId");

            migrationBuilder.CreateIndex(
                name: "IX_Playlists_UserId",
                table: "Playlists",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistSongs_PlaylistId",
                table: "PlaylistSongs",
                column: "PlaylistId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistSongs_SongId",
                table: "PlaylistSongs",
                column: "SongId");

            migrationBuilder.CreateIndex(
                name: "IX_SongArtists_ArtistId",
                table: "SongArtists",
                column: "ArtistId");

            migrationBuilder.CreateIndex(
                name: "IX_SongArtists_SongId",
                table: "SongArtists",
                column: "SongId");

            migrationBuilder.CreateIndex(
                name: "IX_SongImports_SongId",
                table: "SongImports",
                column: "SongId");

            migrationBuilder.CreateIndex(
                name: "IX_SongReplacements_SongId",
                table: "SongReplacements",
                column: "SongId");

            migrationBuilder.CreateIndex(
                name: "IX_SongReplacements_Status",
                table: "SongReplacements",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Songs_Name",
                table: "Songs",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Songs_Version",
                table: "Songs",
                column: "Version");

            migrationBuilder.CreateIndex(
                name: "IX_SongTags_SongId",
                table: "SongTags",
                column: "SongId");

            migrationBuilder.CreateIndex(
                name: "IX_SongTags_TagId",
                table: "SongTags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_Tags_Type",
                table: "Tags",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Version",
                table: "Users",
                column: "Version");

            migrationBuilder.CreateIndex(
                name: "IX_UserSongs_LastPlayed",
                table: "UserSongs",
                column: "LastPlayed");

            migrationBuilder.CreateIndex(
                name: "IX_UserSongs_SongId",
                table: "UserSongs",
                column: "SongId");

            migrationBuilder.CreateIndex(
                name: "IX_UserSongs_UserId",
                table: "UserSongs",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GenreRelations");

            migrationBuilder.DropTable(
                name: "PlaylistSongs");

            migrationBuilder.DropTable(
                name: "SongArtists");

            migrationBuilder.DropTable(
                name: "SongImports");

            migrationBuilder.DropTable(
                name: "SongReplacements");

            migrationBuilder.DropTable(
                name: "SongTags");

            migrationBuilder.DropTable(
                name: "UserSongs");

            migrationBuilder.DropTable(
                name: "Playlists");

            migrationBuilder.DropTable(
                name: "Artists");

            migrationBuilder.DropTable(
                name: "Tags");

            migrationBuilder.DropTable(
                name: "Songs");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
