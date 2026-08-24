# IKnowledgeArticleStore

**Kaynak:** `Ports/Outbound/Persistence/IKnowledgeArticleStore.cs`
**Implementasyon:** [`PostgresKnowledgeArticleStore`](../../../../CustomerSupportBot.Adapters.Persistence/Postgres/PostgresKnowledgeArticleStore.md)

## 1. Ne İşe Yarar

Panelden yönetilen bilgi tabanı (knowledge base) makalelerinin kalıcılığı için secondary port.

## 2. Hangi Amaçla Kullanılır

Admin panelindeki KB editörü `GetAllAsync`/`UpsertAsync`/`DeleteAsync` ile makaleleri yönetir;
[`KnowledgeBaseIngestor`](../../../../CustomerSupportBot.Api/Workers/KnowledgeBaseIngestor.md)
yalnızca `GetPublishedAsync` ile yayındaki makaleleri okuyup vektör indeksine yazar.

## 3. Sorumlulukları

- **Üstlendiği:** Makale CRUD'u ve yayın durumu ayrımı.
- **Üstlenmediği:** Vektör indeksleme — vector store (Qdrant) bu makalelerden **türetilmiş bir
  indekstir**; kayıt otoritesi (source of truth) burasıdır, Qdrant değil.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`Adapters.Persistence/Postgres/PostgresKnowledgeArticleStore` implemente eder.
`GetPublishedAsync`'in döndürdüğü liste `KnowledgeBaseIngestor` tarafından
[`IEmbeddingPort`](../AI/IEmbeddingPort.md) + [`IVectorMemoryPort`](../AI/IVectorMemoryPort.md)
ile işlenip Qdrant'a yazılır.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Yayında olmayan (taslak) makalelerin ingest'e dahil edilmemesi için `GetPublishedAsync` ayrı
bir metot — admin bir makaleyi düzenlerken/tamamlamadan kaydederken bunun yanlışlıkla canlı
bilgi tabanına sızmaması gerekir.

## 6. Metotlar / Üyeler

| Metot | Açıklama |
|---|---|
| `Task<IReadOnlyList<KnowledgeArticle>> GetAllAsync(CancellationToken ct = default)` | Tüm makaleler (taslak dahil, admin UI). |
| `Task<IReadOnlyList<KnowledgeArticle>> GetPublishedAsync(CancellationToken ct = default)` | Yalnızca yayında olanlar — ingest bunu kullanır. |
| `Task<KnowledgeArticle?> GetAsync(string id, CancellationToken ct = default)` | Tekil sorgu. |
| `Task UpsertAsync(KnowledgeArticle article, CancellationToken ct = default)` | Oluştur/güncelle. |
| `Task<bool> DeleteAsync(string id, CancellationToken ct = default)` | Siler; kayıt yoksa `false` (çağıran 404 üretebilsin diye). |

## 7. Bağımlılıklar

Port arayüzü `CustomerSupportBot.Domain.Model.Memory.KnowledgeArticle`'a bağımlıdır.
