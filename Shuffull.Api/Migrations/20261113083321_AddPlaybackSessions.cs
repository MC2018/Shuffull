using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shuffull.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPlaybackSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlaybackSessions",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    DeviceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SongId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PositionSeconds = table.Column<double>(type: "double precision", nullable: false),
                    PositionAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsPlaying = table.Column<bool>(type: "boolean", nullable: false),
                    Scope = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true),
                    History = table.Column<List<string>>(type: "text[]", nullable: false),
                    QueuedNext = table.Column<List<string>>(type: "text[]", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TakenOverBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TakenOverAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Command = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    CommandId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CommandSeekSeconds = table.Column<double>(type: "double precision", nullable: true),
                    CommandAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaybackSessions", x => new { x.UserId, x.DeviceId });
                    table.ForeignKey(
                        name: "FK_PlaybackSessions_UserDevices_UserId_DeviceId",
                        columns: x => new { x.UserId, x.DeviceId },
                        principalTable: "UserDevices",
                        principalColumns: new[] { "UserId", "DeviceId" },
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlaybackSessions");
        }
    }
}
