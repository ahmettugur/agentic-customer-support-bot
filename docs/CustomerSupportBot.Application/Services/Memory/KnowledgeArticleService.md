# KnowledgeArticleService

- **Kaynak:** `CustomerSupportBot.Application/Services/Memory/KnowledgeArticleService.cs`
- **Tür:** `public sealed class : IKnowledgeBasePort`
- **Namespace:** `CustomerSupportBot.Application.Services.Memory`

## Ne işe yarar?

`KnowledgeArticleService`, Application/Services/Memory/KnowledgeArticleService.cs Panelden yönetilen bilgi tabanı makalelerinin use case'i.  Kayıt otoritesi IKnowledgeArticleStore (Postgres); vector store türetilmiş indekstir. Bu yüzden sıra önemlidir: önce DB'ye yaz, sonra indeksle. İndeksleme patlarsa makale kaybolmaz — yalnızca "indekslenmedi" uyarısı döner ve admin tekrar kaydedebilir.

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`KnowledgeArticleService`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

```csharp
public KnowledgeArticleService(IKnowledgeArticleStore store,
        ISemanticMemoryIngestor memory,
        IOptions<SemanticMemoryOptions> options,
        ILogger<KnowledgeArticleService> logger)
```
- **Parametreler ve Başlatma:** Alınan servis bağımlılıkları (`readonly` alanlara) atanır ve gerekli başlatma kontrolleri yapılır.

## Metotlar ve İç Çalışma Mantıkları

### `ListAsync`
```csharp
public Task<IReadOnlyList<KnowledgeArticle>> ListAsync(CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `GetAsync`
```csharp
public Task<KnowledgeArticle?> GetAsync(string id, CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `CreateAsync`
```csharp
public async Task<KnowledgeArticleSaveResult> CreateAsync(
        string title, string content, string? category, bool isPublished,
        string? updatedBy, CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `UpdateAsync`
```csharp
public async Task<KnowledgeArticleSaveResult?> UpdateAsync(
        string id, string title, string content, string? category, bool isPublished,
        string? updatedBy, CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `DeleteAsync`
```csharp
public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `BuildChunkDocuments`
```csharp
internal static List<MemoryDocument> BuildChunkDocuments(KnowledgeArticle article, int chunkSize, int chunkOverlap)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
- `IKnowledgeBasePort`
