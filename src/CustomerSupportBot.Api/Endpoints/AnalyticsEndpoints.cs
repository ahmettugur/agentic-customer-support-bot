// Endpoints/AnalyticsEndpoints.cs
// Analytics dashboard + Conversation rating endpoint'leri:
//   - GET  /analytics/dashboard       : Tüm istatistikler (admin dashboard için)
//   - POST /sessions/{sid}/rating     : Konuşma değerlendirmesi gönder
//   - GET  /sessions/{sid}/rating     : Konuşmanın mevcut rating'ini getir
//   - GET  /analytics/ratings/recent  : Son N rating

using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Api.Endpoints;

public static class AnalyticsEndpoints
{
    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        // ─── ANALYTICS DASHBOARD (admin-only) ───
        // "general" burada MapGroup("").RequireAuthorization("Admin").RequireRateLimiting("general")
        // (adminScope, Program.cs) İLE DEĞİL — bu uçlar o gruba dahil değil, doğrudan app'e map
        // ediliyor. Aksi hâlde admin scope'undaki diğer uçların aksine bu üçü limitsiz kalırdı.
        app.MapGet("/analytics/dashboard", async (IAnalyticsPort analytics, CancellationToken ct) =>
            Results.Json(await analytics.GetDashboardAsync(ct)))
            .RequireAuthorization("Admin")
            .RequireRateLimiting("general");

        // GET /analytics/session/{sid} — Tek bir oturum için detaylı analytics
        app.MapGet("/analytics/session/{sid}", async (string sid, IAnalyticsPort analytics, CancellationToken ct) =>
        {
            var result = await analytics.GetSessionAnalyticsAsync(sid, ct);
            return result == null
                ? Results.NotFound(new { error = "Session bulunamadı." })
                : Results.Json(result);
        })
            .RequireAuthorization("Admin")
            .RequireRateLimiting("general");

        // ─── CONVERSATION RATING (public — kullanıcı oturum açmadan rating bırakır) ───
        app.MapPost("/sessions/{sid}/rating",
            async (string sid, RatingInput body, IAnalyticsPort analytics, CancellationToken ct) =>
            {
                // Ucuz girdi doğrulaması oturum aramasından (DB) ÖNCE — kimliksiz bir uçta
                // geçersiz istek veritabanına hiç dokunmamalı.
                if (body.Stars < 1 || body.Stars > 5)
                    return Results.BadRequest(new { error = "Yıldız puanı 1–5 arasında olmalıdır." });

                if (body.Feedback is { Length: > ConversationRating.MaxFeedbackLength })
                    return Results.BadRequest(new
                    {
                        error = $"Yorum en fazla {ConversationRating.MaxFeedbackLength} karakter olabilir."
                    });

                if (await analytics.GetSessionAnalyticsAsync(sid, ct) == null)
                    return Results.NotFound(new { error = "Session bulunamadı." });

                var rating = await analytics.RateAsync(sid, body.Stars, body.Feedback);
                return Results.Json(rating);
            })
            .RequireRateLimiting("general");

        app.MapGet("/sessions/{sid}/rating",
            (string sid, IAnalyticsPort analytics) =>
            {
                var rating = analytics.GetRating(sid);
                return rating == null
                    ? Results.NotFound(new { error = "Bu session için rating bulunamadı." })
                    : Results.Json(rating);
            })
            .RequireRateLimiting("general");

        app.MapGet("/analytics/ratings/recent",
            (IAnalyticsPort analytics, int count = 20) =>
                Results.Json(analytics.GetRecentRatings(count)))
            .RequireAuthorization("Admin")
            .RequireRateLimiting("general");

        return app;
    }
}
