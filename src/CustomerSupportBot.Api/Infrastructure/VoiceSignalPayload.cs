// Infrastructure/VoiceSignalPayload.cs
// Köprüdeki sesli görüşme sinyalinin (ham JSON) SSE'ye yazılabilir hâli. Bozuk yük olay akışını
// düşürmesin diye atlanır (yükler servis tarafından üretilir ya da doğrulanır; bu bir emniyet ağıdır).

using System.Text.Json;

namespace CustomerSupportBot.Api.Infrastructure;

internal static class VoiceSignalPayload
{
    public static JsonElement? TryParse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
