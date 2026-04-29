// Endpoints/TraceEndpoints.cs
// Reasoning trace'leri gözlemlemek için endpoint'ler.
// OpenTelemetry olmadığı için bu store + endpoint kombinasyonu basit
// Bir dashboard kaynağı işlevi görür.

using CustomerSupportBot.Services;

namespace CustomerSupportBot.Endpoints;

public static class TraceEndpoints
{
    public static IEndpointRouteBuilder MapTraceEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /traces/recent?count=20 — Son N trace'i listeler
        app.MapGet("/traces/recent", (IReasoningTraceStore store, int count = 20) =>
        {
            var traces = store.GetRecent(count);
            return Results.Json(traces);
        });

        // GET /traces/{traceId} — Tek bir trace'in tam detayı
        app.MapGet("/traces/{traceId}", (string traceId, IReasoningTraceStore store) =>
        {
            var trace = store.Get(traceId);
            return trace == null ? Results.NotFound() : Results.Json(trace);
        });

        // GET /traces/by-session/{sessionId} — Bir oturuma ait tüm trace'ler
        app.MapGet("/traces/by-session/{sessionId}",
            (string sessionId, IReasoningTraceStore store) =>
        {
            var traces = store.GetBySession(sessionId);
            return Results.Json(traces);
        });

        // GET /traces/sessions — Session bazlı trace özeti (dashboard session sidebar için)
        // Her session'ın trace sayısı, son trace zamanı ve session başlığını döner.
        app.MapGet("/traces/sessions", (IReasoningTraceStore store, ISessionManager sessionManager) =>
        {
            var allTraces = store.GetRecent(500);
            var grouped = allTraces
                .GroupBy(t => t.SessionId)
                .Select(g =>
                {
                    var sessionId = g.Key;
                    var traces = g.OrderByDescending(t => t.StartedAt).ToList();
                    var firstQuery = traces.LastOrDefault()?.UserQuery;
                    var title = firstQuery != null
                        ? (firstQuery.Length > 60 ? firstQuery[..60] + "…" : firstQuery)
                        : "Yeni Oturum";
                    var messageCount = sessionManager.GetHistory(sessionId).Count;

                    return new
                    {
                        sessionId,
                        title,
                        traceCount = traces.Count,
                        lastTraceAt = traces.First().StartedAt,
                        lastQuery = traces.First().UserQuery,
                        messageCount
                    };
                })
                .OrderByDescending(s => s.lastTraceAt)
                .ToList();

            return Results.Json(grouped);
        });

        // GET /traces/stats — Aggregate istatistikler (dashboard için baseline)
        app.MapGet("/traces/stats", (IReasoningTraceStore store) =>
        {
            var all = store.GetRecent(500);
            if (all.Count == 0)
            {
                return Results.Json(new
                {
                    totalTraces = 0,
                    avgDurationMs = 0.0,
                    avgIterationCount = 0.0,
                    terminationReasons = new Dictionary<string, int>()
                });
            }

            var completed = all.Where(t => t.CompletedAt.HasValue).ToList();
            var terminationCounts = completed
                .Where(t => !string.IsNullOrEmpty(t.TerminationReason))
                .GroupBy(t => t.TerminationReason!)
                .ToDictionary(g => g.Key, g => g.Count());

            return Results.Json(new
            {
                totalTraces = all.Count,
                completedCount = completed.Count,
                errorCount = all.Count(t => !string.IsNullOrEmpty(t.Error)),
                avgDurationMs = completed.Any()
                    ? completed.Where(t => t.DurationMs.HasValue).Average(t => t.DurationMs!.Value)
                    : 0.0,
                avgIterationCount = completed.Any()
                    ? completed.Average(t => (double)t.IterationCount)
                    : 0.0,
                terminationReasons = terminationCounts
            });
        });

        return app;
    }
}
