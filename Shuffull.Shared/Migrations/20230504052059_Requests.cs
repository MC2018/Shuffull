using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Shuffull.Shared.Migrations
{
    public partial class Requests : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Requests",
                columns: table => new
                {
                    Guid = table.Column<string>(nullable: false),
                    TimeRequested = table.Column<DateTime>(nullable: false),
                    RequestType = table.Column<int>(nullable: false),
                    RequestName = table.Column<string>(nullable: false),
                    SongId = table.Column<long>(nullable: true),
                    LastPlayed = table.Column<DateTime>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Requests", x => x.Guid);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Requests");
        }
    }
}
