# QdrantVectorMemoryAdapter

- **Kaynak:** `CustomerSupportBot.Adapters.AI/Qdrant/QdrantVectorMemoryAdapter.cs`
- **Tür:** `public sealed class : IVectorMemoryPort`
- **Namespace:** `CustomerSupportBot.Adapters.AI.Qdrant`

## Ne işe yarar?

`QdrantVectorMemoryAdapter`, Application katmanındaki [IVectorMemoryPort](../../CustomerSupportBot.Application/Ports/Outbound/AI/IVectorMemoryPort.md) portunu uygulayan; Qdrant gRPC istemcisi (`Qdrant.Client`) üzerinden şirket bilgi tabanı (`cs_knowledge`), kurallar (`cs_rules`), dersler (`cs_lessons`) ve episodik bellek (`cs_episodes`) koleksiyonlarında vektör indeksleme, güncelleme (`UpsertAsync`) ve Cosine Similarity benzerlik araması (`SearchAsync`) yapan adaptördür.

## Hangi amaçla kullanılır`?

- RAG (Retrieval-Augmented Generation) ve anlamsal bellek aramalarında metin vektörlerini depolamak ve en yakın `MemoryDocument` kayıtlarını getirmek.
- **Güvenli Boyut Geçişi (Fail-Closed Dimension Migration):** Embedding boyutu değiştiğinde (ör. 1536 ➔ 3072), varsayılan olarak verileri silmek yerine `InvalidOperationException` fırlatarak üretim verilerini korumak; yalnızca `AllowDestructiveDimensionMigration=true` ise koleksiyonu yeniden oluşturmak (`RequiresRecreate`).
- Her `MemoryKind` türünü ayrı Qdrant koleksiyonuna yazarak arama sırasında gereksiz filtreleme yükünden kaçınmak.

## Sorumlulukları

- **Üstlendiği:**
  - `EnsureCollectionAsync` ile koleksiyon varlığını ve vektör boyutunu denetlemek.
  - `UpsertAsync` ile doküman ve vektör çiftlerini gRPC `PointStruct` olarak Qdrant'a yüklemek.
  - `SearchAsync` ile Cosine mesafesine göre benzerlik araması yapmak ve `MemorySearchResult` listesi üretmek.
  - `DeleteBySourceAsync` ve `DeleteAsync` ile kaynak bazlı temizlik yapmak.

## Constructor ve Başlatma Mantığı

```csharp
public QdrantVectorMemoryAdapter(
    IOptions<SemanticMemoryOptions> options,
    ILogger<QdrantVectorMemoryAdapter> logger)
```

### Constructor İçerisinde Yapılan İşler:
1. `options.Value.VectorStore` yapılandırması okunur.
2. `_allowDestructiveDimensionMigration` bayrağı atanır.
3. `QdrantClient` örneği; API Key varsa `new QdrantClient(q.Host, q.Port, q.UseHttps, q.ApiKey)`, yoksa API keysiz olarak başlatılır.

## Metotlar ve İç Çalışma Mantıkları

### 1. `EnsureCollectionAsync`
```csharp
public async Task EnsureCollectionAsync(
    string collection,
    int dimension,
    CancellationToken ct = default)
```
- **Ne işe yarar?:** Belirtilen koleksiyonun mevcut olup olmadığını ve beklenen boyutta olduğunu doğrular; yoksa oluşturur.
- **İç Mantığı:**
  1. `_client.CollectionExistsAsync` ile kontrol edilir.
  2. Varsa `GetCollectionInfoAsync` ile mevcut vektör boyutu (`existingDim`) alınır.
  3. `RequiresRecreate` çağrılır: Boyut uyuşmazlığında `AllowDestructiveDimensionMigration` açık değilse `InvalidOperationException` fırlatılır. Açıksa koleksiyon silinip yeniden oluşturulur.
  4. Yoksa `CreateCollectionAsync(collection, new VectorParams { Size = (ulong)dimension, Distance = Distance.Cosine })` ile oluşturulur.

### 2. `SearchAsync`
```csharp
public async Task<IReadOnlyList<MemorySearchResult>> SearchAsync(
    string collection,
    float[] queryVector,
    int limit = 5,
    float minScore = 0.5F,
    string? filterSessionId = null,
    CancellationToken ct = default)
```
- **Ne işe yarar?:** Verilen sorgu vektörüne en yakın dokümanları arar.
- **İç Mantığı:**
  1. `filterSessionId` verilmişse Qdrant payload filtresi (`_sessionId == filterSessionId`) oluşturulur.
  2. `_client.SearchAsync` çağrısı yapılır (Limit ve minScore eşiğiyle).
  3. Dönen her `ScoredPoint` nesnesi payload alanlarından okunarak `MemoryDocument` nesnesine hidrate edilir ve `MemorySearchResult` listesi döndürülür.

### 3. `UpsertAsync`
```csharp
public async Task UpsertAsync(
    string collection,
    IReadOnlyList<(MemoryDocument Doc, float[] Vector)> items,
    CancellationToken ct = default)
```
- **Ne işe yarar?:** Dokümanları ve vektörlerini toplu olarak Qdrant'a yazar.
- **İç Mantığı:** Doküman ID'si UUID formatına (`ToPointId`) dönüştürülür, payload alanları (`_text`, `_title`, `_source`, `_sessionId`, `_createdAt`) doldurulur ve `_client.UpsertAsync` ile gönderilir.

## Bağımlılıklar

- `Qdrant.Client.QdrantClient`
- [IVectorMemoryPort](../../CustomerSupportBot.Application/Ports/Outbound/AI/IVectorMemoryPort.md)
- [MemoryDocument](../../CustomerSupportBot.Domain/Model/Memory/MemoryDocument.md)
- `CustomerSupportBot.Domain.Model.Memory.SemanticMemoryOptions`
