// Adapters.Telemetry/Langfuse/LangfuseDataEraser.cs
// KVKK: oturum ya da müşteri silinince Langfuse'taki trace'leri ve oturum puanı skorunu da siler.
//
// Langfuse v4'te trace'ler oturuma/kullanıcıya göre doğrudan aranamaz (v1 uçları kapalı). Yol:
// gözlemler (v2) sessionId/userId ile süzülür → traceId'ler toplanır → trace'ler toplu silinir.
// LLM span'leri bu kimlikleri taşır; trace silinince aynı trace'in diğer span'leri (HTTP kök vb.) de gider.
//
// Hata davranışı: Langfuse erişilemezse İSTİSNA fırlatır. DataPrivacyService bunu "depo hatası" sayar ve
// oturumu yerinde bırakır — silme talebi güvenle tekrarlanır; kişisel veri "silindi" diye raporlanmaz.

using System.Net;
using System.Text;
using System.Text.Json;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Adapters.Telemetry.Langfuse;

public sealed class LangfuseDataEraser : ISessionDataEraser, ICustomerDataEraser
{
    private const int PageSize = 1000;
    private const int DeleteBatchSize = 100;
    private const int MaxPages = 100;

    private readonly HttpClient _http;
    private readonly LangfuseOptions _options;
    private readonly ILogger<LangfuseDataEraser> _logger;

    public LangfuseDataEraser(HttpClient http, LangfuseOptions options, ILogger<LangfuseDataEraser> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
    }

    public string Name => "langfuse";

    public async Task<int> EraseSessionsAsync(IReadOnlyCollection<string> sessionIds, CancellationToken ct = default)
    {
        var traceIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sessionId in sessionIds)
            await CollectTraceIdsAsync("sessionId", sessionId, traceIds, ct);

        var erased = await DeleteTracesAsync(traceIds, ct);

        // Oturum puanı skoru trace'e değil oturuma bağlıdır; trace silinse de kalır. Olmayan skoru silmek hata değildir.
        foreach (var sessionId in sessionIds)
            await DeleteScoreAsync(LangfuseScoreSink.ScoreId(sessionId), ct);

        return erased;
    }

    public async Task<int> EraseCustomerAsync(string customerId, CancellationToken ct = default)
    {
        var traceIds = new HashSet<string>(StringComparer.Ordinal);
        await CollectTraceIdsAsync("userId", customerId, traceIds, ct);
        return await DeleteTracesAsync(traceIds, ct);
    }

    private async Task CollectTraceIdsAsync(string column, string value, HashSet<string> traceIds, CancellationToken ct)
    {
        var filter = JsonSerializer.Serialize(new[] { new { type = "string", column, @operator = "=", value } });
        string? cursor = null;

        for (var page = 0; page < MaxPages; page++)
        {
            var query = $"/api/public/v2/observations?limit={PageSize}&fields=core&filter={Uri.EscapeDataString(filter)}"
                + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            using var request = LangfuseRequests.Create(HttpMethod.Get, _options, query);
            using var response = await _http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (json.RootElement.TryGetProperty("data", out var data))
                foreach (var observation in data.EnumerateArray())
                    if (observation.TryGetProperty("traceId", out var id) && id.GetString() is { Length: > 0 } traceId)
                        traceIds.Add(traceId);

            cursor = json.RootElement.TryGetProperty("meta", out var meta) && meta.TryGetProperty("cursor", out var c)
                ? c.GetString()
                : null;
            if (string.IsNullOrEmpty(cursor)) return;
        }

        // Sayfa sınırı aşıldıysa kalanlar silinmeden kalırdı; sessizce "tamam" demek yerine hata.
        throw new InvalidOperationException($"Langfuse gözlem listesi {MaxPages} sayfayı aştı ({column}={value}).");
    }

    private async Task<int> DeleteTracesAsync(HashSet<string> traceIds, CancellationToken ct)
    {
        foreach (var batch in traceIds.Chunk(DeleteBatchSize))
        {
            using var content = new StringContent(JsonSerializer.Serialize(new { traceIds = batch }), Encoding.UTF8, "application/json");
            using var request = LangfuseRequests.Create(HttpMethod.Delete, _options, "/api/public/traces", content);
            using var response = await _http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
        }

        if (traceIds.Count > 0)
            _logger.LogInformation("[Privacy] Langfuse'tan {Count} trace silindi", traceIds.Count);
        return traceIds.Count;
    }

    private async Task DeleteScoreAsync(string scoreId, CancellationToken ct)
    {
        using var request = LangfuseRequests.Create(HttpMethod.Delete, _options, $"/api/public/scores/{Uri.EscapeDataString(scoreId)}");
        using var response = await _http.SendAsync(request, ct);
        if (response.StatusCode != HttpStatusCode.NotFound)
            response.EnsureSuccessStatusCode();
    }
}
