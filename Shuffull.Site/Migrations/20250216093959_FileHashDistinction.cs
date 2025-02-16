using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shuffull.Site.Migrations
{
    /// <inheritdoc />
    public partial class FileHashDistinction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Directory",
                table: "Songs",
                newName: "SongFileExtension");

            migrationBuilder.AddColumn<string>(
                name: "FileHash",
                table: "Songs",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FileHash",
                table: "Songs");

            migrationBuilder.RenameColumn(
                name: "SongFileExtension",
                table: "Songs",
                newName: "Directory");
        }
    }
}
