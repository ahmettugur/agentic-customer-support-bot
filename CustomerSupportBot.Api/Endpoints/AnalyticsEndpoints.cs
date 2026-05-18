// Endpoints/AnalyticsEndpoints.cs
// Analytics dashboard + Conversation rating endpoint'leri:
//   - GET  /analytics/dashboard       : Tüm istatistikler (admin dashboard için)
//   - POST /sessions/{sid}/rating     : Konuşma değerlendirmesi gönder
//   - GET  /sessions/{sid}/rating     : Konuşmanın mevcut rating'ini getir
//   - GET  /analytics/ratings/recent  : Son N rating

using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Api.Services;

namespace CustomerSupportBot.Api.Endpoints;

public static class AnalyticsEndpoints
{
    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        // ─── ANALYTICS DASHBOARD (admin-only) ───
        app.MapGet("/analytics/dashboard", (AnalyticsService analytics) =>
            Results.Json(analytics.GetDashboard()))
            .RequireAuthorization("Admin");

        // GET /analytics/session/{sid} — Tek bir oturum için detaylı analytics
        app.MapGet("/analytics/session/{sid}", (string sid, AnalyticsService analytics) =>
        {
            var result = analytics.GetSessionAnalytics(sid);
            return result == null
                ? Results.NotFound(new { error = "Session bulunamadı." })
                : Results.Json(result);
        })
            .RequireAuthorization("Admin");

        // ─── CONVERSATION RATING (public — kullanıcı oturum açmadan rating bırakır) ───
        app.MapPost("/sessions/{sid}/rating",
            (string sid, RatingInput body, IRatingStore ratings, ISessionManager sessions) =>
            {
                // Oturum var mı kontrol et
                var session = sessions.GetSession(sid);
                if (session == null)
                    return Results.NotFound(new { error = "Session bulunamadı." });

                // Stars validasyonu
                if (body.Stars < 1 || body.Stars > 5)
                    return Results.BadRequest(new { error = "Yıldız puanı 1–5 arasında olmalıdır." });

                var rating = ratings.Submit(sid, body.Stars, body.Feedback);
                return Results.Json(rating);
            });

        app.MapGet("/sessions/{sid}/rating",
            (string sid, IRatingStore ratings) =>
            {
                var rating = ratings.GetBySession(sid);
                return rating == null
                    ? Results.NotFound(new { error = "Bu session için rating bulunamadı." })
                    : Results.Json(rating);
            });

        app.MapGet("/analytics/ratings/recent",
            (IRatingStore ratings, int count = 20) =>
                Results.Json(ratings.GetRecent(count)))
            .RequireAuthorization("Admin");

        return app;
    }
}

