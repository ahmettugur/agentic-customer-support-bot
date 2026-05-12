// Endpoints/TelemetryEndpoints.cs
// /admin/telemetry/cost — model bazlı toplam token + USD maliyet özetini döner.
// Admin panelinde dashboard kartı olarak gösterilebilir.

using CustomerSupportBot.Api.Services.Telemetry;

namespace CustomerSupportBot.Api.Endpoints;

public static class TelemetryEndpoints
{
    public static IEndpointRouteBuilder MapTelemetryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/telemetry").WithTags("Telemetry");

        group.MapGet("/cost", (CostUsageStore store) =>
        {
            var snapshot = store.GetSnapshot();
            return Results.Ok(snapshot);
        });

        group.MapGet("/cost/models", (ICostCalculator calc) =>
        {
            return Results.Ok(new { knownModels = calc.KnownModels });
        });

        group.MapPost("/cost/reset", (CostUsageStore store) =>
        {
            store.Reset();
            return Results.Ok(new { reset = true });
        });

        return app;
    }
}
