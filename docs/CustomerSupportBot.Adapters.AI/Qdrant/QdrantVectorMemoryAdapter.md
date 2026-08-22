# QdrantVectorMemoryAdapter

**Kaynak:** `CustomerSupportBot.Adapters.AI/Qdrant/QdrantVectorMemoryAdapter.cs`
**Tür:** `public sealed class : IVectorMemoryPort`
**Namespace:** `CustomerSupportBot.Adapters.AI.Qdrant`

## 1. Ne İşe Yarar

Application katmanındaki [IVectorMemoryPort](../../CustomerSupportBot.Application/Ports/Outbound/AI/IVectorMemoryPort.md)
portunu, Qdrant vektör veritabanına karşı gRPC istemcisi (`Qdrant.Client`) ile uygulayan
adaptördür. Vektör ekleme/güncelleme (`UpsertAsync`), benzerlik araması (`SearchAsync`),
silme (`DeleteAsync`, `DeleteWhereTagNotAsync`) ve sayım (`CountAsync`) işlemlerini yapar.

## 2. Hangi Amaçla Kullanılır

- Anlamsal bellek (semantic memory) ve RAG bilgi tabanı akışlarında, metinlerden üretilen
  embedding vektörlerini kalıcı depoya yazmak ve sorgu vektörüne en yakın kayıtları getirmek.
- Her `MemoryKind` (ör. bilgi tabanı, ders, episodik bellek) **ayrı bir koleksiyona** yazılır
  — böylece `SearchAsync` içeride `Kind` filtresine ihtiyaç duymadan doğrudan doğru
  koleksiyonu sorgular.

## 3. Sorumlulukları

- **Üstlendiği:**
  - `EnsureCollectionAsync` — koleksiyonun var olduğunu ve doğru vektör boyutunda
    olduğunu garanti eder (yoksa oluşturur).
  - `UpsertAsync` — `(MemoryDocument, float[])` çiftlerini Qdrant `PointStruct`'a çevirip yazar.
  - `SearchAsync` — Cosine benzerliğine göre en yakın noktaları bulur, `minScore` altını eler,
    payload'dan `MemoryDocument`'a geri hidratlar.
  - `DeleteAsync` / `DeleteWhereTagNotAsync` — tekil veya etiket bazlı toplu silme.
  - `CountAsync` — koleksiyondaki nokta sayısını döner (hata durumunda `0`, fırlatmaz).
