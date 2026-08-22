# IVectorMemoryPort

**Kaynak:** `Ports/Outbound/AI/IVectorMemoryPort.cs`
**Implementasyon:** [`QdrantVectorMemoryAdapter`](../../../../CustomerSupportBot.Adapters.AI/Qdrant/QdrantVectorMemoryAdapter.md)

## 1. Ne İşe Yarar

Semantic memory'nin vektör deposu (Qdrant) ile konuşmasını soyutlayan port. Koleksiyon
yönetimi, upsert, benzerlik araması, silme ve sayma işlemlerini kapsar.

## 2. Hangi Amaçla Kullanılır

`SemanticMemoryService` (episodik bellek), `KnowledgeBaseIngestor` (KB) ve ders/lesson
depolama akışları bu port üzerinden Qdrant'a yazar/okur.

## 3. Sorumlulukları

- **Üstlendiği:** Koleksiyon oluşturma (idempotent), upsert, top-K benzerlik araması (opsiyonel
  etiket filtresiyle), tekil silme, koşullu toplu silme (`DeleteWhereTagNotAsync`), sayma.
- **Üstlenmediği:** Vektör üretimi — o [`IEmbeddingPort`](IEmbeddingPort.md)'un işi; bu port
  yalnızca hazır vektörü (`float[]`) depolar/arar.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`Adapters.AI/Qdrant/QdrantVectorMemoryAdapter` implemente eder. Koleksiyon adları
`SemanticMemoryOptions.CollectionOptions`'tan gelir (bkz. [SemanticMemoryOptions](SemanticMemoryOptions.md)).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Application katmanı somut bir vektör veritabanına (Qdrant) bağımlı olmasın diye bu port var —
ileride başka bir vektör deposuna geçilmesi yalnızca yeni bir adaptör yazmayı gerektirir.

`DeleteWhereTagNotAsync` özellikle dikkat gerektirir: yeniden indeksleme sırasında, her ingest
turu ürettiği belgeleri kendi damgasıyla (tag) yazar, sonra bu damgayı TAŞIMAYANLARI siler.
Tek tek id ile silinemez çünkü silinmesi gereken belgeler artık üretilmeyen belgelerdir — kaynak
dosyası silinmiş/küçülmüş olanlar; id'leri yeni turda hiç görünmediği için bilinemezler, tek
ayırt edici özellikleri damgalarının eskiliğidir.

## 6. Metotlar / Üyeler

| Metot | Açıklama |
|---|---|
| `Task EnsureCollectionAsync(string collection, int dimension, CancellationToken ct = default)` | Koleksiyon yoksa yaratır (idempotent). |
| `Task UpsertAsync(string collection, IReadOnlyList<(MemoryDocument Doc, float[] Vector)> items, CancellationToken ct = default)` | Bir/daha fazla dokümanı yazar. |
| `Task<IReadOnlyList<MemorySearchHit>> SearchAsync(string collection, float[] query, int topK, float minScore, IReadOnlyDictionary<string,string>? tagFilter = null, CancellationToken ct = default)` | Verilen vektöre en yakın N dokümanı döner. |
| `Task DeleteAsync(string collection, string id, CancellationToken ct = default)` | Tek bir noktayı siler. |
| `Task DeleteWhereTagNotAsync(string collection, string tagKey, string tagValue, CancellationToken ct = default)` | Belirtilen etiketi taşımayan tüm noktaları siler (re-index temizliği). |
| `Task<long> CountAsync(string collection, CancellationToken ct = default)` | Koleksiyondaki nokta sayısı (dashboard için). |

## 7. Bağımlılıklar

Port arayüzü `CustomerSupportBot.Domain.Model.Memory` tiplerine (`MemoryDocument`,
`MemorySearchHit`) bağımlıdır.
