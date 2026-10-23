using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shuffull.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSongTagBackoff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "NextTagAttemptAt",
                table: "Songs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TagAttempts",
                table: "Songs",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NextTagAttemptAt",
                table: "Songs");

            migrationBuilder.DropColumn(
                name: "TagAttempts",
                table: "Songs");
        }
    }
}
