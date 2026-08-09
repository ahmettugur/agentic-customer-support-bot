// Endpoints/SessionEndpoints.cs
// Oturum yönetimi endpoint'leri — sidebar/debug kullanımı için.

using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Api.Endpoints;

public static class SessionEndpoints
{
    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /sessions/ — Tüm oturumları listele (sidebar için)
        app.MapGet("/sessions/", async (ISessionPort sessionPort, CancellationToken ct) =>
        {
            var sessions = await sessionPort.GetAllSessionsAsync(ct);
            return Results.Json(sessions);
        });

        // GET /sessions/{sessionId}/messages — Belirli oturumun mesajlarını getir
        app.MapGet("/sessions/{sessionId}/messages", async (string sessionId, ISessionPort sessionPort, CancellationToken ct) =>
        {
            var history = await sessionPort.GetHistoryAsync(sessionId, ct);
            var messages = history.Select(m => new
            {
                role = m.Role == ConversationRoles.User ? "user" : "bot",
                text = m.Text ?? ""
            });
            return Results.Json(messages);
        });

        // GET /sessions/{sessionId}/state — Oturum durumunu getir (debug/frontend için)
        app.MapGet("/sessions/{sessionId}/state", async (string sessionId, ISessionPort sessionPort, CancellationToken ct) =>
        {
            var session = await sessionPort.GetSessionAsync(sessionId, ct);
            if (session == null)
                return Results.NotFound();

            return Results.Json(new
            {
                session.SessionId,
                session.CreatedAt,
                session.LastActivity,
                session.State
            });
        });

        return app;
    }
}
