// Endpoints/PromptEndpoints.cs
// Yüklü prompt'ların içerik sürümleri:
//   - GET /prompts : birleşik set sürümü + prompt başına sürüm (prompt METNİ dönmez)
//
// "Bu yanıt hangi prompt setiyle üretildi?" — trace'teki promptVersion bu sürümle eşleşir.
// Admin kapsamında, rate limit arkasında (Program.cs).

using CustomerSupportBot.Application.Ports.Outbound;

namespace CustomerSupportBot.Api.Endpoints;

public static class PromptEndpoints
{
    public static IEndpointRouteBuilder MapPromptEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/prompts", (IPromptRepository prompts) => Results.Json(new
        {
            setVersion = prompts.SetVersion,
            prompts = prompts.Versions
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => new { key = kv.Key, version = kv.Value })
        }));

        return app;
    }
}
