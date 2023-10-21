using Microsoft.EntityFrameworkCore.Migrations;

namespace Shuffull.Shared.Migrations
{
    public partial class LocalSessionData : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LocalSessionData",
                columns: table => new
                {
                    UserId = table.Column<long>(nullable: false),
                    CurrentPlaylistId = table.Column<long>(nullable: false),
                    ActivelyDownload = table.Column<bool>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocalSessionData", x => x.UserId);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LocalSessionData");
        }
    }
}
