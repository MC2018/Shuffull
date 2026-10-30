using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shuffull.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddHookStartPercent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "HookStartPercent",
                table: "Songs",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "HookStartPercent",
                table: "SongImports",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HookStartPercent",
                table: "Songs");

            migrationBuilder.DropColumn(
                name: "HookStartPercent",
                table: "SongImports");
        }
    }
}
