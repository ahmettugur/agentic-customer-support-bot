# FileSystemKnowledgeBaseSource

- **Kaynak:** `CustomerSupportBot.Adapters.Persistence/FileSystem/FileSystemKnowledgeBaseSource.cs`
- **Tür:** `public sealed class : IKnowledgeBaseSource`
- **Namespace:** `CustomerSupportBot.Adapters.Persistence.FileSystem`

## Ne işe yarar?

`FileSystemKnowledgeBaseSource`, Application katmanındaki [IKnowledgeBaseSource](../../CustomerSupportBot.Application/Ports/Outbound/AI/IKnowledgeBaseSource.md) portunu uygulayan; `KnowledgeBase/` dizinindeki Markdown dokümanlarını okuyarak RAG vektörleştirme ve indeksleme hattına ham makale verilerini sağlayan dosya sistemi kaynağıdır.

## Hangi amaçla kullanılır`?

- RAG bilgi bankasını harici veritabanı yüklemesi olmadan dosya sistemi üzerinden beslemek.
- Dosya içeriği ve hash'i üzerinden dokümanın değişip değişmediğini kontrol etmek.

## Metotlar ve İç Çalışma Mantıkları

### 1. `GetArticlesAsync`
```csharp
public async Task<IReadOnlyList<KnowledgeArticleSourceItem>> GetArticlesAsync(
    CancellationToken ct = default)
```
- **Ne işe yarar?:** `KnowledgeBase/` klasörünü tarar ve tüm `.md` dosyalarını `KnowledgeArticleSourceItem` listesi olarak döner.

## Bağımlılıklar

- [IKnowledgeBaseSource](../../CustomerSupportBot.Application/Ports/Outbound/AI/IKnowledgeBaseSource.md)
