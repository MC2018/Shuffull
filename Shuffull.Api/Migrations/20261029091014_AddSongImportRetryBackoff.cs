using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shuffull.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSongImportRetryBackoff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FailedTagAttempts",
                table: "SongImports",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextAttemptAt",
                table: "SongImports",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FailedTagAttempts",
                table: "SongImports");

            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                table: "SongImports");
        }
    }
}
