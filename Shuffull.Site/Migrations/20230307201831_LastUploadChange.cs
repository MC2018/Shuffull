using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shuffull.Site.Migrations
{
    /// <inheritdoc />
    public partial class LastUploadChange : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BucketNumber",
                table: "PlaylistSongs");

            migrationBuilder.AddColumn<bool>(
                name: "InQueue",
                table: "PlaylistSongs",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastPlayed",
                table: "PlaylistSongs",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "LastUpdated",
                table: "Playlists",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InQueue",
                table: "PlaylistSongs");

            migrationBuilder.DropColumn(
                name: "LastPlayed",
                table: "PlaylistSongs");

            migrationBuilder.DropColumn(
                name: "LastUpdated",
                table: "Playlists");

            migrationBuilder.AddColumn<long>(
                name: "BucketNumber",
                table: "PlaylistSongs",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }
    }
}
