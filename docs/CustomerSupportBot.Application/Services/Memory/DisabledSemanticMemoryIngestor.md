# DisabledSemanticMemoryIngestor

- **Kaynak:** `CustomerSupportBot.Application/Services/Memory/DisabledSemanticMemoryIngestor.cs`
- **Tür:** `public sealed class : ISemanticMemoryIngestor`
- **Namespace:** `CustomerSupportBot.Application.Services.Memory`

## Ne işe yarar?

`DisabledSemanticMemoryIngestor`, Application/Services/Memory/DisabledSemanticMemoryIngestor.cs Null Object pattern — SemanticMemory devre dışıyken kullanılır. Service locator anti-pattern'ı yerine açık null-object kaydı kullanır. <summary> Semantic memory devre dışı olduğunda DI'a kaydedilen no-op implementasyonu. </summary>

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`DisabledSemanticMemoryIngestor`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Metotlar ve İç Çalışma Mantıkları

### `EnsureCollectionsAsync`
```csharp
public Task EnsureCollectionsAsync(CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `UpsertManyAsync`
```csharp
public Task UpsertManyAsync(MemoryKind kind, IReadOnlyList<MemoryDocument> docs, CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `DeleteAsync`
```csharp
public Task DeleteAsync(MemoryKind kind, string documentId, CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `DeleteStaleAsync`
```csharp
public Task DeleteStaleAsync(MemoryKind kind, string tagKey, string tagValue, CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `CountAsync`
```csharp
public Task<long> CountAsync(MemoryKind kind, CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
- `ISemanticMemoryIngestor`
