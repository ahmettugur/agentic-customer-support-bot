using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CustomerSupportBot.Adapters.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddApprovalParamSignatureDedup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "param_signature",
                schema: "hitl",
                table: "approval_requests",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_approvals_pending_dedup",
                schema: "hitl",
                table: "approval_requests",
                columns: new[] { "session_id", "tool_name", "param_signature" },
                unique: true,
                filter: "status = 'Pending' AND param_signature IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_approvals_pending_dedup",
                schema: "hitl",
                table: "approval_requests");

            migrationBuilder.DropColumn(
                name: "param_signature",
                schema: "hitl",
                table: "approval_requests");
        }
    }
}
