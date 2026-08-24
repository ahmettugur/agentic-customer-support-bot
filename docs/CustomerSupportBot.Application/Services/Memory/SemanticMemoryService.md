# SemanticMemoryService

**Dosya:** `Services/Memory/SemanticMemoryService.cs`
**Portlar:** `ISemanticMemoryIngestor`, `ISemanticMemoryWriter`
**Namespace:** `CustomerSupportBot.Application.Services.Memory`

## 1. Ne İşe Yarar

Semantik (vektör) belleğin **üst seviye facade'ı** — üç koleksiyonu (Episodic/konuşma geçmişi,
Lessons/onaylı dersler, Knowledge/bilgi bankası) yönetir, embedding + upsert + arama akışını
tek noktadan sunar. Uygulamadaki HER YERİN vektör belleğe erişim şeklidir.

## 2. Hangi Amaçla Kullanılır

- [`ContextPipeline`](../Chat/ContextPipeline.md)'daki semantik bellek provider'ları arama
  (`SearchAsync`/`SearchByVectorAsync`) için kullanır.
- Bir tur bittiğinde `WriteEpisodeAsync` ile o turun episodik özeti yazılır.
- [`KnowledgeArticleService`](KnowledgeArticleService.md)/[`KnowledgeBaseIngestionService`](KnowledgeBaseIngestionService.md)/`LessonMiner`
  toplu yazım (`UpsertManyAsync`) için kullanır.
- [`MemoryPortService`](MemoryPortService.md) (driving port implementasyonu) bu sınıfı sarar.

## 3. Sorumlulukları

- **Üstlendiği:** Koleksiyon yönetimi, embedding çağrısını tetikleme, yazma-zamanı temizlik
  (sanitization), arama parametrelerinin (topK/minScore) varsayılanlarla doldurulması.
- **Üstlenmediği:** Somut vektör veritabanı bağlantısı (`IVectorMemoryPort`, Adapters.AI),
  somut embedding modeli çağrısı (`IEmbeddingPort`, Adapters.AI), okuma-zamanı fence sarma
  (bu `IContextSanitizer.WrapRetrieved`'ı çağıran context provider'ların işi — bu servis
  yalnızca YAZMA tarafında `Sanitize` çağırır).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- İki port'u AYNI ANDA implemente eder: `ISemanticMemoryIngestor` (toplu yazma/silme, KB
  ingest tarafından kullanılır) ve `ISemanticMemoryWriter` (episodik yazma, chat akışı tarafından).
- **Inject eder:** `IVectorMemoryPort`, `IEmbeddingPort`, [`IContextSanitizer`](ContextSanitizer.md),
  `IOptions<SemanticMemoryOptions>`, `ILogger`.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

**Neden tek bir "facade" sınıfı, üç ayrı servis değil:** Üç koleksiyon (Episodic/Lessons/Knowledge)
AYNI temel operasyonları (embed → upsert, embed → search) paylaşır; farklılık sadece HANGİ
koleksiyona yazıldığı/aranıldığıdır (`CollectionFor(MemoryKind)`). Üç ayrı sınıf, bu ortak
mantığı üç kez tekrarlardı. Tek facade, `MemoryKind` parametresiyle davranışı parametrize eder.

