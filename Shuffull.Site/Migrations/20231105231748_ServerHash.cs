using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shuffull.Site.Migrations
{
    /// <inheritdoc />
    public partial class ServerHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "PasswordHash",
                table: "Users",
                newName: "ServerHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ServerHash",
                table: "Users",
                newName: "PasswordHash");
        }
    }
}
