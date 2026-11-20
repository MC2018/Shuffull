using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shuffull.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSongDuplicateReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SongDuplicateReports",
                columns: table => new
                {
                    ReportId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReporterUserId = table.Column<string>(type: "text", nullable: false),
                    SongId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OtherSongId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    PreferredSongId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    HiddenSongId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PairKey = table.Column<string>(type: "character varying(129)", maxLength: 129, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResolvedByUserId = table.Column<string>(type: "text", nullable: true),
                    Version = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SongDuplicateReports", x => x.ReportId);
                    table.ForeignKey(
                        name: "FK_SongDuplicateReports_Users_ReporterUserId",
                        column: x => x.ReporterUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SongDuplicateVerdicts",
                columns: table => new
                {
                    PairKey = table.Column<string>(type: "character varying(129)", maxLength: 129, nullable: false),
                    Verdict = table.Column<int>(type: "integer", nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DecidedByUserId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SongDuplicateVerdicts", x => x.PairKey);
                });

            migrationBuilder.CreateTable(
                name: "SongDuplicateReportAliases",
                columns: table => new
                {
                    AliasId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReportId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SongDuplicateReportAliases", x => x.AliasId);
                    table.ForeignKey(
                        name: "FK_SongDuplicateReportAliases_SongDuplicateReports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "SongDuplicateReports",
                        principalColumn: "ReportId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SongDuplicateReportAliases_ReportId",
                table: "SongDuplicateReportAliases",
                column: "ReportId");

            migrationBuilder.CreateIndex(
                name: "IX_SongDuplicateReports_ReporterUserId_PairKey",
                table: "SongDuplicateReports",
                columns: new[] { "ReporterUserId", "PairKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SongDuplicateReports_ReporterUserId_Version",
                table: "SongDuplicateReports",
                columns: new[] { "ReporterUserId", "Version" });

            migrationBuilder.CreateIndex(
                name: "IX_SongDuplicateReports_Status_PairKey",
                table: "SongDuplicateReports",
                columns: new[] { "Status", "PairKey" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SongDuplicateReportAliases");

            migrationBuilder.DropTable(
                name: "SongDuplicateVerdicts");

            migrationBuilder.DropTable(
                name: "SongDuplicateReports");
        }
    }
}