> 🐞 **`SearchByVectorAsync` — neden `SearchAsync`'ten ayrı bir metot.** `SearchAsync` her
> çağrıda sorgu metnini YENİDEN embed eder. Ama `SemanticMemoryContextProvider` (context
> pipeline'da) HER TURDA aynı kullanıcı mesajını hem Knowledge hem Lesson koleksiyonunda
> ayrı ayrı arıyordu — yani tur başına AYNI metin İKİ KEZ embed ediliyordu (embedder'da cache
> yok, her çağrı gerçek bir API isteğidir). `SearchByVectorAsync`, hazır bir vektör alıp
> embedding adımını ATLAR; çağıran taraf `EmbedQueryAsync`'i BİR KEZ çağırıp sonucu iki arama
> için paylaşabilir — embedding maliyetini yarıya indirir.

**Write-time sanitization + read-time fence = çift katman savunma:** `WriteEpisodeAsync`,
episodik belleğe yazılan kullanıcı sorgusunu/yanıtı `IContextSanitizer.Sanitize` ile temizler
(HTML yorumu, kontrol karakteri). Bu, veri YAZILIRKEN yapılan bir temizliktir. Ayrıca, bu veri
daha sonra bir context provider tarafından OKUNUP prompt'a eklenirken `WrapRetrieved` ile
fence'lenir (READ tarafı, bu sınıfın dışında). İki katmanın amacı farklıdır: yazma-zamanı
temizlik kalıcı depoyu temiz tutar, okuma-zamanı fence modelin retrieval içeriğini talimat
sanmamasını garanti eder — biri diğerinin yerini tutmaz.

**`WriteEpisodeAsync`'te `customerId` neden ayrıca bir ETİKET olarak yazılır:** Bir episodik
kaydın normal erişim yolu `SessionId`'dir (aynı oturumdaki geçmiş). Ama bir müşteri FARKLI bir
oturumda (ör. yeni bir sekme, yeniden login) tekrar konuştuğunda, önceki oturumlardaki
geçmişinin de bulunabilmesi gerekebilir — bu yüzden `customerId` (varsa, JWT-doğrulanmış) ayrı
bir etiket olarak eklenir ve retrieval bu etikete göre de arama yapabilir. Anonim/kimliksiz
turlarda bu alan boş kalır, kayıt yalnızca `SessionId` ile bulunabilir kalmaya devam eder.

`Enabled`/`docs.Count == 0`/`queryVector.Length == 0` gibi erken çıkışlar her metotta
tekrarlanır — bellek kapalıyken veya boş girdiyle çağrıldığında (ör. bir turda hiç kullanıcı
sorgusu yoksa) gereksiz bir API çağrısı (embedding, vektör deposu) YAPILMAMASI için.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Enabled` (`bool`) | `SemanticMemoryOptions.Enabled`. |
| `IsConfigured` (`bool`) | Embedding client yapılandırılmış mı. |
| `Options` (`SemanticMemoryOptions`) | Ayarlara doğrudan erişim (ör. [`MemoryPortService`](MemoryPortService.md) `Config` üretirken kullanır). |
| `EnsureCollectionsAsync(CancellationToken ct = default): Task` | Üç koleksiyonu idempotent olarak yaratır (startup'ta çağrılır). |
| `CollectionFor(MemoryKind kind): string` | `MemoryKind` → gerçek koleksiyon adı eşlemesi. |
| `UpsertAsync(MemoryDocument doc, CancellationToken ct = default): Task` | Tek doküman embed eder ve yazar. |
| `UpsertManyAsync(MemoryKind kind, IReadOnlyList<MemoryDocument> docs, CancellationToken ct = default): Task` | Toplu embed + yazım (KB ingest gibi). |
| `DeleteStaleAsync(MemoryKind kind, string tagKey, string tagValue, CancellationToken ct = default): Task` | Belirtilen etiketi taşımayan belgeleri siler. |
| `DeleteAsync(MemoryKind kind, string documentId, CancellationToken ct = default): Task` | Tekil belge siler. |
| `SearchAsync(MemoryKind kind, string query, int? topK = null, float? minScore = null, CancellationToken ct = default): Task<IReadOnlyList<MemorySearchHit>>` | Sorguyu embed edip arar. |
| `SearchByVectorAsync(MemoryKind kind, float[] queryVector, int? topK = null, float? minScore = null, IReadOnlyDictionary<string,string>? tagFilter = null, CancellationToken ct = default): Task<IReadOnlyList<MemorySearchHit>>` | Hazır vektörle arar — embedding adımını atlar. |
| `EmbedQueryAsync(string query, CancellationToken ct = default): Task<float[]>` | Sorguyu vektöre çevirir; çok koleksiyonlu aramada bir kez kullanılır. |
| `WriteEpisodeAsync(...)` | Bir turun episodik özetini (sanitize edilmiş) yazar. |
| `CountAsync(MemoryKind kind, CancellationToken ct = default): Task<long>` | Koleksiyondaki kayıt sayısı. |

## 7. Bağımlılıklar (Constructor Injection)

- `IVectorMemoryPort` — somut vektör veritabanı (Adapters.AI, Qdrant).
- `IEmbeddingPort` — somut embedding modeli çağrısı.
- `IContextSanitizer` — yazma-zamanı temizlik.
- `IOptions<SemanticMemoryOptions>` — koleksiyon adları, chunk/retrieval ayarları.
- `ILogger<SemanticMemoryService>` — toplu yazım loglaması.

## Bağlantılar

- [ContextSanitizer.md](ContextSanitizer.md) — yazma-zamanı temizlik
- [MemoryPortService.md](MemoryPortService.md) — bu servisi saran driving port
- [KnowledgeArticleService.md](KnowledgeArticleService.md) / [KnowledgeBaseIngestionService.md](KnowledgeBaseIngestionService.md) — toplu yazım tüketicileri
