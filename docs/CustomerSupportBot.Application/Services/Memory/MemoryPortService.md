# MemoryPortService

- **Kaynak:** `CustomerSupportBot.Application/Services/Memory/MemoryPortService.cs`
- **Tür:** `public sealed class : IMemoryPort`
- **Namespace:** `CustomerSupportBot.Application.Services.Memory`

## Ne işe yarar?

`MemoryPortService`, Application/Services/MemoryPortService.cs DRIVING PORT IMPL — IMemoryPort → SemanticMemoryService + IKnowledgeBaseIngestor.

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`MemoryPortService`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

```csharp
public MemoryPortService(SemanticMemoryService memory, IKnowledgeBaseIngestor ingestor)
```
- **Parametreler ve Başlatma:** Alınan servis bağımlılıkları (`readonly` alanlara) atanır ve gerekli başlatma kontrolleri yapılır.

## Metotlar ve İç Çalışma Mantıkları

### `CountAsync`
```csharp
public Task<long> CountAsync(MemoryKind kind, CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `SearchAsync`
```csharp
public Task<IReadOnlyList<MemorySearchHit>> SearchAsync(MemoryKind kind, string query, int? topK = null, CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `IngestAsync`
```csharp
public Task IngestAsync(CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `CountAsync`
```csharp
public Task<long> CountAsync(MemoryKind kind, CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `SearchAsync`
```csharp
public Task<IReadOnlyList<MemorySearchHit>> SearchAsync(MemoryKind kind, string query, int? topK = null, CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `IngestAsync`
```csharp
public Task IngestAsync(CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
- `IMemoryPort`
