// Adapters.AI/Qdrant/QdrantVectorMemoryAdapter.cs
// DRIVEN ADAPTER — IVectorMemoryPort → Qdrant implementasyonu.
// Core bu adapter'ı bilmez; sadece IVectorMemoryPort'a bağımlıdır.

using CustomerSupportBot.Domain.Model.Memory;
using CustomerSupportBot.Application.Ports.Driven.AI;
using Microsoft.Extensions.Logging;
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

    public QdrantVectorMemoryAdapter(
        QdrantClient client,
        ILogger<QdrantVectorMemoryAdapter> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task EnsureCollectionAsync(string collection, int dimension, CancellationToken ct = default)
    {
        var collections = await _client.ListCollectionsAsync(ct);
        if (collections.Any(c => c == collection))
            return;

        await _client.CreateCollectionAsync(
            collection,
            new VectorParams { Size = (ulong)dimension, Distance = Distance.Cosine },
            cancellationToken: ct);

        _logger.LogInformation("Qdrant koleksiyon oluşturuldu: {Collection} (dim={Dimension})", collection, dimension);
    }

    public async Task UpsertAsync(
        string collection,
        IReadOnlyList<(MemoryDocument Doc, float[] Vector)> items,
        CancellationToken ct = default)
    {
        var points = items.Select(item =>
        {
            var (doc, vector) = item;
            var point = new PointStruct
            {
                Id = new PointId { Uuid = doc.Id },
                Vectors = vector,
                Payload =
                {
                    ["session_id"] = doc.SessionId ?? "",
                    ["text"] = doc.Text,
                    ["kind"] = doc.Kind.ToString(),
                    ["created_at"] = doc.CreatedAt.ToString("O")
                }
            };

            if (doc.Title is not null) point.Payload["title"] = doc.Title;
            if (doc.Source is not null) point.Payload["source"] = doc.Source;
            foreach (var tag in doc.Tags)
                point.Payload[tag.Key] = tag.Value;

            return point;
        }).ToList();

        await _client.UpsertAsync(collection, points, cancellationToken: ct);
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
            var conditions = tagFilter.Select(kv =>
                new Condition
                {
                    Field = new FieldCondition
                    {
                        Key = kv.Key,
                        Match = new Match { Text = kv.Value }
                    }
                }).ToList();
            filter = new Filter();
            foreach (var c in conditions) filter.Must.Add(c);
        }

        var results = await _client.SearchAsync(
            collection,
            query,
            limit: (ulong)topK,
            scoreThreshold: minScore,
            filter: filter,
            cancellationToken: ct);

        return results.Select(r =>
        {
            var tags = r.Payload
                .Where(kv => kv.Key is not ("session_id" or "text" or "kind" or "created_at" or "title" or "source"))
                .ToDictionary(kv => kv.Key, kv => kv.Value.StringValue);

            Enum.TryParse<MemoryKind>(
                r.Payload.TryGetValue("kind", out var kv2) ? kv2.StringValue : "Episodic",
                out var kind);

            return new MemorySearchHit
            {
                Document = new MemoryDocument
                {
                    Id        = r.Id.Uuid,
                    SessionId = r.Payload.TryGetValue("session_id", out var sid) ? sid.StringValue : "",
                    Text      = r.Payload.TryGetValue("text", out var txt) ? txt.StringValue : "",
                    Title     = r.Payload.TryGetValue("title", out var title) ? title.StringValue : null,
                    Source    = r.Payload.TryGetValue("source", out var src) ? src.StringValue : null,
                    Kind      = kind,
                    Tags      = tags
                },
                Score = r.Score
            };
        }).ToList();
    }

    public async Task DeleteAsync(string collection, string id, CancellationToken ct = default)
    {
        await _client.DeleteAsync(
            collection,
            new[] { new PointId { Uuid = id } },
            cancellationToken: ct);
    }

    public async Task<long> CountAsync(string collection, CancellationToken ct = default)
    {
        var info = await _client.GetCollectionInfoAsync(collection, ct);
        return (long)info.PointsCount;
    }
}
