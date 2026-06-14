using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shuffull.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddGenres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GenreRelations",
                columns: table => new
                {
                    GenreRelationId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    MainGenreId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    SubGenreId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GenreRelations", x => x.GenreRelationId);
                    table.ForeignKey(
                        name: "FK_GenreRelations_Tags_MainGenreId",
                        column: x => x.MainGenreId,
                        principalTable: "Tags",
                        principalColumn: "TagId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GenreRelations_Tags_SubGenreId",
                        column: x => x.SubGenreId,
                        principalTable: "Tags",
                        principalColumn: "TagId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tags_Type",
                table: "Tags",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_GenreRelations_MainGenreId",
                table: "GenreRelations",
                column: "MainGenreId");

            migrationBuilder.CreateIndex(
                name: "IX_GenreRelations_SubGenreId",
                table: "GenreRelations",
                column: "SubGenreId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GenreRelations");

            migrationBuilder.DropIndex(
                name: "IX_Tags_Type",
                table: "Tags");
        }
    }
}
