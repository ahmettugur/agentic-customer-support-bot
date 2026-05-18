using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Migrations
{
    /// <inheritdoc />
    public partial class AddProductionPersistenceStores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "personalization");

            migrationBuilder.EnsureSchema(
                name: "improvement");

            migrationBuilder.EnsureSchema(
                name: "workflow");

            migrationBuilder.CreateTable(
                name: "customer_profiles",
                schema: "personalization",
                columns: table => new
                {
                    customer_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    preferred_language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    preferred_tone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    intent_frequency = table.Column<string>(type: "jsonb", nullable: false),
                    product_interests = table.Column<string>(type: "jsonb", nullable: false),
                    recent_ratings = table.Column<string>(type: "jsonb", nullable: false),
                    summary = table.Column<string>(type: "text", nullable: true),
                    admin_note = table.Column<string>(type: "text", nullable: true),
                    total_sessions = table.Column<int>(type: "integer", nullable: false),
                    total_turns = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    last_interaction_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    last_consolidated_at = table.Column<DateTime>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_profiles", x => x.customer_id);
                });

            migrationBuilder.CreateTable(
                name: "lessons",
                schema: "improvement",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    lesson_text = table.Column<string>(type: "text", nullable: false),
                    observation = table.Column<string>(type: "text", nullable: false),
                    suggested_agent = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    source_trace_ids = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    decided_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    decided_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    decision_reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    vector_memory_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lessons", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sla_events",
                schema: "analytics",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    timestamp = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    target_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    age_seconds = table.Column<int>(type: "integer", nullable: false),
                    action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sla_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_definitions",
                schema: "workflow",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    trigger_keywords = table.Column<string>(type: "jsonb", nullable: false),
                    input_patterns = table.Column<string>(type: "jsonb", nullable: false),
                    steps = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_definitions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_customer_profiles_last_interaction_at",
                schema: "personalization",
                table: "customer_profiles",
                column: "last_interaction_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_lessons_created_at",
                schema: "improvement",
                table: "lessons",
                column: "created_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_lessons_status",
                schema: "improvement",
                table: "lessons",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_sla_events_kind_target_severity",
                schema: "analytics",
                table: "sla_events",
                columns: new[] { "kind", "target_id", "severity" });

            migrationBuilder.CreateIndex(
                name: "ix_sla_events_timestamp",
                schema: "analytics",
                table: "sla_events",
                column: "timestamp",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_workflow_definitions_is_active",
                schema: "workflow",
                table: "workflow_definitions",
                column: "is_active");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_profiles",
                schema: "personalization");

            migrationBuilder.DropTable(
                name: "lessons",
                schema: "improvement");

            migrationBuilder.DropTable(
                name: "sla_events",
                schema: "analytics");

            migrationBuilder.DropTable(
                name: "workflow_definitions",
                schema: "workflow");
        }
    }
}
