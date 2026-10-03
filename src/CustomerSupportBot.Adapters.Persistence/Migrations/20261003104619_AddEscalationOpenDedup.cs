using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CustomerSupportBot.Adapters.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEscalationOpenDedup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_escalations_session_open",
                schema: "hitl",
                table: "escalations");

            // Unique index, kısıt yokken oluşmuş mükerrer açık kayıtlar varsa oluşturulamaz.
            // Aynı session+ajan için EN ESKİ açık kayıt korunur, sonrakiler kapatılır (silinmez —
            // denetim izi kalır).
            migrationBuilder.Sql("""
                UPDATE hitl.escalations AS e
                SET status = 'Dismissed',
                    resolved_at = COALESCE(e.resolved_at, now()),
                    resolution = COALESCE(e.resolution, 'Otomatik kapatıldı: aynı oturum ve ajan için daha eski açık eskalasyon mevcut.')
                WHERE e.status IN ('Open', 'Acknowledged')
                  AND EXISTS (
                      SELECT 1 FROM hitl.escalations AS o
                      WHERE o.session_id = e.session_id
                        AND o.agent_name = e.agent_name
                        AND o.status IN ('Open', 'Acknowledged')
                        AND (o.created_at < e.created_at OR (o.created_at = e.created_at AND o.id < e.id)));
                """);

            migrationBuilder.CreateIndex(
                name: "ux_escalations_open_session_agent",
                schema: "hitl",
                table: "escalations",
                columns: new[] { "session_id", "agent_name" },
                unique: true,
                filter: "status IN ('Open', 'Acknowledged')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_escalations_open_session_agent",
                schema: "hitl",
                table: "escalations");

            migrationBuilder.CreateIndex(
                name: "ix_escalations_session_open",
                schema: "hitl",
                table: "escalations",
                column: "session_id",
                filter: "status IN ('Open', 'Acknowledged')");
        }
    }
}