- **Üstlenmediği:** Embedding üretimi (bu iş [`OpenAiEmbeddingAdapter`](../OpenAi/OpenAiEmbeddingAdapter.md)'de), hangi metnin hangi `MemoryKind`'a ait olduğuna karar vermek (çağıran `SemanticMemoryService`'te).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `IVectorMemoryPort`'u implemente eder — Application katmanı somut Qdrant tipini hiç görmez.
- [`ExceptionTranslator`](../ExceptionTranslator.md) — gRPC (`Grpc.Core.RpcException`) ve diğer
  altyapı hatalarını domain istisnasına çevirir (`UpsertAsync`/`DeleteAsync`/`DeleteWhereTagNotAsync`
  bunu kullanır; `SearchAsync`/`CountAsync` hatada fırlatmak yerine boş sonuç/`0` döner —
  arama başarısızlığı turun tamamını durdurmamalı).
- `SemanticMemoryOptions` (`CustomerSupportBot.Application.Ports.Outbound.AI.SemanticMemoryOptions`)
  — `VectorStore` alt-bölümünden `Host`/`Port`/`UseHttps`/`ApiKey`/`AllowDestructiveDimensionMigration` okunur.
- [`MemoryDocument`](../../CustomerSupportBot.Domain/Model/Memory/MemoryDocument.md) — domain modeli, payload'a/'dan bu tipe dönüşüm burada olur.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

**Fail-closed boyut geçişi:** `EnsureCollectionAsync`, koleksiyonun mevcut vektör boyutu
beklenenle uyuşmuyorsa **varsayılan olarak hiçbir şey silmez** — `RequiresRecreate` bir
`InvalidOperationException` fırlatır ve uygulama başlangıçta durur.

> 🐞 **Neden böyle:** Eskiden bu kontrol koşulsuzdu ve boyut uyuşmazlığında koleksiyon
> otomatik siliniyordu ("dev-friendly" diye işaretlenmişti) — ama ortam ayrımı (dev/prod)
> yoktu, yani üretimde de aynı şekilde çalışıyordu. Embedding modeli değiştirildiğinde
> (ör. `text-embedding-3-small` → `-large`, boyut 1536→3072) bilgi tabanı, dersler ve
> episodik bellek **sessizce siliniyordu**; üstelik bilgi tabanı için yeniden yükleme
> (re-ingest) de atlanıyordu çünkü `KnowledgeBaseIngestionService` kaynak dosyanın hash'i
> değişmediği için "değişmemiş" sanıyordu. Sonuç: boş bilgi tabanı + geri getirilemeyen
> bellek. Düzeltme: silme artık yalnızca `SemanticMemory:VectorStore:AllowDestructiveDimensionMigration=true`
> açıkça verildiğinde gerçekleşir; aksi halde uygulama net bir hata mesajıyla durur.

**`RequiresRecreate`'in ayrı, `internal static` bir metot olmasının nedeni:** Gerçek
`QdrantClient` mock'lanamadığı (sealed/gRPC tabanlı) için `EnsureCollectionAsync`'in tamamı
birim testiyle sürülemez. Riskli olan asıl karar mantığı (silinecek mi, fırlatılacak mı)
bu şekilde saf bir fonksiyona çıkarılıp test altına alınmıştır.

**Nokta ID'leri neden GUID:** Qdrant point id'leri ya UUID ya da uint64 olabilir.
Domain'deki `MemoryDocument.Id` string olduğundan (`ToPointGuid`) önce doğrudan GUID
parse'ı denenir, olmazsa SHA-256 hash'inden **deterministik** bir GUID türetilir — aynı
string id her zaman aynı GUID'e eşlenir, böylece `UpsertAsync` ve `DeleteAsync` **aynı**
noktayı hedefler (aksi halde silme yanlış noktayı vurur).

## 6. Metotlar / Üyeler

| Üye | İmza | Açıklama |
|---|---|---|
| `EnsureCollectionAsync` | `Task EnsureCollectionAsync(string collection, int dimension, CancellationToken ct = default)` | Koleksiyon yoksa oluşturur; varsa boyut uyuşmazlığını `RequiresRecreate` ile değerlendirir. |
| `RequiresRecreate` *(internal static)* | `bool RequiresRecreate(string collection, int existingDim, int expectedDim, bool allowDestructive)` | Boyut uyuşmazlığı kararını verir: uyumluysa `false`, izin yoksa fırlatır, izin varsa `true`. |
| `UpsertAsync` | `Task UpsertAsync(string collection, IReadOnlyList<(MemoryDocument Doc, float[] Vector)> items, CancellationToken ct = default)` | Dokümanları payload'a (`_kind`, `_text`, `_docId`, `_createdAt`, `_title`, `_source`, `_sessionId`, etiketler) çevirip toplu yazar. |
| `SearchAsync` | `Task<IReadOnlyList<MemorySearchHit>> SearchAsync(string collection, float[] query, int topK, float minScore, IReadOnlyDictionary<string,string>? tagFilter = null, CancellationToken ct = default)` | `tagFilter` varsa `Filter.Must` koşulları kurar, `_client.QueryAsync` çağırır, `minScore` altını eler, sonuçları hidratlar. Qdrant'a ulaşılamazsa boş liste döner (fırlatmaz). |
| `DeleteAsync` | `Task DeleteAsync(string collection, string id, CancellationToken ct = default)` | Tek noktayı `ToPointGuid(id)` ile siler. |
| `DeleteWhereTagNotAsync` | `Task DeleteWhereTagNotAsync(string collection, string tagKey, string tagValue, CancellationToken ct = default)` | Belirli bir etiket değerine **sahip olmayan** tüm noktaları siler (ör. kaynak dosyası değişmiş/kaldırılmış eski chunk'ları temizlemek için). |
| `CountAsync` | `Task<long> CountAsync(string collection, CancellationToken ct = default)` | Koleksiyondaki nokta sayısını döner; herhangi bir hata durumunda `0` döner. |
| `ToPointGuid` *(internal static)* | `Guid ToPointGuid(string id)` | String id'yi deterministik GUID'e çevirir (parse edilemezse SHA-256 tabanlı hash). |

## 7. Bağımlılıklar

Constructor: `IOptions<SemanticMemoryOptions> options`, `ILogger<QdrantVectorMemoryAdapter> logger`.
`options.Value.VectorStore`'dan `Host`/`Port`/`UseHttps`/`ApiKey` okunarak `QdrantClient`
kurulur (API key boşsa key'siz overload kullanılır).

## Bağlantılar

- [../../CustomerSupportBot.Application/Ports/Outbound/AI/IVectorMemoryPort.md](../../CustomerSupportBot.Application/Ports/Outbound/AI/IVectorMemoryPort.md)
- [../ExceptionTranslator.md](../ExceptionTranslator.md)
- [../../CustomerSupportBot.Domain/Model/Memory/MemoryDocument.md](../../CustomerSupportBot.Domain/Model/Memory/MemoryDocument.md)
- [../OpenAi/OpenAiEmbeddingAdapter.md](../OpenAi/OpenAiEmbeddingAdapter.md) — vektörleri üreten taraf
