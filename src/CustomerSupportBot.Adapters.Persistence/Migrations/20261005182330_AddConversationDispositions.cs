using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CustomerSupportBot.Adapters.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationDispositions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "conversation_dispositions",
                schema: "chat",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    session_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    reason_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    tags = table.Column<List<string>>(type: "text[]", nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    closed_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    closed_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversation_dispositions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_conversation_dispositions_session",
                schema: "chat",
                table: "conversation_dispositions",
                column: "session_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "conversation_dispositions",
                schema: "chat");
        }
    }
}
