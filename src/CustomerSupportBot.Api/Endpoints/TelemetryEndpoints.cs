// Endpoints/TelemetryEndpoints.cs
// /admin/telemetry/cost — model bazlı toplam token + USD maliyet özetini döner.
// Admin panelinde dashboard kartı olarak gösterilebilir.

using CustomerSupportBot.Application.Ports.Inbound;

namespace CustomerSupportBot.Api.Endpoints;

public static class TelemetryEndpoints
{
    public static IEndpointRouteBuilder MapTelemetryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/telemetry").WithTags("Telemetry");

        group.MapGet("/cost", (ITelemetryPort telemetry) =>
        {
            var snapshot = telemetry.GetCostSnapshot();
            return Results.Ok(snapshot);
        });

        group.MapGet("/cost/models", (ITelemetryPort telemetry) =>
        {
            return Results.Ok(new { knownModels = telemetry.GetKnownModels() });
        });

        group.MapPost("/cost/reset", (ITelemetryPort telemetry) =>
        {
            telemetry.ResetCostSnapshot();
            return Results.Ok(new { reset = true });
        });

        return app;
    }
}
