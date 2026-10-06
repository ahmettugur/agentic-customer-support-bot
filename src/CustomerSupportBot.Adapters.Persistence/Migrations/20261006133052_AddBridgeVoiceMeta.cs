using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CustomerSupportBot.Adapters.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBridgeVoiceMeta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "offset_ms",
                schema: "chat",
                table: "bridge_messages",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "voice_call_id",
                schema: "chat",
                table: "bridge_messages",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "voice_track",
                schema: "chat",
                table: "bridge_messages",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "offset_ms",
                schema: "chat",
                table: "bridge_messages");

            migrationBuilder.DropColumn(
                name: "voice_call_id",
                schema: "chat",
                table: "bridge_messages");

            migrationBuilder.DropColumn(
                name: "voice_track",
                schema: "chat",
                table: "bridge_messages");
        }
    }
}
