using Microsoft.EntityFrameworkCore.Migrations;

namespace Shuffull.Shared.Migrations
{
    public partial class ProcessingMethodAndUser : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UserHash",
                table: "Requests",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Username",
                table: "Requests",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProcessingMethod",
                table: "Requests",
                nullable: false,
                defaultValue: 0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UserHash",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "Username",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "ProcessingMethod",
                table: "Requests");
        }
    }
}
