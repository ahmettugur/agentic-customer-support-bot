// Endpoints/TraceEndpoints.cs
// Reasoning trace'leri gözlemlemek için endpoint'ler.

using CustomerSupportBot.Application.Ports.Inbound;

namespace CustomerSupportBot.Api.Endpoints;

public static class TraceEndpoints
{
    public static IEndpointRouteBuilder MapTraceEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /traces/recent?count=20 — Son N trace'i listeler
        app.MapGet("/traces/recent", (ITracePort tracePort, int count = 20) =>
            Results.Json(tracePort.GetRecentTraces(count)));

        // GET /traces/{traceId} — Tek bir trace'in tam detayı
        app.MapGet("/traces/{traceId}", (string traceId, ITracePort tracePort) =>
        {
            var trace = tracePort.GetTrace(traceId);
            return trace == null ? Results.NotFound() : Results.Json(trace);
        });

        // GET /traces/by-session/{sessionId} — Bir oturuma ait tüm trace'ler
        app.MapGet("/traces/by-session/{sessionId}", (string sessionId, ITracePort tracePort) =>
            Results.Json(tracePort.GetTracesBySession(sessionId)));

        // GET /traces/sessions — Session bazlı trace özeti (dashboard session sidebar için)
        app.MapGet("/traces/sessions", (ITracePort tracePort) =>
            Results.Json(tracePort.GetSessionsSummary()));

        // GET /traces/stats — Aggregate istatistikler
        app.MapGet("/traces/stats", (ITracePort tracePort) =>
            Results.Json(tracePort.GetStats()));

        return app;
    }
}
