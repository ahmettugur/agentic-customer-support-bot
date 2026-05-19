// Adapters.AI/Qdrant/QdrantVectorMemoryAdapter.cs
// DRIVEN ADAPTER — IVectorMemoryPort → Qdrant implementasyonu.
// Her MemoryKind ayrı koleksiyona yazılır → SearchAsync filter'a ihtiyaç duymaz.

using System.Globalization;
using CustomerSupportBot.Application.Ports.Driven.AI;
using CustomerSupportBot.Domain.Model.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace CustomerSupportBot.Adapters.AI.Qdrant;

/// <summary>
/// Qdrant vektör deposu adapter'ı.
/// SemanticMemoryService bu adapter aracılığıyla episodic memory'e erişir.
/// </summary>
public sealed class QdrantVectorMemoryAdapter : IVectorMemoryPort
{
    private readonly QdrantClient _client;
    private readonly ILogger<QdrantVectorMemoryAdapter> _logger;

    // Payload alan adları — search hit → MemoryDocument hidrasyonu için.
    private const string PayloadKind = "_kind";
    private const string PayloadText = "_text";
    private const string PayloadTitle = "_title";
    private const string PayloadSource = "_source";
    private const string PayloadSessionId = "_sessionId";
    private const string PayloadCreatedAt = "_createdAt";
    private const string PayloadDocId = "_docId"; // string id'yi koruruz

    public QdrantVectorMemoryAdapter(
        IOptions<SemanticMemoryOptions> options,
        ILogger<QdrantVectorMemoryAdapter> logger)
    {
        _logger = logger;
        var q = options.Value.Qdrant;
        _client = string.IsNullOrWhiteSpace(q.ApiKey)
            ? new QdrantClient(q.Host, q.Port, q.UseHttps)
            : new QdrantClient(q.Host, q.Port, q.UseHttps, q.ApiKey);
    }

    public async Task EnsureCollectionAsync(string collection, int dimension, CancellationToken ct = default)
    {
        var exists = await _client.CollectionExistsAsync(collection, ct).ConfigureAwait(false);
        if (exists)
        {
            // Dim uyuşmazlığında collection'ı sil ve yeniden yarat (dev-friendly).
            try
            {
                var info = await _client.GetCollectionInfoAsync(collection, ct).ConfigureAwait(false);
                var existingDim = (int)(info?.Config?.Params?.VectorsConfig?.Params?.Size ?? 0);
                if (existingDim > 0 && existingDim != dimension)
                {
                    _logger.LogWarning(
                        "Qdrant collection {Collection} dim={Existing}, beklenen={Expected}. Yeniden oluşturuluyor (mevcut veriler silinecek).",
                        collection, existingDim, dimension);
                    await _client.DeleteCollectionAsync(collection, cancellationToken: ct).ConfigureAwait(false);
                }
                else
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Qdrant collection {Collection} bilgisi alınamadı; mevcut sayılıp atlandı.", collection);
                return;
            }
        }

        await _client.CreateCollectionAsync(
            collection,
            new VectorParams { Size = (ulong)dimension, Distance = Distance.Cosine },
            cancellationToken: ct).ConfigureAwait(false);

        _logger.LogInformation("Qdrant collection oluşturuldu: {Collection} (dim={Dim})",
            collection, dimension);
    }

