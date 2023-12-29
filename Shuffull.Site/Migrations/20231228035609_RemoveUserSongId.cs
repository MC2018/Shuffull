using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shuffull.Site.Migrations
{
    /// <inheritdoc />
    public partial class RemoveUserSongId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_UserSongs",
                table: "UserSongs");

            migrationBuilder.DropColumn(
                name: "UserSongId",
                table: "UserSongs");

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserSongs",
                table: "UserSongs",
                columns: new[] { "UserId", "SongId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_UserSongs",
                table: "UserSongs");

            migrationBuilder.AddColumn<long>(
                name: "UserSongId",
                table: "UserSongs",
                type: "bigint",
                nullable: false,
                defaultValue: 0L)
                .Annotation("SqlServer:Identity", "1, 1");

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserSongs",
                table: "UserSongs",
                column: "UserSongId");
        }
    }
}
