// Endpoints/SseWriter.cs
// Server-Sent Events (SSE) formatında event yazmak için yardımcı sınıf.
// "event: TYPE\ndata: JSON\n\n" formatını üretir ve response body'yi flush eder.

using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace CustomerSupportBot.Api.Endpoints;

internal static class SseWriter
{
    private static readonly JsonSerializerOptions SseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// SSE header'larını response'a yazar. Proxy/nginx buffering'i kapatır.
    /// </summary>
    public static void WriteHeaders(HttpResponse response)
    {
        response.Headers["Content-Type"] = "text/event-stream";
        response.Headers["Cache-Control"] = "no-cache, no-transform";
        response.Headers["X-Accel-Buffering"] = "no";
        response.Headers["Connection"] = "keep-alive";
    }

    /// <summary>
    /// SSE formatında tek bir event yazar ve flush eder.
    /// </summary>
    public static async Task WriteEventAsync(
        HttpResponse response,
        string eventType,
        object? data,
        CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return;

        var json = data != null
            ? JsonSerializer.Serialize(data, SseJsonOptions)
            : "{}";

        var sb = new StringBuilder();
        sb.Append("event: ").Append(eventType).Append('\n');
        sb.Append("data: ").Append(json).Append("\n\n");

        await response.WriteAsync(sb.ToString(), ct);
        await response.Body.FlushAsync(ct);
    }

    /// <summary>
    /// Anonymous object'ten "text" property'sini reflection ile okur.
    /// StreamEvent.Data anonim tip olduğu için JSON round-trip yerine direkt erişim.
    /// </summary>
    public static string GetTextFromAnon(object data)
    {
        var prop = data.GetType().GetProperty("text");
        return prop?.GetValue(data) as string ?? "";
    }
}
