using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CustomerSupportBot.Adapters.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowCheckpoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "workflow_turns",
                schema: "observability",
                columns: table => new
                {
                    trace_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    session_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    query = table.Column<string>(type: "text", nullable: false),
                    customer_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    reasoning = table.Column<string>(type: "jsonb", nullable: true),
                    history_count = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_turns", x => x.trace_id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_checkpoints",
                schema: "observability",
                columns: table => new
                {
                    trace_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    checkpoint_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    parent_checkpoint_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    payload = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_checkpoints", x => new { x.trace_id, x.checkpoint_id });
                    table.ForeignKey(
                        name: "FK_workflow_checkpoints_workflow_turns_trace_id",
                        column: x => x.trace_id,
                        principalSchema: "observability",
                        principalTable: "workflow_turns",
                        principalColumn: "trace_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_workflow_checkpoints_trace_created",
                schema: "observability",
                table: "workflow_checkpoints",
                columns: new[] { "trace_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_workflow_turns_started_at",
                schema: "observability",
                table: "workflow_turns",
                column: "started_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "workflow_checkpoints",
                schema: "observability");

            migrationBuilder.DropTable(
                name: "workflow_turns",
                schema: "observability");
        }
    }
}
