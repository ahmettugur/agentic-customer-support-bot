# SemanticMemoryService

- **Kaynak:** `CustomerSupportBot.Application/Services/Memory/SemanticMemoryService.cs`
- **Tür:** `public sealed class : ISemanticMemoryIngestor, ISemanticMemoryWriter`
- **Namespace:** `CustomerSupportBot.Application.Services.Memory`

## Ne işe yarar?

`SemanticMemoryService`, Application/Services/Memory/SemanticMemoryService.cs Üst seviye memory facade'ı. Görevleri: - Üç collection'ı (Episodic / Lessons / Knowledge) ensure-edip yönetmek - WriteEpisodic / WriteLesson / SearchKnowledge gibi domain operasyonları sunmak - Tek-noktadan embedding + upsert + search akışı

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`SemanticMemoryService`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

```csharp
public SemanticMemoryService(IVectorMemoryPort store,
        IEmbeddingPort embedder,
        IContextSanitizer sanitizer,
        IOptions<SemanticMemoryOptions> options,
        ILogger<SemanticMemoryService> logger)
```
- **Parametreler ve Başlatma:** Alınan servis bağımlılıkları (`readonly` alanlara) atanır ve gerekli başlatma kontrolleri yapılır.

## Metotlar ve İç Çalışma Mantıkları

### `EnsureCollectionsAsync`
```csharp
public async Task EnsureCollectionsAsync(CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `CollectionFor`
```csharp
public string CollectionFor(MemoryKind kind)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `UpsertAsync`
```csharp
public async Task UpsertAsync(MemoryDocument doc, CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `UpsertManyAsync`
```csharp
public async Task UpsertManyAsync(MemoryKind kind, IReadOnlyList<MemoryDocument> docs, CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `DeleteStaleAsync`
```csharp
public async Task DeleteStaleAsync(
        MemoryKind kind, string tagKey, string tagValue, CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `DeleteAsync`
```csharp
public async Task DeleteAsync(MemoryKind kind, string documentId, CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `SearchAsync`
```csharp
public async Task<IReadOnlyList<MemorySearchHit>> SearchAsync(
        MemoryKind kind, string query, int? topK = null, float? minScore = null,
        CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `SearchByVectorAsync`
```csharp
public async Task<IReadOnlyList<MemorySearchHit>> SearchByVectorAsync(
        MemoryKind kind, float[] queryVector, int? topK = null, float? minScore = null,
        IReadOnlyDictionary<string, string>? tagFilter = null,
        CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `EmbedQueryAsync`
```csharp
public Task<float[]> EmbedQueryAsync(string query, CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `WriteEpisodeAsync`
```csharp
public Task WriteEpisodeAsync(string sessionId, string traceId, string userQuery,
        string finalResponse, string? intent, int? rating, string? customerId = null,
        CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `CountAsync`
```csharp
public async Task<long> CountAsync(MemoryKind kind, CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
- `ISemanticMemoryIngestor, ISemanticMemoryWriter`
