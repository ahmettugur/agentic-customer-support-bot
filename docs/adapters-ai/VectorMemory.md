# QdrantVectorMemoryAdapter

**Dosya:** `Qdrant/QdrantVectorMemoryAdapter.cs`  
**Port:** `IVectorMemoryPort`  
**Yaşam döngüsü:** Singleton  
**Protokol:** gRPC (port 6334)

Qdrant vector store ile etkileşim — semantic memory için cosine similarity search.

---

## Neden Qdrant?

Vektörel arama ihtiyaçları:
- ✅ Cosine similarity / dot product
- ✅ Metadata (payload) ile filter
- ✅ Yüksek hacim (yüzbinler/milyonlar)
- ✅ HNSW index — sub-millisecond search

Qdrant Rust ile yazılmış, embedded veya containerize çalışabilir. Alternatifler: Pinecone (SaaS), Milvus, Weaviate, pgvector (Postgres).

---

## Bağlantı

```csharp
public QdrantVectorMemoryAdapter(
    IOptions<SemanticMemoryOptions> options,
    ILogger<QdrantVectorMemoryAdapter> logger)
{
    var qdrant = options.Value.Qdrant;
    _client = new QdrantClient(
        host: qdrant.Host,
        port: qdrant.Port,
        https: qdrant.UseHttps,
        apiKey: qdrant.ApiKey);   // Opsiyonel
    _collectionName = qdrant.CollectionName;
}
```

`QdrantClient` thread-safe, internal connection pool yönetir. Singleton güvenli.

---

## Payload şeması

Her vektör Qdrant'a kaydedilirken yanına metadata (payload) eklenir:

| Anahtar | Tip | İçerik |
|---|---|---|
| `_kind` | string | `Episodic`, `Lesson`, `Knowledge` |
| `_text` | string | Memory metni (embed edilen) |
| `_title` | string? | Opsiyonel başlık |
| `_source` | string? | Kaynak (örn. dosya adı, trace ID) |
| `_sessionId` | string? | Session referansı |
| `_createdAt` | string (ISO 8601) | Oluşturma zamanı |
| `_docId` | string | Document ID (orijinal Memory.Id) |
| *(diğer)* | string | Kullanıcı tag'leri (`Tags` Dict) |

Reserved alanlar `_` prefix'iyle — kullanıcı tag'leriyle çakışmaz.

---

## `EnsureCollectionAsync`

```csharp
public async Task EnsureCollectionAsync(int dimension, CancellationToken ct = default)
{
    var collections = await _client.ListCollectionsAsync(ct);
    if (collections.Contains(_collectionName))
    {
        // Dimension match kontrolü
        var info = await _client.GetCollectionInfoAsync(_collectionName, ct);
        var existingDim = info.Config.Params.VectorsConfig.Params.Size;
        if (existingDim != (ulong)dimension)
        {
            _logger.LogWarning("Dimension mismatch ({Existing} vs {New}), recreating", existingDim, dimension);
            await _client.DeleteCollectionAsync(_collectionName, cancellationToken: ct);
            await CreateCollection(dimension, ct);
        }
    }
    else
    {
        await CreateCollection(dimension, ct);
    }
}

private async Task CreateCollection(int dimension, CancellationToken ct)
{
    await _client.CreateCollectionAsync(
        _collectionName,
        new VectorParams { Size = (ulong)dimension, Distance = Distance.Cosine },
        cancellationToken: ct);
}
```

### Dev-friendly mismatch handling

Eğer Qdrant collection 1536-dim ile yaratılmışsa ama config'de 3072 isteniyorsa:
- **Eski collection silinir** (tüm vektörler kaybolur!)
- Yeni collection 3072-dim ile yaratılır

⚠️ Production'da bu davranış **tehlikeli** — silmek yerine error fırlatmak gerekir. Bu kod development kolaylığı için (model değiştirme rahatlığı).

Startup'ta `MemoryPortService.IngestAsync` bu metodu çağırır.

---

## `UpsertAsync`

```csharp
public async Task UpsertAsync(
    IEnumerable<(MemoryDocument Document, float[] Vector)> items,
    CancellationToken ct = default)
{
    var points = items.Select(item => new PointStruct
    {
        Id = ToPointId(item.Document.Id),
        Vectors = item.Vector,
        Payload = { /* payload Dict */ }
    }).ToList();

    await _client.UpsertAsync(_collectionName, points, cancellationToken: ct);
}
```

Batch upsert — N item tek HTTP gRPC call'da yazılır.

### `ToPointId` — string ID dönüşümü

Qdrant point ID **UUID veya integer** olmalı. Domain `MemoryDocument.Id` string. Çözüm:

```csharp
private static PointId ToPointId(string id)
{
    if (Guid.TryParse(id, out var guid))
        return new PointId { Uuid = guid.ToString() };

    // Fallback: SHA-256 hash → deterministik GUID
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(id));
    var guidBytes = hash.Take(16).ToArray();
    return new PointId { Uuid = new Guid(guidBytes).ToString() };
}
```

