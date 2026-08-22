# IKnowledgeBasePort ve KnowledgeArticleSaveResult

**Dosya:** `Ports/Inbound/IKnowledgeBasePort.cs`
**Namespace:** `CustomerSupportBot.Application.Ports.Inbound`

## 1. Ne işe yarar?

Bilgi tabanı (knowledge base) makalelerinin admin panelinden yönetimi için primary port — CRUD işlemleri hem kalıcı depoyu hem vektör indeksini (Qdrant) senkron tutar.

## 2. Hangi amaçla kullanılır?

Admin panelindeki "bilgi bankası" sayfası, makale oluşturma/güncelleme/silme/listeleme için bu portu kullanır. Botun `SemanticMemoryContextProvider`'ı bu makalelerin vektör indeksini arka planda dolaylı olarak kullanır (indeksleme bu port üzerinden tetiklenir).

## 3. Sorumlulukları

- **Üstlendiği:** Makale CRUD'u ve bu işlemler sırasında vektör indeksinin (Qdrant) senkron güncellenmesi.
- **Üstlenmediği:** Vektör aramasının kendisi (`IMemoryPort`/`SemanticMemoryContextProvider`'ın işi) — bu port sadece yazma tarafını (indeksleme dahil) kapsar.

## 4. Diğer katman/bileşenlerle ilişkileri

- Implementasyonu hem kalıcı depoyu (Postgres/EF) hem vektör store adaptörünü (`QdrantVectorMemoryAdapter`) kullanır.
- Admin panelindeki bilgi bankası sayfası tüketicisidir.

## 5. Kullanılma nedeni ve tasarım yaklaşımı

Kaydetme/silme işlemlerinin vektör indeksini otomatik senkron tutması, admin'in ayrıca "yeniden ingest et" demesini gereksiz kılar — iki ayrı adımın unutulma riskini (makale güncellendi ama indeks eski kaldı) ortadan kaldırır. `KnowledgeArticleSaveResult.Indexed`/`Warning` alanları, kalıcılık başarılı olsa bile indekslemenin ayrı başarısız olabileceği (ör. Qdrant erişilemez) senaryoyu şeffaf şekilde admin'e iletir — sessiz başarısızlık yerine.

## 6. Tipler ve Üyeler

### `KnowledgeArticleSaveResult(KnowledgeArticle Article, bool Indexed, string? Warning)`
Makale yazma sonucu. `Article`: kaydedilmiş hali (chunk sayısı güncellenmiş). `Indexed`: vector store'a yazıldı mı. `Warning`: `Indexed=false` ise nedeni, admin panelinde gösterilir.

### `IKnowledgeBasePort`

| Metot | Açıklama |
|---|---|
| `Task<IReadOnlyList<KnowledgeArticle>> ListAsync(CancellationToken ct = default)` | Tüm makaleleri listeler. |
| `Task<KnowledgeArticle?> GetAsync(string id, CancellationToken ct = default)` | Tek makale. |
| `Task<KnowledgeArticleSaveResult> CreateAsync(string title, string content, string? category, bool isPublished, string? updatedBy, CancellationToken ct = default)` | Yeni makale oluşturur ve (yayındaysa) indeksler. |
| `Task<KnowledgeArticleSaveResult?> UpdateAsync(string id, string title, string content, string? category, bool isPublished, string? updatedBy, CancellationToken ct = default)` | Mevcut makaleyi günceller ve indeksi tazeler; kayıt yoksa `null`. |
| `Task<bool> DeleteAsync(string id, CancellationToken ct = default)` | Makaleyi ve indeksteki tüm chunk'larını siler; kayıt yoksa `false`. |

## 7. Bağımlılıklar

`CustomerSupportBot.Domain.Model.Memory.KnowledgeArticle`.

## Bağlantılar

- [IMemoryPort](IMemoryPort.md) — okuma/arama tarafı.
