using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CustomerSupportBot.Adapters.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "hitl");

            migrationBuilder.EnsureSchema(
                name: "chat");

            migrationBuilder.EnsureSchema(
                name: "catalog");

            migrationBuilder.EnsureSchema(
                name: "personalization");

            migrationBuilder.EnsureSchema(
                name: "improvement");

            migrationBuilder.EnsureSchema(
                name: "observability");

            migrationBuilder.EnsureSchema(
                name: "analytics");

            migrationBuilder.EnsureSchema(
                name: "auth");

            migrationBuilder.EnsureSchema(
                name: "workflow");

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
                name: "categories",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "complaints",
                schema: "catalog",
                columns: table => new
                {
                    code = table.Column<long>(type: "bigint", nullable: false),
                    order_id = table.Column<long>(type: "bigint", nullable: false),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    complaint = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_complaints", x => x.code);
                });

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
                name: "human_agents",
                schema: "hitl",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    display_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    skills = table.Column<string>(type: "jsonb", nullable: false),
                    languages = table.Column<string>(type: "jsonb", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    max_concurrent_load = table.Column<int>(type: "integer", nullable: false),
                    current_load = table.Column<int>(type: "integer", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    last_assigned_at = table.Column<DateTime>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_human_agents", x => x.id);
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
                name: "llm_call_usage",
                schema: "observability",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    input_tokens = table.Column<long>(type: "bigint", nullable: false),
                    output_tokens = table.Column<long>(type: "bigint", nullable: false),
                    cost_usd = table.Column<decimal>(type: "numeric(12,8)", nullable: false),
                    duration_ms = table.Column<double>(type: "double precision", nullable: false),
                    called_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_llm_call_usage", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "orders",
                schema: "catalog",
                columns: table => new
                {
                    code = table.Column<long>(type: "bigint", nullable: false),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    order_date = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    cancelled_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    return_requested_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    return_reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_orders", x => x.code);
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
                name: "users",
                schema: "auth",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    linked_agent_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    last_login_at = table.Column<DateTime>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
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

            migrationBuilder.CreateTable(
                name: "products",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    price = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    stock = table.Column<int>(type: "integer", nullable: false),
                    category_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_products", x => x.id);
                    table.ForeignKey(
                        name: "FK_products_categories_category_id",
                        column: x => x.category_id,
                        principalSchema: "catalog",
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
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

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                schema: "auth",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    user_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    revoked_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    replaced_by_token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_refresh_tokens_user",
                        column: x => x.user_id,
                        principalSchema: "auth",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "order_details",
                schema: "catalog",
                columns: table => new
                {
                    order_code = table.Column<long>(type: "bigint", nullable: false),
                    product_id = table.Column<int>(type: "integer", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_details", x => new { x.order_code, x.product_id });
                    table.ForeignKey(
                        name: "FK_order_details_orders_order_code",
                        column: x => x.order_code,
                        principalSchema: "catalog",
                        principalTable: "orders",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_order_details_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "catalog",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
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
                name: "ix_complaints_customer_id",
                schema: "catalog",
                table: "complaints",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_complaints_order_id",
                schema: "catalog",
                table: "complaints",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_customer_profiles_last_interaction_at",
                schema: "personalization",
                table: "customer_profiles",
                column: "last_interaction_at",
                descending: new bool[0]);

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
                name: "ix_human_agents_active",
                schema: "hitl",
                table: "human_agents",
                column: "is_active",
                filter: "is_active = true");

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
                name: "ix_llm_call_usage_called_at",
                schema: "observability",
                table: "llm_call_usage",
                column: "called_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_llm_call_usage_model",
                schema: "observability",
                table: "llm_call_usage",
                column: "model");

            migrationBuilder.CreateIndex(
                name: "ix_messages_session",
                schema: "chat",
                table: "messages",
                columns: new[] { "session_id", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_order_details_product_id",
                schema: "catalog",
                table: "order_details",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_orders_customer_id",
                schema: "catalog",
                table: "orders",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "IX_products_category_id",
                schema: "catalog",
                table: "products",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_products_name",
                schema: "catalog",
                table: "products",
                column: "name",
                unique: true);

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
                name: "ix_refresh_tokens_user",
                schema: "auth",
                table: "refresh_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_refresh_tokens_hash",
                schema: "auth",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

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
                name: "ux_users_username",
                schema: "auth",
                table: "users",
                column: "username",
                unique: true);

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
                name: "approval_requests",
                schema: "hitl");

            migrationBuilder.DropTable(
                name: "bridge_messages",
                schema: "chat");

            migrationBuilder.DropTable(
                name: "complaints",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "customer_profiles",
                schema: "personalization");

            migrationBuilder.DropTable(
                name: "escalations",
                schema: "hitl");

            migrationBuilder.DropTable(
                name: "human_agents",
                schema: "hitl");

            migrationBuilder.DropTable(
                name: "lessons",
                schema: "improvement");

            migrationBuilder.DropTable(
                name: "llm_call_usage",
                schema: "observability");

            migrationBuilder.DropTable(
                name: "messages",
                schema: "chat");

            migrationBuilder.DropTable(
                name: "order_details",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "ratings",
                schema: "analytics");

            migrationBuilder.DropTable(
                name: "reasoning_traces",
                schema: "observability");

            migrationBuilder.DropTable(
                name: "refresh_tokens",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "session_modes",
                schema: "chat");

            migrationBuilder.DropTable(
                name: "sla_events",
                schema: "analytics");

            migrationBuilder.DropTable(
                name: "workflow_definitions",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "sessions",
                schema: "chat");

            migrationBuilder.DropTable(
                name: "orders",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "products",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "users",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "categories",
                schema: "catalog");
        }
    }
}
