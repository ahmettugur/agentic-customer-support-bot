# KnowledgeArticleService

**Dosya:** `Services/Memory/KnowledgeArticleService.cs`
**Port:** `IKnowledgeBasePort` (driving/inbound port)
**Namespace:** `CustomerSupportBot.Application.Services.Memory`

## 1. Ne İşe Yarar

Admin panelinden yönetilen bilgi bankası **makalelerinin** CRUD use case'i: oluşturma,
güncelleme, silme — her yazma işleminde makaleyi hem kalıcı depoya (Postgres) hem vektör
belleğe (arama için) senkron tutar.

## 2. Hangi Amaçla Kullanılır

Api katmanındaki admin panel (bilgi bankası yönetim ekranı) bu servisi kullanır.

## 3. Sorumlulukları

- **Üstlendiği:** Makale kaydını DB'ye yazmak, yayındaysa (`IsPublished`) chunk'layıp vektör
  belleğe indekslemek, indeksleme başarısız olsa bile makalenin kaybolmamasını garanti etmek,
  silindiğinde/kısaldığında yetim chunk'ları temizlemek.
- **Üstlenmediği:** Dosya tabanlı KB'nin toplu/periyodik indekslenmesi (bu
  [`KnowledgeBaseIngestionService`](KnowledgeBaseIngestionService.md)'te — ama chunk'lama
  algoritmasını bu iki sınıf PAYLAŞIR).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `IKnowledgeBasePort` port'unu implemente eder.
- **Inject eder:** `IKnowledgeArticleStore` (kayıt otoritesi — Postgres), `ISemanticMemoryIngestor`
  (türetilmiş indeks), `IOptions<SemanticMemoryOptions>`, `ILogger`.
- `BuildChunkDocuments`, [`KnowledgeBaseIngestionService.ChunkText`](KnowledgeBaseIngestionService.md)'i
  ÇAĞIRIR — iki kaynağın (dosya + panel makalesi) arama sonuçlarında AYNI granülerlikte
  görünmesi için chunk'lama algoritması TEK yerde (`KnowledgeBaseIngestionService`) durur.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

> 🐞 **Kayıt otoritesi neden DB, vektör deposu neden "türetilmiş":** Kod içi yorumda açıkça
> belirtilir: `IKnowledgeArticleStore` (Postgres) TEK gerçek kaynak (source of truth), vektör
> deposu ondan TÜRETİLEN bir arama indeksidir. Bu yüzden sıra ÖNEMLİDİR — **önce DB'ye yaz,
> sonra indeksle.** İndeksleme (vektör yazımı) BAŞARISIZ OLURSA makale KAYBOLMAZ; yalnızca
> "indekslenmedi" uyarısı döner ve admin isterse tekrar kaydedip yeniden dener. Sıra tersine
> çevrilseydi (önce indeksle, sonra DB'ye yaz), indeksleme başarılı ama DB yazımı başarısız
> olan bir senaryoda arama sonucu döndüren ama GERÇEKTE VAR OLMAYAN bir makale ortaya çıkardı.

`SaveAndReindexAsync`, `IndexedChunkCount` alanını **her adımda gerçeğe uygun tutar**:
- Bellek kapalı/yapılandırılmamışsa → `0` yazılır (sonraki kayıt yanlışlıkla "silinecek chunk" aramasın).
- İndeksleme başarılıysa → gerçek chunk sayısı yazılır.
- İndeksleme HATA verirse → **önceki** (`previousChunkCount`) sayı geri yazılır, YENİ (yanlış)
  sayı değil — bu, DB'deki sayının vektör deposundaki gerçek durumla tutarlı kalmasını sağlar
  ki bir SONRAKİ kayıt, doğru aralığı (`from`/`to`) temizleyebilsin.

**`UpdateAsync` neden `previousChunkCount`'ı önceden okur:** Bir makale kısaltılırsa (ör. 5
chunk'tan 3 chunk'a düşerse), eski 4. ve 5. chunk'lar vektör deposunda YETİM kalır — yeni
yazım onları güncellemez, sadece 0-2 chunk'larını değiştirir. `RemoveChunksAsync(from: docs.Count,
to: previousChunkCount)` bu aralığı silerek yetim chunk temizler.

**`DeleteAsync` neden ÖNCE indeksi temizler, sonra DB satırını siler:** Sıra tersine çevrilseydi
(önce DB satırını sil), makale ID'si DB'de artık YOK olurdu — ama chunk kimlikleri
`KnowledgeArticle.ChunkId(articleId, idx)` fonksiyonuna, yani makale nesnesine bağlıdır. DB
satırı silindikten sonra bu chunk id'lerini bir daha türetemezdik ve yetim vektörler kalıcı
olarak ajanların yanıtlarını kirletmeye devam ederdi (asla temizlenemezlerdi).

`BuildChunkDocuments` **saf bir fonksiyondur** (I/O yok, `internal static`) — test edilebilirlik
için bilinçli bir ayrım; hangi metnin nasıl chunk'landığı, gerçek bir vektör deposuna
bağlanmadan doğrulanabilir.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `ListAsync(CancellationToken ct = default): Task<IReadOnlyList<KnowledgeArticle>>` | Tüm makaleleri listeler. |
| `GetAsync(string id, CancellationToken ct = default): Task<KnowledgeArticle?>` | Tekil makale getirir. |
| `CreateAsync(string title, string content, string? category, bool isPublished, string? updatedBy, CancellationToken ct = default): Task<KnowledgeArticleSaveResult>` | Yeni makale oluşturur, yayındaysa indeksler. |
| `UpdateAsync(string id, ..., CancellationToken ct = default): Task<KnowledgeArticleSaveResult?>` | Makaleyi günceller, yeniden indeksler, yetim chunk'ları temizler. |
| `DeleteAsync(string id, CancellationToken ct = default): Task<bool>` | Önce indeksi, sonra DB kaydını siler. |
| `BuildChunkDocuments(KnowledgeArticle article, int chunkSize, int chunkOverlap): List<MemoryDocument>` *(internal static)* | Makaleyi vektör dokümanlarına çevirir; saf fonksiyon. |
| `SaveAndReindexAsync(...)` *(private)* | DB yazımı + indeksleme + hata durumunda geri alma orkestrasyonu. |
| `RemoveChunksAsync(...)` *(private)* | Belirli bir chunk indeks aralığını vektör deposundan siler. |

## 7. Bağımlılıklar (Constructor Injection)

- `IKnowledgeArticleStore` — makalelerin kalıcı deposu (kayıt otoritesi).
- `ISemanticMemoryIngestor` — türetilmiş vektör indeksi.
- `IOptions<SemanticMemoryOptions>` — chunk boyutu/overlap.
- `ILogger<KnowledgeArticleService>` — indeksleme hatalarını loglar.

## Bağlantılar

- [KnowledgeBaseIngestionService.md](KnowledgeBaseIngestionService.md) — chunk'lama algoritmasının asıl sahibi, toplu/periyodik indeksleme
- [SemanticMemoryService.md](SemanticMemoryService.md) — `ISemanticMemoryIngestor`'ın gerçek implementasyonu
