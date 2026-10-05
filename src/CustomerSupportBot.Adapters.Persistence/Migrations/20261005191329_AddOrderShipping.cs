using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CustomerSupportBot.Adapters.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderShipping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "carrier",
                schema: "catalog",
                table: "orders",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "shipped_at",
                schema: "catalog",
                table: "orders",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tracking_number",
                schema: "catalog",
                table: "orders",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "carrier",
                schema: "catalog",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "shipped_at",
                schema: "catalog",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "tracking_number",
                schema: "catalog",
                table: "orders");
        }
    }
}