    public async Task UpsertAsync(
        string collection,
        IReadOnlyList<(MemoryDocument Doc, float[] Vector)> items,
        CancellationToken ct = default)
    {
        if (items.Count == 0) return;

        var points = new List<PointStruct>(items.Count);
        foreach (var (doc, vec) in items)
        {
            var p = new PointStruct
            {
                Id = ToPointId(doc.Id),
                Vectors = vec
            };
            p.Payload[PayloadKind] = doc.Kind.ToString();
            p.Payload[PayloadText] = doc.Text ?? "";
            p.Payload[PayloadDocId] = doc.Id;
            p.Payload[PayloadCreatedAt] = doc.CreatedAt.ToString("O", CultureInfo.InvariantCulture);
            if (doc.Title != null) p.Payload[PayloadTitle] = doc.Title;
            if (doc.Source != null) p.Payload[PayloadSource] = doc.Source;
            if (doc.SessionId != null) p.Payload[PayloadSessionId] = doc.SessionId;
            foreach (var kv in doc.Tags)
                p.Payload[kv.Key] = kv.Value ?? "";

            points.Add(p);
        }

        await _client.UpsertAsync(collection, points, cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MemorySearchHit>> SearchAsync(
        string collection,
        float[] query,
        int topK,
        float minScore,
        IReadOnlyDictionary<string, string>? tagFilter = null,
        CancellationToken ct = default)
    {
        Filter? filter = null;
        if (tagFilter is { Count: > 0 })
        {
            filter = new Filter();
            foreach (var kv in tagFilter)
                filter.Must.Add(new Condition { Field = new FieldCondition { Key = kv.Key, Match = new Match { Keyword = kv.Value } } });
        }

        IReadOnlyList<ScoredPoint> hits;
        try
        {
            hits = await _client.SearchAsync(
                collection, query, filter: filter,
                limit: (ulong)topK,
                cancellationToken: ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Qdrant search başarısız (collection={Collection}); boş liste dönüyorum", collection);
            return Array.Empty<MemorySearchHit>();
        }

        var output = new List<MemorySearchHit>(hits.Count);
        foreach (var h in hits)
        {
            if (h.Score < minScore) continue;
            output.Add(new MemorySearchHit { Document = HydrateDocument(h.Payload), Score = h.Score });
        }
        return output;
    }

    public Task DeleteAsync(string collection, string id, CancellationToken ct = default)
        => _client.DeleteAsync(collection, ToPointId(id).Uuid is { } _ ? Guid.Parse(id) : Guid.Empty,
            cancellationToken: ct);

    public async Task<long> CountAsync(string collection, CancellationToken ct = default)
    {
        try
        {
            var info = await _client.GetCollectionInfoAsync(collection, ct).ConfigureAwait(false);
            return (long)(info.PointsCount);
        }
        catch
        {
            return 0;
        }
    }

    // ─── helpers ───

    private static PointId ToPointId(string id)
    {
        // Qdrant point id ya UUID ya da uint64. GUID kullanıyoruz; parse edemezsek deterministik hash → guid.
        if (Guid.TryParse(id, out var g)) return new PointId { Uuid = g.ToString() };
        // Stable hash → guid v5-benzeri
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(id));
        var guidBytes = new byte[16];
        Array.Copy(bytes, guidBytes, 16);
        return new PointId { Uuid = new Guid(guidBytes).ToString() };
    }

    private static MemoryDocument HydrateDocument(IDictionary<string, Value> payload)
    {
        var doc = new MemoryDocument
        {
            Id = payload.TryGetValue(PayloadDocId, out var idV) ? idV.StringValue : Guid.NewGuid().ToString(),
            Text = payload.TryGetValue(PayloadText, out var txt) ? txt.StringValue : "",
            Title = payload.TryGetValue(PayloadTitle, out var ttl) ? ttl.StringValue : null,
            Source = payload.TryGetValue(PayloadSource, out var src) ? src.StringValue : null,
            SessionId = payload.TryGetValue(PayloadSessionId, out var sid) ? sid.StringValue : null,
        };

        if (payload.TryGetValue(PayloadKind, out var kindV) &&
            Enum.TryParse<MemoryKind>(kindV.StringValue, ignoreCase: true, out var k))
            doc.Kind = k;

        if (payload.TryGetValue(PayloadCreatedAt, out var ca) &&
            DateTime.TryParse(ca.StringValue, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var dt))
            doc.CreatedAt = dt;

        // Diğer tüm key'leri Tags'a kopyala (rezerve isimler hariç)
        foreach (var kv in payload)
        {
            if (kv.Key.StartsWith('_')) continue;
            doc.Tags[kv.Key] = kv.Value.StringValue ?? "";
        }

        return doc;
    }
}
