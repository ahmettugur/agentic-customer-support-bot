using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CustomerSupportBot.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "hitl");

            migrationBuilder.EnsureSchema(
                name: "chat");

            migrationBuilder.EnsureSchema(
                name: "analytics");

            migrationBuilder.EnsureSchema(
                name: "observability");

            migrationBuilder.CreateTable(
                name: "approval_requests",
                schema: "hitl",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    session_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    trace_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    tool_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    agent_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    parameters = table.Column<string>(type: "jsonb", nullable: false),
                    user_query = table.Column<string>(type: "text", nullable: true),
                    justification = table.Column<string>(type: "text", nullable: true),
                    requested_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    decided_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    decided_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    decision_reason = table.Column<string>(type: "text", nullable: true),
                    timeout_seconds = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_approval_requests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bridge_messages",
                schema: "chat",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    message_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    session_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    sender = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    human_agent = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    text = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bridge_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "escalations",
                schema: "hitl",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    session_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    trace_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    agent_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    user_query = table.Column<string>(type: "text", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    missing_context = table.Column<string>(type: "jsonb", nullable: false),
                    response_summary = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    acknowledged_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    resolved_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    assigned_to = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    resolution = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ratings",
                schema: "analytics",
                columns: table => new
                {
                    session_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    stars = table.Column<int>(type: "integer", nullable: false),
                    feedback = table.Column<string>(type: "text", nullable: true),
                    rated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ratings", x => x.session_id);
                    table.CheckConstraint("ck_ratings_stars_range", "stars BETWEEN 1 AND 5");
                });

            migrationBuilder.CreateTable(
                name: "reasoning_traces",
                schema: "observability",
                columns: table => new
                {
                    trace_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    session_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    user_query = table.Column<string>(type: "text", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    termination_reason = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    final_response = table.Column<string>(type: "text", nullable: true),
                    iteration_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    error = table.Column<string>(type: "text", nullable: true),
                    estimated_tokens = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    first_draft_response = table.Column<string>(type: "text", nullable: true),
                    was_revised = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    reasoning = table.Column<string>(type: "jsonb", nullable: true),
                    planning = table.Column<string>(type: "jsonb", nullable: true),
                    specialist_reasonings = table.Column<string>(type: "jsonb", nullable: false),
                    final_critique = table.Column<string>(type: "jsonb", nullable: true),
                    agent_visits = table.Column<string>(type: "jsonb", nullable: false),
                    tool_calls = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reasoning_traces", x => x.trace_id);
                });

            migrationBuilder.CreateTable(
                name: "session_modes",
                schema: "chat",
                columns: table => new
                {
                    session_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    human_agent = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    entered_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    last_activity_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    message_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_session_modes", x => x.session_id);
                });

            migrationBuilder.CreateTable(
                name: "sessions",
                schema: "chat",
                columns: table => new
                {
                    session_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    last_activity = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    state = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sessions", x => x.session_id);
                });

            migrationBuilder.CreateTable(
                name: "messages",
                schema: "chat",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    session_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_messages_session",
                        column: x => x.session_id,
                        principalSchema: "chat",
                        principalTable: "sessions",
                        principalColumn: "session_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_approvals_session_id",
                schema: "hitl",
                table: "approval_requests",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_approvals_status_requested_at",
                schema: "hitl",
                table: "approval_requests",
                columns: new[] { "status", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "ix_bridge_messages_message_id",
                schema: "chat",
                table: "bridge_messages",
                column: "message_id");

            migrationBuilder.CreateIndex(
                name: "ix_bridge_messages_session",
                schema: "chat",
                table: "bridge_messages",
                columns: new[] { "session_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_escalations_session_open",
                schema: "hitl",
                table: "escalations",
                column: "session_id",
                filter: "status IN ('Open', 'Acknowledged')");

            migrationBuilder.CreateIndex(
                name: "ix_escalations_status_created_at",
                schema: "hitl",
                table: "escalations",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_messages_session",
                schema: "chat",
                table: "messages",
                columns: new[] { "session_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_ratings_rated_at",
                schema: "analytics",
                table: "ratings",
                column: "rated_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_traces_session_started",
                schema: "observability",
                table: "reasoning_traces",
                columns: new[] { "session_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_traces_started",
                schema: "observability",
                table: "reasoning_traces",
                column: "started_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_session_modes_active",
                schema: "chat",
                table: "session_modes",
                column: "mode",
                filter: "mode = 'Human'");

            migrationBuilder.CreateIndex(
                name: "ix_sessions_last_activity",
                schema: "chat",
                table: "sessions",
                column: "last_activity",
                descending: new bool[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "approval_requests",
                schema: "hitl");

            migrationBuilder.DropTable(
                name: "bridge_messages",
                schema: "chat");

            migrationBuilder.DropTable(
                name: "escalations",
                schema: "hitl");

            migrationBuilder.DropTable(
                name: "messages",
                schema: "chat");

            migrationBuilder.DropTable(
                name: "ratings",
                schema: "analytics");

            migrationBuilder.DropTable(
                name: "reasoning_traces",
                schema: "observability");

            migrationBuilder.DropTable(
                name: "session_modes",
                schema: "chat");

            migrationBuilder.DropTable(
                name: "sessions",
                schema: "chat");
        }
    }
}
