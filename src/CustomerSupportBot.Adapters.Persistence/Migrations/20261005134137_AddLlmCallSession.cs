using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CustomerSupportBot.Adapters.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLlmCallSession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "session_id",
                schema: "observability",
                table: "llm_call_usage",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_llm_call_usage_session",
                schema: "observability",
                table: "llm_call_usage",
                column: "session_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_llm_call_usage_session",
                schema: "observability",
                table: "llm_call_usage");

            migrationBuilder.DropColumn(
                name: "session_id",
                schema: "observability",
                table: "llm_call_usage");
        }
    }
}
