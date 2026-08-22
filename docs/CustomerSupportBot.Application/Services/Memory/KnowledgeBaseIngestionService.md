# KnowledgeBaseIngestionService

- **Kaynak:** `CustomerSupportBot.Application/Services/Memory/KnowledgeBaseIngestionService.cs`
- **Tür:** `public sealed class : IKnowledgeBaseIngestor`
- **Namespace:** `CustomerSupportBot.Application.Services.Memory`

## Ne işe yarar?

`KnowledgeBaseIngestionService`, Application/Services/Memory/KnowledgeBaseIngestionService.cs KnowledgeBase ingest use case'inin Application katmanı implementasyonu. Dosya erişimi IKnowledgeBaseSource (driven port) üzerinden; vector store ISemanticMemoryIngestor üzerinden. Chunking algoritması ve change-detection orkestrasyonu burada kapsüllenir. <summary>Belgenin hangi ingest turunda üretildiğini taşıyan etiket.</summary>

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`KnowledgeBaseIngestionService`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

```csharp
public KnowledgeBaseIngestionService(IKnowledgeBaseSource source,
        IKnowledgeArticleStore articles,
        ISemanticMemoryIngestor memory,
        IOptions<SemanticMemoryOptions> options,
        ILogger<KnowledgeBaseIngestionService> logger)
```
- **Parametreler ve Başlatma:** Alınan servis bağımlılıkları (`readonly` alanlara) atanır ve gerekli başlatma kontrolleri yapılır.

## Metotlar ve İç Çalışma Mantıkları

### `IngestAsync`
```csharp
public async Task IngestAsync(CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `ComputeArticlesFingerprint`
```csharp
internal static string ComputeArticlesFingerprint(IReadOnlyList<KnowledgeArticle> articles)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
- `IKnowledgeBaseIngestor`
