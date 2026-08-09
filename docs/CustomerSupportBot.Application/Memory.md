# Memory (Semantik Bellek)

Bu belge Application katmanındaki semantik bellek altyapısını kapsar.

## Bileşenler

```
SemanticMemoryService          ← Facade — tüm bellek operasyonları buradan
    ├── IVectorMemoryPort      ← Adapter (Qdrant / InMemory)
    └── IEmbeddingPort         ← Adapter (OpenAI / Azure)

KnowledgeBaseIngestionService  ← Knowledge base dosyalarını yükler
SemanticMemoryContextProvider  ← ContextPipeline entegrasyonu

ISemanticMemoryWriter          ← CustomerSupportTeam tarafından kullanılır (episodik)
ISemanticMemoryIngestor        ← Admin endpoint / startup tarafından kullanılır (KB)
```

---

## SemanticMemoryService

**Dosya:** `Services/Memory/SemanticMemoryService.cs`  
**Implements:** `ISemanticMemoryIngestor`, `ISemanticMemoryWriter`  
**Yaşam döngüsü:** Singleton (yalnızca `SemanticMemory.Enabled=true` ise kaydedilir)

### 3 koleksiyon

| Koleksiyon | Anahtar | İçerik | Kim yazar? |
|-----------|---------|--------|----------|
| Episodic | `options.Collections.Episodic` | Geçmiş Q&A çiftleri | `WriteEpisodeAsync` (CustomerSupportTeam) |
| Lessons | `options.Collections.Lessons` | Çıkarılan dersler | `LessonMiner` |
| Knowledge | `options.Collections.Knowledge` | Ürün/politika dokümanları | `KnowledgeBaseIngestionService` |

### `EnsureCollectionsAsync`

```csharp
public async Task EnsureCollectionsAsync(CancellationToken ct = default)
```

Startup sırasında çağrılır. Her koleksiyonu idempotent olarak oluşturur (zaten varsa dokunmaz). Embedding boyutunu (`IEmbeddingPort.Dimension`) koleksiyon şemasına geçirir.

### `UpsertAsync` / `UpsertManyAsync`

Tek veya toplu doküman yazar. Adımlar:
1. `IEmbeddingPort.EmbedAsync(text)` → embedding vektörü üret
2. `IVectorMemoryPort.UpsertAsync(collection, [(doc, vec)])` → vektör deposuna yaz

`UpsertManyAsync` embedding'leri batch'te üretir (`EmbedBatchAsync`) — tek tek çağrıdan daha verimli.

### `SearchAsync`

```csharp
public async Task<IReadOnlyList<MemorySearchHit>> SearchAsync(
    MemoryKind kind, string query,
    int? topK = null, float? minScore = null,
    CancellationToken ct = default)
```

1. `IEmbeddingPort.EmbedAsync(query)` → sorgu vektörü
2. `IVectorMemoryPort.SearchAsync(collection, vec, topK, minScore)` → en yakın K dokümanı bul

Varsayılan `topK` ve `minScore` değerleri `SemanticMemoryOptions.Retrieval` bölümünden gelir.

### `WriteEpisodeAsync`

```csharp
public Task WriteEpisodeAsync(
    string sessionId, string traceId,
    string userQuery, string finalResponse,
    string? intent, int? rating,
    CancellationToken ct = default)
```

`CustomerSupportTeam` her tamamlanan workflow sonrasında bu metodu fire-and-forget olarak çağırır.

**Oluşturulan doküman:**

```
Title: "Siparişim nerede?" (max 80 karakter, tek satır)
Text:  "Soru: Siparişim nerede?\n\nYanıt: 4821 kargoda..." (max 1200 karakter)
Tags:  { "intent": "order_inquiry", "rating": "5" }
```

Bu dokümanlar ileriki oturumlarda `SemanticMemoryContextProvider` tarafından benzer sorularda geri getirilir.

> 🔒 **Sanitizasyon (write-time):** `userQuery` ve `finalResponse` belleğe yazılmadan önce `IContextSanitizer.Sanitize` uygulanır — newline dışındaki C0/C1 kontrol karakterleri temizlenir, `<!-- ... -->` HTML comment blokları kaldırılır, aşırı uzunluk kırpılır. Okuma tarafında ayrıca `<retrieved_data>` fence uygulanır (çift katman, bkz. ContextPipeline.md).

---

## KnowledgeBaseIngestionService

**Dosya:** `Services/Memory/KnowledgeBaseIngestionService.cs`  
**Implements:** `IKnowledgeBaseIngestor`

Knowledge base (ürün dokümanları, politika metinleri) dosyalarını okuyup `SemanticMemoryService.UpsertManyAsync` ile Knowledge koleksiyonuna yükler. Admin endpoint'i veya startup hook'u tarafından tetiklenir.

---

## `SemanticMemoryOptions` yapılandırması

`appsettings.json` → `SemanticMemory:` bölümü:

```json
{
  "SemanticMemory": {
    "Enabled": false,
    "Collections": {
      "Episodic":  "episodic_memory",
      "Lessons":   "learned_lessons",
      "Knowledge": "knowledge_base"
    },
    "Retrieval": {
      "TopK": 5,
      "MinScore": 0.70,
      "MaxContextChars": 2000
    }
  }
}
```

| Ayar | Açıklama |
|------|---------|
| `Enabled` | false ise tüm memory servisleri `DisabledSemanticMemoryIngestor` ile replace edilir |
| `TopK` | Semantic search'te döndürülecek max sonuç sayısı |
| `MinScore` | Minimum cosine similarity skoru (0.0-1.0) |
| `MaxContextChars` | `SemanticMemoryContextProvider`'ın bağlama ekleyeceği max karakter |

## Semantic memory kapalıysa ne olur?

`AddMemoryServices` içinde:
```csharp
services.AddSingleton<ISemanticMemoryIngestor, DisabledSemanticMemoryIngestor>();
services.AddSingleton<IMemoryPort, DisabledMemoryPort>();
services.AddSingleton<IContextProvider, NoopContextProvider>();
```

- `DisabledSemanticMemoryIngestor` — tüm metodlar no-op (sessizce geri döner)
- `NoopContextProvider` — `GetContextAsync` her zaman null döner
- `ISemanticMemoryWriter` **kayıtlı değildir** — `CustomerSupportTeam`'e null gelir, episodik bellek atlanır

## Yeni embedding provider eklemek

`IEmbeddingPort` arayüzünü implement eden yeni bir adapter yazın:

```csharp
public interface IEmbeddingPort
{
    int Dimension { get; }
    bool IsConfigured { get; }
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
    Task<List<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
}
```

Adapteri `Adapters.AI` altına ekleyin ve DI'da kaydedin.
