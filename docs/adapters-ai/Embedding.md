# OpenAiEmbeddingAdapter

**Dosya:** `OpenAi/OpenAiEmbeddingAdapter.cs`  
**Port:** `IEmbeddingPort`  
**Yaşam döngüsü:** Singleton

Metni vektöre çevirir. OpenAI veya Azure OpenAI embedding API'sini kullanır.

---

## Neden embedding?

Semantic memory (Qdrant) **cosine similarity** ile arama yapar. Metni doğrudan değil, **anlamsal vektörü** karşılaştırır:

```
"Sipariş nerede?"      → [0.12, -0.45, 0.78, ...]   (1536 boyut)
"Kargom ne durumda?"   → [0.14, -0.42, 0.81, ...]
```

İki vektör arasında cosine similarity ~0.95 — semantik olarak benzer cümleler. Embedding bu vektörü üreten LLM.

---

## Property'ler

```csharp
public int Dimension { get; }        // 1536 (text-embedding-3-small) veya 3072 (large)
public string Model { get; }         // "text-embedding-3-small"
public bool IsConfigured { get; }    // _client null değil
```

`Dimension` ve `Model` `SemanticMemoryOptions.Embedding`'den gelir; Qdrant collection bu dimension'a göre yaratılır.

---

## Provider seçimi — graceful fallback

```
1. Provider=AzureOpenAI VE Azure endpoint+key var → Azure embedding
2. Provider=OpenAI VE OpenAI key var → OpenAI embedding
3. (Diğer provider seçilmiş ama) OpenAI key var → OpenAI'ye düş
4. Azure endpoint+key var → Azure'a düş
5. Hiçbiri yok → _client = null, warning log, memory devre dışı
```

**Neden bu kadar esnek?**

Chat için Anthropic kullanılsa bile **embedding için OpenAI gerekir** (Anthropic embedding API'sı yok). Bu fallback şunu sağlar:

```json
{
  "AI": {
    "Provider": "Anthropic",
    "OpenAI": { "ApiKey": "sk-..." }   // sadece embedding için
  }
}
```

`Adapter chat'i Anthropic'ten, embedding'i OpenAI'dan alır. Anthropic key olmasa bile semantic memory çalışır.

### IsConfigured == false

Hiçbir provider yapılandırılmamışsa:
- `_client = null`
- Warning loglanır
- `EmbedAsync` çağrıları boş array döner
- Application `IMemoryPort.Enabled = false` ile memory'i kapatır

---

## `EmbedAsync`

```csharp
public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
{
    if (!IsConfigured) return Array.Empty<float>();
    if (string.IsNullOrWhiteSpace(text)) return Array.Empty<float>();

    try
    {
        var result = await _client.GenerateEmbeddingAsync(text, cancellationToken: ct);
        return result.Vector.ToArray();
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
    catch (Exception ex)
    {
        throw ExceptionTranslator.Translate(ex, "Embedding generation failed");
    }
}
```

- Boş metin → boş array (API çağrısı yapma, ücretsiz)
- Configured değilse → boş array (memory zaten kapalı)
- Hata → `ExternalServiceException`

---

## `EmbedBatchAsync`

```csharp
public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(
    IEnumerable<string> texts,
    CancellationToken ct = default)
```

Toplu embedding — büyük dataset (KB ingestion, lesson archive) için.

### Batch sınırı

```csharp
const int MaxBatchSize = 64;
```

OpenAI API tek istekte ~2048 input kabul eder ama:
- 64 ile daha kısa latency (büyük batch yavaş döner)
- Hata olursa kayıp az (1 batch fail → max 64 item)

İç döngü:

```csharp
foreach (var batch in texts.Chunk(MaxBatchSize))
{
    var batchResult = await _client.GenerateEmbeddingsAsync(batch, ...);
    results.AddRange(batchResult.Select(r => r.Vector.ToArray()));
}
```

Sequential — paralel değil. Rate limit dostu.

### Kullanım

`FileSystemKnowledgeBaseSource` 20 KB dosyası okur:

```csharp
var docs = await kbSource.GetFilesAsync().ToListAsync();    // 20 doküman
var texts = docs.Select(d => d.Content).ToList();
var vectors = await embedding.EmbedBatchAsync(texts);       // 20 vektör
// batch=64 olduğu için 1 batch → 1 API çağrısı
await qdrant.UpsertAsync(docs.Zip(vectors));
```

---

## Embedding modelleri

| Model | Dimension | Maliyet/1M token |
|---|---|---|
| `text-embedding-3-small` | 1536 | $0.02 |
| `text-embedding-3-large` | 3072 | $0.13 |
| `text-embedding-ada-002` (legacy) | 1536 | $0.10 |

Default seçim: `text-embedding-3-small` (1536). Yeterli kaliteye sahip ve 6x ucuz.

**Dimension değişimi:**
- Qdrant collection o dimension'a göre yaratılır
- Model değiştirilirse collection yeniden inşa edilmeli (eski vektörler uyumsuz)
- `QdrantVectorMemoryAdapter.EnsureCollectionAsync` dimension uyumsuzluğunu tespit eder

---

## Yapılandırma

```json
{
  "SemanticMemory": {
    "Enabled": true,
    "VectorStore": {
      "Host": "localhost",
      "Port": 7334,
      "UseHttps": false,
      "ApiKey": ""
    },
    "Embedding": {
      "Model": "text-embedding-3-large",
      "Dimension": 3072
    },
    "Collections": {
      "Episodic": "cs_episodic",
      "Lessons": "cs_lessons",
      "Knowledge": "cs_knowledge"
    }
  }
}
```

---

## Performans

| İşlem | Tipik süre |
|---|---|
| Tek embedding | 50-200 ms (OpenAI) |
| Batch 64 | 150-400 ms |
| Boş text early return | <1 µs |

`EmbedAsync` her LLM çağrısının önünde değildir — sadece semantic search yapılırken. CustomerProfileService heuristic update'inde embedding **yapılmaz** (deterministic). Sadece consolidate veya KB ingestion sırasında.

---

## Bağlantılar

- [VectorMemory.md](VectorMemory.md) — embedding sonucu Qdrant'a nasıl yazılır
- [Adapters.Persistence FileSystemKnowledgeBaseSource](../adapters-persistence/FileSystemAdapters.md#filesystemknowledgebasesource)
