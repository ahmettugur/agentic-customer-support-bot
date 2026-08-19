// Endpoints/SessionEndpoints.cs
// Oturum yönetimi endpoint'leri — sidebar/debug kullanımı için.
//
// Bu uçlar eskiden TAMAMEN ANONİMDİ. Chat tarafı müşteri login'ine taşınırken bunlar
// birlikte taşınmamıştı; sonuç olarak kimliği doğrulanmamış biri /sessions/ ile tüm oturum
// kimliklerini listeleyip ardından her birinin mesajlarını ve state'ini okuyabiliyordu —
// yani /chat/* üzerindeki yetkilendirme bu yoldan tamamen dolaşılabiliyordu.
//
// İki katman birden gerekir:
//   1. Kimlik  — RequireAuthorization(): anonim erişim yok.
//   2. Sahiplik — "bu kişi müşteri mi" ile "bu oturum onun mu" farklı sorulardır. İkincisi
//      olmadan müşteri B, A'nın oturum kimliğini vererek A'nın konuşmasını okuyabilirdi.
//      Liste ucunda karşılığı, sonucun çağıranın kendi oturumlarıyla sınırlanmasıdır.
//
// Admin/agent kapsam dışıdır (linked_customer_id claim'leri yoktur): panelin tüm oturumları
// görmesi gerekir, bu bilinçlidir.

using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Chat;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Api.Endpoints;

public static class SessionEndpoints
{
    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /sessions/ — Oturumları listele (sidebar için)
        app.MapGet("/sessions/", async (
            HttpContext http, ISessionPort sessionPort, CancellationToken ct) =>
        {
            // Müşteri ise yalnızca kendi oturumları; admin/agent ise hepsi.
            if (!TryResolveScope(http, out var scope)) return SessionForbidden();

            var sessions = await sessionPort.GetAllSessionsAsync(scope, ct);
            return Results.Json(sessions);
        }).RequireAuthorization("SessionAccess");

        // GET /sessions/{sessionId}/messages — Belirli oturumun mesajlarını getir
        app.MapGet("/sessions/{sessionId}/messages", async (
            string sessionId, HttpContext http,
            ISessionPort sessionPort, ISessionManager sessions, CancellationToken ct) =>
        {
            if (!await IsOwnSessionAsync(sessionId, http, sessions, ct)) return SessionForbidden();

            var history = await sessionPort.GetHistoryAsync(sessionId, ct);
            var messages = history.Select(m => new
            {
                role = m.Role == ConversationRoles.User ? "user" : "bot",
                text = m.Text ?? ""
            });
            return Results.Json(messages);
        }).RequireAuthorization("SessionAccess");

        // GET /sessions/{sessionId}/state — Oturum durumunu getir (debug/frontend için)
        app.MapGet("/sessions/{sessionId}/state", async (
            string sessionId, HttpContext http,
            ISessionPort sessionPort, ISessionManager sessions, CancellationToken ct) =>
        {
            if (!await IsOwnSessionAsync(sessionId, http, sessions, ct)) return SessionForbidden();

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
        }).RequireAuthorization("SessionAccess");

        return app;
    }

    /// <summary>
    /// Kapsam kararı: <c>null</c> = sınırsız (tüm oturumlar), aksi hâlde yalnızca o müşteri.
    /// <c>false</c> dönerse çağıranın bu uçlarda hiçbir kapsamı yoktur.
    ///
    /// <para>
    /// Sınırsız erişim <b>rolden</b> türetilir, claim'in YOKLUĞUNDAN değil. "linked_customer_id
    /// yoksa admin'dir" varsayımı sessiz bir yetki yükseltmesidir: o claim'i taşımayan tek grup
    /// admin/agent değildir — A2A partner token'ında da yoktur ve aynı JWT şemasıyla doğrulanır.
    /// Rol beyaz listesi bu ayrımı açıkça yapar.
    /// </para>
    /// </summary>
    private static bool TryResolveScope(HttpContext http, out string? customerScope)
    {
        customerScope = null;

        if (http.User.IsInRole("Admin") || http.User.IsInRole("Agent"))
            return true;

        if (http.User.IsInRole("Customer"))
        {
            var linked = http.User.FindFirst("linked_customer_id")?.Value;
            if (string.IsNullOrWhiteSpace(linked)) return false;
            customerScope = linked;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Oturum sahipliği — ChatEndpoints ile AYNI kuralı kullanır (<c>SessionIdentityBinder</c>),
    /// böylece iki uç grubu birbirinden ayrışıp farklı davranamaz.
    /// </summary>
    private static async Task<bool> IsOwnSessionAsync(
        string sessionId, HttpContext http, ISessionManager sessions, CancellationToken ct)
    {
        if (!TryResolveScope(http, out var scope)) return false;
        return await SessionIdentityBinder.IsAccessibleAsync(sessionId, scope, sessions, ct);
    }

    private static IResult SessionForbidden() =>
        Results.Json(new
        {
            error = "session_forbidden",
            message = "Bu oturuma erişim yetkiniz yok."
        }, statusCode: StatusCodes.Status403Forbidden);
}
