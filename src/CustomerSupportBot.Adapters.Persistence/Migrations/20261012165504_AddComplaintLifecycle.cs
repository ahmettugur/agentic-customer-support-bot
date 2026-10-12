using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CustomerSupportBot.Adapters.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddComplaintLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "approval_id",
                schema: "catalog",
                table: "complaints",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "assigned_agent_id",
                schema: "catalog",
                table: "complaints",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "created_at",
                schema: "catalog",
                table: "complaints",
                type: "timestamptz",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<string>(
                name: "resolution",
                schema: "catalog",
                table: "complaints",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at",
                schema: "catalog",
                table: "complaints",
                type: "timestamptz",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<int>(
                name: "version",
                schema: "catalog",
                table: "complaints",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "complaint_events",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    complaint_code = table.Column<long>(type: "bigint", nullable: false),
                    at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    actor = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    from_status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    to_status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    assigned_agent_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_complaint_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_complaint_events_complaints_complaint_code",
                        column: x => x.complaint_code,
                        principalSchema: "catalog",
                        principalTable: "complaints",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_complaints_status_created_at",
                schema: "catalog",
                table: "complaints",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_complaint_events_code_at",
                schema: "catalog",
                table: "complaint_events",
                columns: new[] { "complaint_code", "at" });

            // Mevcut kayıtlar: iki sütun da migration anını aldı; aynı değer olsun diye eşitle.
            migrationBuilder.Sql("UPDATE catalog.complaints SET updated_at = created_at;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "complaint_events",
                schema: "catalog");

            migrationBuilder.DropIndex(
                name: "ix_complaints_status_created_at",
                schema: "catalog",
                table: "complaints");

            migrationBuilder.DropColumn(
                name: "approval_id",
                schema: "catalog",
                table: "complaints");

            migrationBuilder.DropColumn(
                name: "assigned_agent_id",
                schema: "catalog",
                table: "complaints");

            migrationBuilder.DropColumn(
                name: "created_at",
                schema: "catalog",
                table: "complaints");

            migrationBuilder.DropColumn(
                name: "resolution",
                schema: "catalog",
                table: "complaints");

            migrationBuilder.DropColumn(
                name: "updated_at",
                schema: "catalog",
                table: "complaints");

            migrationBuilder.DropColumn(
                name: "version",
                schema: "catalog",
                table: "complaints");
        }
    }
}
