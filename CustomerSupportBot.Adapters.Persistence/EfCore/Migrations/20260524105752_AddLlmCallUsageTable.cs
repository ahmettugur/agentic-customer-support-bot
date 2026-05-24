using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Migrations
{
    /// <inheritdoc />
    public partial class AddLlmCallUsageTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "llm_call_usage",
                schema: "observability");
        }
    }
}
