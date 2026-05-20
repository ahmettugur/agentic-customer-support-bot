// Endpoints/SlaEndpoints.cs
// SLA Guardian admin endpoint'leri:
//   GET /sla/events    — son N warn/breach olayı
//   GET /sla/status    — güncel pending/open kuyruk + max yaş + ihlal sayısı

using CustomerSupportBot.Application.Ports.Driving;

namespace CustomerSupportBot.Api.Endpoints;

public static class SlaEndpoints
{
    public static IEndpointRouteBuilder MapSlaEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/sla/events", (ISlaPort slaPort, int? count) =>
        {
            var events = slaPort.GetRecentEvents(count ?? 100);
            return Results.Ok(new { count = events.Count, items = events });
        });

        app.MapGet("/sla/status", (ISlaPort slaPort) =>
            Results.Ok(slaPort.GetStatus()));

        return app;
    }
}
