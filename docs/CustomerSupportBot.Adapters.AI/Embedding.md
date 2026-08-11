# OpenAiEmbeddingAdapter

**Dosya:** `OpenAi/OpenAiEmbeddingAdapter.cs`  
**Port:** `IEmbeddingPort`  
**Yaşam döngüsü:** Singleton

## 1. Ne İşe Yarar

Metni vektöre çevirir. OpenAI veya Azure OpenAI embedding API'sini kullanır.

## 2. Hangi Amaçla Kullanılır

Semantic memory (Qdrant) arama yapabilmek için metinlerin sayısal vektörlere dönüştürülmesi gerekir. Bu adapter o dönüşümü yapar.

> 💡 **Analiz notu:** Kütüphanedeki bir kitap dizin kartı gibi düşün — "sipariş nerede?" sorusunu 1536 boyutlu bir sayı dizisine çevirir. Benzer anlamdaki cümleler birbirine yakın vektörler üretir. Bu sayede "kargom ne durumda?" da aynı kitap dizin kartına yakın çıkar.

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
public bool IsConfigured { get; }    // _client null değil
```

`Dimension` `SemanticMemoryOptions.Embedding.Dimension`'dan gelir; Qdrant collection bu dimension'a göre yaratılır. Model adı `SemanticMemoryOptions.Embedding.Model`'dan client oluşturulurken kullanılır ama ayrı bir property olarak expose edilmez.

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

Seçili chat sağlayıcısının embedding config'i eksik olsa bile, elde başka bir sağlayıcının key'i varsa semantic memory yine çalışsın diye. Örneğin:

```json
{
  "AI": {
    "Provider": "AzureOpenAI",
    "OpenAI": { "ApiKey": "sk-..." }   // Azure embedding config'i yoksa buna düşer
  }
}
```

Adapter chat'i Azure'dan, embedding'i OpenAI'dan alır — eksik config semantic memory'yi devre dışı bırakmaz.

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
    if (_client is null)
        throw new InvalidOperationException("Embedding client yapılandırılmadı (OpenAI/AzureOpenAI ApiKey eksik).");
    if (string.IsNullOrWhiteSpace(text))
        return new float[Dimension];

    var result = await _client.GenerateEmbeddingAsync(text, cancellationToken: ct).ConfigureAwait(false);
    return result.Value.ToFloats().ToArray();
}
```

- Boş/whitespace metin → `Dimension` boyutunda sıfır dolu array döner (API çağrısı yapılmaz)
- `_client` null ise → `InvalidOperationException` fırlatır (memory etkinken ApiKey eksik yapılandırma hatası)
- Hata → exception caller'a yayılır

---

## `EmbedBatchAsync`

```csharp
public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(
    IReadOnlyList<string> texts,
    CancellationToken ct = default)
```

Toplu embedding — büyük dataset (KB ingestion, lesson archive) için. `IReadOnlyList<string>` alır.

### Batch sınırı

```csharp
const int batchSize = 64;
```

OpenAI API tek istekte ~2048 input kabul eder ama:

- 64 ile daha kısa latency (büyük batch yavaş döner)
- Hata olursa kayıp az (1 batch fail → max 64 item)

İç döngü:

```csharp
for (int i = 0; i < texts.Count; i += batchSize)
{
    var slice = texts.Skip(i).Take(batchSize).ToList();
    var resp = await _client.GenerateEmbeddingsAsync(slice, cancellationToken: ct).ConfigureAwait(false);
    output.AddRange(resp.Value.Select(e => e.ToFloats().ToArray()));
}
```

Sequential — paralel değil. Rate limit dostu. Boş liste → `Array.Empty<float[]>()` döner; `_client` null ise → `InvalidOperationException`.

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
| --- | --- | --- |
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
| --- | --- |
| Tek embedding | 50-200 ms (OpenAI) |
| Batch 64 | 150-400 ms |
| Boş text early return | <1 µs |

`EmbedAsync` her LLM çağrısının önünde değildir — sadece semantic search yapılırken. CustomerProfileService heuristic update'inde embedding **yapılmaz** (deterministic). Sadece consolidate veya KB ingestion sırasında.

---

## Bağlantılar

- [VectorMemory.md](VectorMemory.md) — embedding sonucu Qdrant'a nasıl yazılır
- [Adapters.Persistence FileSystemKnowledgeBaseSource](../adapters-persistence/FileSystemAdapters.md#filesystemknowledgebasesource)
