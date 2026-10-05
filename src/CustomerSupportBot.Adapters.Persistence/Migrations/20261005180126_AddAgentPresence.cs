using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CustomerSupportBot.Adapters.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentPresence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "last_seen_at",
                schema: "hitl",
                table: "human_agents",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "presence",
                schema: "hitl",
                table: "human_agents",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Offline");

            migrationBuilder.AddColumn<DateTime>(
                name: "presence_changed_at",
                schema: "hitl",
                table: "human_agents",
                type: "timestamptz",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_seen_at",
                schema: "hitl",
                table: "human_agents");

            migrationBuilder.DropColumn(
                name: "presence",
                schema: "hitl",
                table: "human_agents");

            migrationBuilder.DropColumn(
                name: "presence_changed_at",
                schema: "hitl",
                table: "human_agents");
        }
    }
}
