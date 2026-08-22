# PostgresKnowledgeArticleStore

**Dosya:** `Postgres/PostgresKnowledgeArticleStore.cs`
**Namespace:** `CustomerSupportBot.Adapters.Persistence.Postgres`
**Port:** [`IKnowledgeArticleStore`](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IKnowledgeArticleStore.md)

## 1. Ne İşe Yarar

Şirket bilgi bankası makalelerini (başlık, içerik, etiket, kategori, yayın durumu) `knowledge.knowledge_articles` tablosunda CRUD'layan, cache içermeyen doğrudan-DB adaptörü.

## 2. Hangi Amaçla Kullanılır

Admin panelindeki bilgi bankası yönetim ekranı ve `KnowledgeBaseIngestor` (Api katmanı — makaleleri Qdrant'a vektörleştirip gömen worker) tarafından okunur/yazılır.

## 3. Sorumlulukları

- Üstlendiği: makale CRUD'u, yayınlanmış/tümü ayrımı.
- Üstlenmediği: vektör embedding/semantik arama (bu `QdrantVectorMemoryAdapter`'ın işi — bu depo yalnızca "gerçek kaynak" metaveridir).

## 4. İlişkiler

- `IKnowledgeArticleStore` portunu implemente eder.
- `IDbContextFactory<CustomerSupportDbContext>` enjekte edilir.
- [`KnowledgeBaseIngestor`](../../CustomerSupportBot.Api/Workers/KnowledgeBaseIngestor.md) bu depodan okuyup Qdrant'a yazar.

## 5. Tasarım Yaklaşımı

Cache YOKTUR — bilgi bankası makaleleri admin tarafından seyrek değişir (yüksek okuma frekansı olan chat turlarının aksine), bu yüzden her okumada doğrudan DB'ye gitmenin maliyeti düşük ve basitlik tercih edilmiştir.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Task<IReadOnlyList<KnowledgeArticle>> GetAllAsync(CancellationToken ct)` | Yayın durumundan bağımsız tüm makaleler (admin görünümü). |
| `Task<IReadOnlyList<KnowledgeArticle>> GetPublishedAsync(CancellationToken ct)` | Yalnızca yayınlanmış makaleler (ingestion/son-kullanıcı görünümü). |
| `Task<KnowledgeArticle?> GetAsync(string id, CancellationToken ct)` | Tek makale. |
| `Task UpsertAsync(KnowledgeArticle article, CancellationToken ct)` | Var/yok kontrolüyle ekle veya güncelle. |
| `Task<bool> DeleteAsync(string id, CancellationToken ct)` | Kaydı siler. |

## 7. Bağımlılıklar

- `IDbContextFactory<CustomerSupportDbContext>`

## Bağlantılar

- [IKnowledgeArticleStore](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IKnowledgeArticleStore.md)
- [KnowledgeBaseIngestor](../../CustomerSupportBot.Api/Workers/KnowledgeBaseIngestor.md)
