using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shuffull.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUserServerHashSalt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ServerHashSalt",
                table: "Users",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ServerHashSalt",
                table: "Users");
        }
    }
}
