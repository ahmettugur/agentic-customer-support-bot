using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CustomerSupportBot.Adapters.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVoiceCalls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "voice");

            migrationBuilder.CreateTable(
                name: "calls",
                schema: "voice",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    session_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    agent_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    agent_display_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    answered_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    consent_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    ended_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    end_reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    last_chunk_at = table.Column<DateTime>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_calls", x => x.id);
                    table.ForeignKey(
                        name: "fk_calls_session",
                        column: x => x.session_id,
                        principalSchema: "chat",
                        principalTable: "sessions",
                        principalColumn: "session_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recording_chunks",
                schema: "voice",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    call_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    session_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    track = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    offset_ms = table.Column<int>(type: "integer", nullable: false),
                    duration_ms = table.Column<int>(type: "integer", nullable: false),
                    content_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    size_bytes = table.Column<int>(type: "integer", nullable: false),
                    data = table.Column<byte[]>(type: "bytea", nullable: false),
                    transcript_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    transcript_text = table.Column<string>(type: "text", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    claimed_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    audio_purged_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recording_chunks", x => x.id);
                    table.ForeignKey(
                        name: "fk_chunks_call",
                        column: x => x.call_id,
                        principalSchema: "voice",
                        principalTable: "calls",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_calls_session",
                schema: "voice",
                table: "calls",
                columns: new[] { "session_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_calls_agent_open",
                schema: "voice",
                table: "calls",
                column: "agent_id",
                unique: true,
                filter: "status IN ('Ringing','Active')");

            migrationBuilder.CreateIndex(
                name: "ux_calls_session_open",
                schema: "voice",
                table: "calls",
                column: "session_id",
                unique: true,
                filter: "status IN ('Ringing','Active')");

            migrationBuilder.CreateIndex(
                name: "ix_chunks_created",
                schema: "voice",
                table: "recording_chunks",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_chunks_transcript_queue",
                schema: "voice",
                table: "recording_chunks",
                columns: new[] { "transcript_status", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "ux_chunks_call_track_seq",
                schema: "voice",
                table: "recording_chunks",
                columns: new[] { "call_id", "track", "sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recording_chunks",
                schema: "voice");

            migrationBuilder.DropTable(
                name: "calls",
                schema: "voice");
        }
    }
}
