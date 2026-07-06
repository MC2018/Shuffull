using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shuffull.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSongTagProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "CrestFactorDb",
                table: "Songs",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "LoudnessRangeLu",
                table: "Songs",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MeasuredBpm",
                table: "Songs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "OnsetsPerSecond",
                table: "Songs",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OriginalReleaseYear",
                table: "Songs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TagModel",
                table: "Songs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "CrestFactorDb",
                table: "SongImports",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "LoudnessRangeLu",
                table: "SongImports",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MeasuredBpm",
                table: "SongImports",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "OnsetsPerSecond",
                table: "SongImports",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OriginalReleaseYear",
                table: "SongImports",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TagModel",
                table: "SongImports",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CrestFactorDb",
                table: "Songs");

            migrationBuilder.DropColumn(
                name: "LoudnessRangeLu",
                table: "Songs");

            migrationBuilder.DropColumn(
                name: "MeasuredBpm",
                table: "Songs");

            migrationBuilder.DropColumn(
                name: "OnsetsPerSecond",
                table: "Songs");

            migrationBuilder.DropColumn(
                name: "OriginalReleaseYear",
                table: "Songs");

            migrationBuilder.DropColumn(
                name: "TagModel",
                table: "Songs");

            migrationBuilder.DropColumn(
                name: "CrestFactorDb",
                table: "SongImports");

            migrationBuilder.DropColumn(
                name: "LoudnessRangeLu",
                table: "SongImports");

            migrationBuilder.DropColumn(
                name: "MeasuredBpm",
                table: "SongImports");

            migrationBuilder.DropColumn(
                name: "OnsetsPerSecond",
                table: "SongImports");

            migrationBuilder.DropColumn(
                name: "OriginalReleaseYear",
                table: "SongImports");

            migrationBuilder.DropColumn(
                name: "TagModel",
                table: "SongImports");
        }
    }
}
