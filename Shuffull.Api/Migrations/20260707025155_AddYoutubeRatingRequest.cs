using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shuffull.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddYoutubeRatingRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "YoutubeRatingRequests",
                columns: table => new
                {
                    YoutubeRatingRequestId = table.Column<string>(type: "text", nullable: false),
                    SongId = table.Column<string>(type: "text", nullable: false),
                    VideoId = table.Column<string>(type: "text", nullable: false),
                    Rating = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YoutubeRatingRequests", x => x.YoutubeRatingRequestId);
                    table.ForeignKey(
                        name: "FK_YoutubeRatingRequests_Songs_SongId",
                        column: x => x.SongId,
                        principalTable: "Songs",
                        principalColumn: "SongId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_YoutubeRatingRequests_SongId",
                table: "YoutubeRatingRequests",
                column: "SongId");

            migrationBuilder.CreateIndex(
                name: "IX_YoutubeRatingRequests_Status",
                table: "YoutubeRatingRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_YoutubeRatingRequests_VideoId",
                table: "YoutubeRatingRequests",
                column: "VideoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "YoutubeRatingRequests");
        }
    }
}
