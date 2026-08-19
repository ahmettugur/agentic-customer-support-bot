using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CustomerSupportBot.Adapters.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddApprovalExecutionStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "execution_status",
                schema: "hitl",
                table: "approval_requests",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.CreateIndex(
                name: "ix_approvals_status_execution_status",
                schema: "hitl",
                table: "approval_requests",
                columns: new[] { "status", "execution_status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_approvals_status_execution_status",
                schema: "hitl",
                table: "approval_requests");

            migrationBuilder.DropColumn(
                name: "execution_status",
                schema: "hitl",
                table: "approval_requests");
        }
    }
}
