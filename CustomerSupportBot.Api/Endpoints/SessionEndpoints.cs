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
        app.MapGet("/sessions/", (ISessionPort sessionPort) =>
        {
            var sessions = sessionPort.GetAllSessions();
            return Results.Json(sessions);
        });

        // GET /sessions/{sessionId}/messages — Belirli oturumun mesajlarını getir
        app.MapGet("/sessions/{sessionId}/messages", (string sessionId, ISessionPort sessionPort) =>
        {
            var history = sessionPort.GetHistory(sessionId);
            var messages = history.Select(m => new
            {
                role = m.Role == ConversationRoles.User ? "user" : "bot",
                text = m.Text ?? ""
            });
            return Results.Json(messages);
        });

        // GET /sessions/{sessionId}/state — Oturum durumunu getir (debug/frontend için)
        app.MapGet("/sessions/{sessionId}/state", (string sessionId, ISessionPort sessionPort) =>
        {
            var session = sessionPort.GetSession(sessionId);
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