- Eğer id zaten GUID formatında → direkt kullan
- Değilse → SHA-256 hash'in ilk 16 byte'ı ile deterministik GUID

**Deterministik** kısmı önemli: Aynı string ID hep aynı GUID'i verir → aynı dokümanın iki kez upsert edilmesi duplicate yaratmaz (Qdrant'ta upsert = insert or update).

---

## `SearchAsync`

```csharp
public async Task<IReadOnlyList<MemorySearchHit>> SearchAsync(
    float[] query,
    int topK,
    float minScore = 0f,
    IReadOnlyDictionary<string, string>? tagFilter = null,
    CancellationToken ct = default)
```

### Filter oluşturma

Tag filter varsa Qdrant `Filter` oluşturulur:

```csharp
Filter? filter = null;
if (tagFilter is { Count: > 0 })
{
    var conditions = tagFilter.Select(kv => new Condition
    {
        Field = new FieldCondition
        {
            Key = kv.Key,
            Match = new Match { Keyword = kv.Value }
        }
    });
    filter = new Filter { Must = { conditions } };
}
```

AND-combined (`Must`). Tüm tag'ler eşleşmeli.

### Örnek

```csharp
await memory.SearchAsync(
    query: queryVector,
    topK: 5,
    minScore: 0.75f,
    tagFilter: new Dictionary<string, string>
    {
        ["customer_id"] = "1990",
        ["_kind"] = "Episodic"
    });
```

Bu sorgu sadece 1990'ın episodic anılarını arar — global memory'den karışmaz.

### Graceful error

```csharp
catch (Exception ex)
{
    _logger.LogWarning(ex, "Vector search failed, returning empty");
    return Array.Empty<MemorySearchHit>();
}
```

Search hata verirse **boş liste** döner — semantic memory critical değil, fallback davranış gösterilir (örn. LLM context bilgisi olmadan da yanıt verir).

---

## `DeleteAsync`

```csharp
public async Task DeleteAsync(string id, CancellationToken ct = default)
{
    var pointId = ToPointId(id);
    await _client.DeleteAsync(_collectionName, new[] { pointId }, cancellationToken: ct);
}
```

Tek point silme. Lesson reddedildiğinde veya episodic memory expire olduğunda kullanılır.

---

## `CountAsync`

```csharp
public async Task<long> CountAsync(CancellationToken ct = default)
{
    try
    {
        var info = await _client.GetCollectionInfoAsync(_collectionName, ct);
        return (long)info.PointsCount;
    }
    catch
    {
        return 0;
    }
}
```

Admin paneli "memory'de kaç doküman var?" görseli için. Hata → 0 (cosmetic data, fail etmemeli).

---

## `HydrateDocument` — payload → domain

```csharp
private MemoryDocument HydrateDocument(ScoredPoint point)
{
    var payload = point.Payload;
    var tags = payload
        .Where(kv => !kv.Key.StartsWith("_"))
        .ToDictionary(kv => kv.Key, kv => kv.Value.StringValue);

    return new MemoryDocument
    {
        Id = payload["_docId"].StringValue,
        Kind = Enum.Parse<MemoryKind>(payload["_kind"].StringValue),
        Text = payload["_text"].StringValue,
        Title = payload.GetValueOrDefault("_title")?.StringValue,
        Source = payload.GetValueOrDefault("_source")?.StringValue,
        SessionId = payload.GetValueOrDefault("_sessionId")?.StringValue,
        CreatedAt = DateTime.Parse(payload["_createdAt"].StringValue),
        Tags = tags
    };
}
```

Reserved alanlar `_` ayıklanır, kalanı `Tags` Dict'e gider — kullanıcı eklediği custom tag'ler korunur.

---

## Exception handling

`ExceptionTranslator` Qdrant gRPC exception'larını çevirir:

| gRPC Status | Domain Exception |
|---|---|
| `Unavailable` | `ExternalServiceException("Qdrant", "database unavailable")` |
| `DeadlineExceeded` | `ExternalServiceException("Qdrant", "operation timeout")` |
| `NotFound` | `EntityNotFoundException("VectorCollection")` |
| `AlreadyExists` | `ConcurrencyConflictException` |

Detay: [ExceptionTranslator.md](ExceptionTranslator.md).

---

## Collection management

Production'da collection lifecycle:

1. **İlk kurulum:** `EnsureCollectionAsync` startup'ta çalışır
2. **Backup:** `qdrant-backup` CLI ile snapshot
3. **Migration:** Yeni model → dimension değişikliği → manuel re-ingest
4. **Cleanup:** Eski/expired memory için `DeleteAsync`

---

## Bağlantılar

- [Embedding.md](Embedding.md) — vektör nasıl üretilir
- [Domain Model-Memory.md](../domain/Model-Memory.md) — `MemoryDocument`, `MemoryKind`
- [Application MemoryPortService](../application/MemoryPortService.md)
