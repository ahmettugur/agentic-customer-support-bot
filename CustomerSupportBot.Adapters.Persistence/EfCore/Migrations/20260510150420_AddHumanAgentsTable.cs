using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Migrations
{
    /// <inheritdoc />
    public partial class AddHumanAgentsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.CreateIndex(
                name: "ix_human_agents_active",
                schema: "hitl",
                table: "human_agents",
                column: "is_active",
                filter: "is_active = true");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "human_agents",
                schema: "hitl");
        }
    }
}
