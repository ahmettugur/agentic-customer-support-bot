// Endpoints/SessionEndpoints.cs
// Oturum yönetimi endpoint'leri — sidebar/debug kullanımı için.

using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Api.Endpoints;

public static class SessionEndpoints
{
    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /sessions/ — Tüm oturumları listele (sidebar için)
        app.MapGet("/sessions/", (ISessionManager sessionManager) =>
        {
            var sessions = sessionManager.GetAllSessions();
            return Results.Json(sessions);
        });

        // GET /sessions/{sessionId}/messages — Belirli oturumun mesajlarını getir
        app.MapGet("/sessions/{sessionId}/messages", (string sessionId, ISessionManager sessionManager) =>
        {
            var history = sessionManager.GetHistory(sessionId);
            var messages = history.Select(m => new
            {
                role = m.Role == ChatRole.User ? "user" : "bot",
                text = m.Text ?? ""
            });
            return Results.Json(messages);
        });

        // GET /sessions/{sessionId}/state — Oturum durumunu getir (debug/frontend için)
        app.MapGet("/sessions/{sessionId}/state", (string sessionId, ISessionManager sessionManager) =>
        {
            var session = sessionManager.GetSession(sessionId);
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
