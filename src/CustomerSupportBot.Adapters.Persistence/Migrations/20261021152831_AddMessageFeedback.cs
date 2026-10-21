using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CustomerSupportBot.Adapters.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "message_feedback",
                schema: "analytics",
                columns: table => new
                {
                    session_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    message_hash = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    helpful = table.Column<bool>(type: "boolean", nullable: false),
                    reason = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    comment = table.Column<string>(type: "text", nullable: true),
                    trace_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    customer_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_message_feedback", x => new { x.session_id, x.message_hash });
                });

            migrationBuilder.CreateIndex(
                name: "ix_message_feedback_trace_id",
                schema: "analytics",
                table: "message_feedback",
                column: "trace_id");

            migrationBuilder.CreateIndex(
                name: "ix_message_feedback_updated_at",
                schema: "analytics",
                table: "message_feedback",
                column: "updated_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "message_feedback",
                schema: "analytics");
        }
    }
}
