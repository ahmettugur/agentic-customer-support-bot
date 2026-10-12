// Endpoints/PerformanceEndpoints.cs
// GET /analytics/performance?from=&to=  [Admin] — yapay zekâ ajanları, araçlar ve temsilcilerin performansı.
// Tarihler ISO-8601 (saat dilimiyle); verilmezse son 7 gün. Aralık hatası 400. Yetki ve hız sınırı çağıran grupta
// (Program.cs: adminScope).

using CustomerSupportBot.Application.Ports.Inbound;

namespace CustomerSupportBot.Api.Endpoints;

public static class PerformanceEndpoints
{
    public static IEndpointRouteBuilder MapPerformanceEndpoints(this IEndpointRouteBuilder app)
    {
        // DateTimeOffset: DateTime bağlaması "…Z" değerini sunucunun yerel saatine çevirirdi.
        app.MapGet("/analytics/performance", async (DateTimeOffset? from, DateTimeOffset? to, IPerformancePort port, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await port.GetAsync(from?.UtcDateTime, to?.UtcDateTime, ct));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        return app;
    }
}
