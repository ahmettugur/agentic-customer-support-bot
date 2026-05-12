using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CustomerSupportBot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLinkedAgentIdToUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "linked_agent_id",
                schema: "auth",
                table: "users",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "linked_agent_id",
                schema: "auth",
                table: "users");
        }
    }
}
