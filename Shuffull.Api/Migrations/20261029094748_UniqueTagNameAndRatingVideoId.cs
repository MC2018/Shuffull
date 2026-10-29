using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shuffull.Api.Migrations
{
    /// <inheritdoc />
    public partial class UniqueTagNameAndRatingVideoId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Merge the duplicates the unique indexes would refuse (Shuffull#43). On 2026-10-29 prod had four tag
            // names twice over, every song linked to a copy also linked to the original, and 19 videos with 2-4
            // rating rows each, created milliseconds apart by one like sent several times.

            // Tags: keep the oldest id per name (ULIDs sort by creation time), move every link onto it, then drop
            // the copies and any link that is now doubled.
            migrationBuilder.Sql("""
                CREATE TEMP TABLE "TagKeep" AS
                SELECT "TagId", "KeepId" FROM (
                    SELECT "TagId", min("TagId" COLLATE "C") OVER (PARTITION BY "Name") AS "KeepId" FROM "Tags"
                ) t WHERE "TagId" <> "KeepId";

                UPDATE "SongTags" s SET "TagId" = k."KeepId" FROM "TagKeep" k WHERE s."TagId" = k."TagId";
                DELETE FROM "SongTags" a USING "SongTags" b
                WHERE a."SongId" = b."SongId" AND a."TagId" = b."TagId" AND a."SongTagId" > b."SongTagId";

                UPDATE "GenreRelations" g SET "MainGenreId" = k."KeepId" FROM "TagKeep" k WHERE g."MainGenreId" = k."TagId";
                UPDATE "GenreRelations" g SET "SubGenreId" = k."KeepId" FROM "TagKeep" k WHERE g."SubGenreId" = k."TagId";
                DELETE FROM "GenreRelations" a USING "GenreRelations" b
                WHERE a."MainGenreId" = b."MainGenreId" AND a."SubGenreId" = b."SubGenreId"
                  AND a."GenreRelationId" > b."GenreRelationId";

                DELETE FROM "Tags" t USING "TagKeep" k WHERE t."TagId" = k."TagId";
                DROP TABLE "TagKeep";
                """);

            // Rating rows: keep one per video. A Pending row still has work to do, so it wins; otherwise the most
            // recently updated.
            migrationBuilder.Sql("""
                DELETE FROM "YoutubeRatingRequests" r USING (
                    SELECT "YoutubeRatingRequestId", row_number() OVER (
                        PARTITION BY "VideoId"
                        ORDER BY ("Status" = 0) DESC, "UpdatedAt" DESC, "CreatedAt" DESC, "YoutubeRatingRequestId" DESC
                    ) AS rn
                    FROM "YoutubeRatingRequests"
                ) d
                WHERE r."YoutubeRatingRequestId" = d."YoutubeRatingRequestId" AND d.rn > 1;
                """);

            migrationBuilder.DropIndex(
                name: "IX_YoutubeRatingRequests_VideoId",
                table: "YoutubeRatingRequests");

            migrationBuilder.CreateIndex(
                name: "IX_YoutubeRatingRequests_VideoId",
                table: "YoutubeRatingRequests",
                column: "VideoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tags_Name",
                table: "Tags",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_YoutubeRatingRequests_VideoId",
                table: "YoutubeRatingRequests");

            migrationBuilder.DropIndex(
                name: "IX_Tags_Name",
                table: "Tags");

            migrationBuilder.CreateIndex(
                name: "IX_YoutubeRatingRequests_VideoId",
                table: "YoutubeRatingRequests",
                column: "VideoId");
        }
    }
}
