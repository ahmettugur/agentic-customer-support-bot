# SemanticMemoryOptions

**Kaynak:** `Ports/Outbound/AI/SemanticMemoryOptions.cs`
**Ayar bölümü:** `appsettings.json` → `"SemanticMemory"` (`SemanticMemoryOptions.SectionName`)

> Not: Bu dosyada ikinci bir sınıf daha (`SelfImprovementOptions`) tanımlıdır — konusu farklı
> olduğu için ayrı dosyada belgelenmiştir: [SelfImprovementOptions](SelfImprovementOptions.md).

## 1. Ne İşe Yarar

Semantic memory alt sisteminin tüm konfigürasyonunu tek çatı altında toplayan options sınıfı:
vektör deposu bağlantısı, embedding modeli, koleksiyon adları, retrieval parametreleri ve
knowledge base ingest ayarları.

## 2. Hangi Amaçla Kullanılır

DI container başlangıçta bu sınıfı appsettings'ten bind eder (`ValidateOnStart` ile); memory
ile ilgili tüm servisler (`SemanticMemoryService`, `QdrantVectorMemoryAdapter`,
`KnowledgeBaseIngestor`) `IOptions<SemanticMemoryOptions>` ile inject eder.

## 3. Sorumlulukları

- **Üstlendiği:** Yalnızca yapılandırma değerlerini taşımak — hiçbir davranış/iş mantığı
  içermez (dış içi boş bir DTO).
- **Üstlenmediği:** Değerlerin doğrulanması dışında (varsayılanlar güvenli), gerçek bağlantı
  kurma/embedding üretme mantığı ilgili adaptörlerdedir.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `VectorStoreOptions` → [`QdrantVectorMemoryAdapter`](../../../../CustomerSupportBot.Adapters.AI/Qdrant/QdrantVectorMemoryAdapter.md) bağlantı kurarken kullanır.
- `EmbeddingOptions` → [`OpenAiEmbeddingAdapter`](../../../../CustomerSupportBot.Adapters.AI/OpenAi/OpenAiEmbeddingAdapter.md) hangi modeli/boyutu kullanacağını buradan okur.
- `CollectionOptions` → koleksiyon adları [`IVectorMemoryPort`](IVectorMemoryPort.md) çağrılarında kullanılır.
- `KnowledgeBaseOptions` → [`KnowledgeBaseIngestor`](../../../../CustomerSupportBot.Api/Workers/KnowledgeBaseIngestor.md) (Api katmanı worker) tarafından okunur.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Tüm memory ayarlarının tek bir options ağacında toplanması, appsettings.json'da ilgili tüm
ayarların bir arada görünmesini sağlar (Options Pattern). İç içe `sealed class`'lar
(`VectorStoreOptions`, `EmbeddingOptions` vb.) her alt sistemin kendi mantıksal grubunu
korurken tek bir kök section altında kalmasını sağlar.

> 🐞 **`AllowDestructiveDimensionMigration` neden var:** varsayılan `false` — yani fail-closed.
> Eskiden bu davranış koşulsuzdu; embedding boyutu mevcut Qdrant koleksiyonuyla uyuşmadığında
> koleksiyon sessizce SİLİNİP yeniden oluşturuluyordu. Üretimde embedding modelini değiştirmek
> (ör. `-small` → `-large`) tüm knowledge base'i, dersleri ve episodic belleği sessizce
> siliyordu — üstelik `KnowledgeBaseIngestionService` kaynak hash'i değişmediği için
> re-ingest'i atlıyor, sonuç BOŞ bir bilgi tabanı oluyordu. Episodic bellek ve dersler ise
> yeniden üretilemez. Artık bu bayrak açılmadan boyut uyuşmazlığı bir **başlatma hatasıdır**.

## 6. Metotlar / Üyeler

| Üye | Varsayılan | Açıklama |
|---|---|---|
| `bool Enabled` | `true` | Semantic memory tümden kapatılabilir. |
| `VectorStoreOptions VectorStore` | — | `Host`, `Port` (6334), `UseHttps`, `ApiKey`, `AllowDestructiveDimensionMigration` (`false`). |
| `EmbeddingOptions Embedding` | — | `Model` (`text-embedding-3-small`), `Dimension` (1536). |
| `CollectionOptions Collections` | — | `Episodic` (`cs_episodic`), `Lessons` (`cs_lessons`), `Knowledge` (`cs_knowledge`). |
| `RetrievalOptions Retrieval` | — | `TopK` (4), `MinScore` (0.35), `MaxContextChars` (1800). |
| `KnowledgeBaseOptions KnowledgeBase` | — | `AutoIngestOnStartup` (`true`), `ChunkSize` (800), `ChunkOverlap` (100). |

## 7. Bağımlılıklar

Yok — saf options sınıfı.
