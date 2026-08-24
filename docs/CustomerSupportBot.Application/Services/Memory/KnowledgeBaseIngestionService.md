# KnowledgeBaseIngestionService

**Dosya:** `Services/Memory/KnowledgeBaseIngestionService.cs`
**Port:** `IKnowledgeBaseIngestor`
**Namespace:** `CustomerSupportBot.Application.Services.Memory`

## 1. Ne İşe Yarar

Bilgi bankasını (hem dosya tabanlı hem panelden yönetilen makaleler) okuyup parçalara ayırıp
(chunk) vektör belleğe indeksler. Değişiklik tespiti (hash tabanlı) ile **gereksiz yeniden
indekslemeyi** önler; "artık üretilmeyen" (silinen/küçülen) belgeleri temizler.

## 2. Hangi Amaçla Kullanılır

Api katmanındaki arka plan worker (`Workers/KnowledgeBaseIngestor.cs`) uygulama başlangıcında
ve periyodik olarak `IngestAsync`'i çağırır.

## 3. Sorumlulukları

- **Üstlendiği:** İki kaynağı (dosya sistemi + panel makaleleri) birleştirip tek bir indeksleme
  turu olarak işlemek, değişiklik tespiti, paragraf-farkında (paragraph-aware) chunk'lama,
  stale (artık üretilmeyen) belge temizliği.
- **Üstlenmediği:** Panelden tekil makale kaydı sırasındaki ANLIK indeksleme (bu
  [`KnowledgeArticleService`](KnowledgeArticleService.md)'te — bu sınıf yalnızca TOPLU/periyodik
  yeniden indeksleme senaryosunu kapsar).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `IKnowledgeBaseIngestor` port'unu implemente eder.
- **Inject eder:** `IKnowledgeBaseSource` (dosya erişimi, driven port), `IKnowledgeArticleStore`
  (panel makaleleri), `ISemanticMemoryIngestor` (vektör yazımı), `IOptions<SemanticMemoryOptions>`, `ILogger`.
- [`KnowledgeArticleService.BuildChunkDocuments`](KnowledgeArticleService.md)'i çağırarak
  panel makaleleri için AYNI chunk'lama algoritmasını (`ChunkText`) paylaşır — iki kaynağın
  arama sonuçlarında tutarlı granülerlikte görünmesi için.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

**Panel makaleleri neden burada TEKRAR işleniyor — zaten kaydedildiklerinde indekslenmiyorlar mı?**
Evet, normalde [`KnowledgeArticleService`](KnowledgeArticleService.md) bir makale kaydedildiği
anda indeksler. Bu sınıfın onları YİNE işlemesinin sebebi bir **kurtarma senaryosudur**:
embedding modeli değiştiğinde (farklı boyut) koleksiyon sıfırdan yaratılmak zorundadır ve TÜM
kaynaklar (dosyalar + makaleler) yeniden yazılmalıdır — panel makaleleri dosya sisteminden
bağımsız bir kaynak olduğu için, dosya tarafı hiç yoksa bile (`hasFiles = false`) makaleler
yine indekslenir.

> 🐞 **Değişiklik parmakizi (`currentHash`) neden İKİ kaynağı da kapsar.** Eğer yalnızca dosya
> dizininin hash'ine bakılsaydı, panel makalelerindeki bir değişiklik "kaynak değişmedi" olarak
> yanlış sınıflandırılır ve re-ingest sessizce atlanırdı. `ComputeArticlesFingerprint`,
> makalelerin (`id + son güncelleme zamanı`) DETERMİNİSTİK bir özetini üretir (sıralama
> garantilidir — store'un dönüş sırası değişse bile hash sabit kalır) ve dosya hash'iyle
> birleştirilir.

> 🐞 **"Kaynak değişmedi" kısayolu neden koleksiyon SAYISINI da kontrol eder.** Hash eşleşse
> bile, koleksiyon dışarıdan (Qdrant'ı manuel sıfırlama, embedding boyutu değişimi sonucu
> otomatik yeniden yaratma) BOŞALTILMIŞ olabilir — kaynak aynı kalır ama veri gitmiştir. Yalnızca
> hash'e güvenilseydi bu durumda re-ingest SESSİZCE atlanır ve sonuç: arama hiçbir sonuç
> döndürmeyen, hata da vermeyen BOŞ bir bilgi tabanı olurdu. `_memory.CountAsync` ile koleksiyon
> gerçekten dolu mu diye AYRICA doğrulanır; boşsa hash eşleşse bile yeniden yükleme yapılır.

> 🐞 **Dosya chunk kimliği neden kararlı (`file:{path}#{idx}`), rastgele `Guid` DEĞİL.**
> Kod içi yorumda kesin bir geçmiş hata anlatılır: Guid kullanıldığında her yeniden yükleme aynı
> içeriği YENİ bir kayıt olarak ekliyordu — upsert, id her seferinde yeni olduğu için fiilen
> insert'e dönüşüyordu. Sonuç iki hataydı: (1) değişmeyen dosyalar her turda ÇOĞALIYORDU;
> (2) düzenlenen bir dosyanın ESKİ metni aramada kalmaya devam ediyordu — yani düzeltilmiş
> yanlış bir bilgi "silinmiş" olmasına rağmen bot onu yanıtlamaya devam edebiliyordu. Kararlı
> kimlik (`path#chunk-index`), aynı chunk'ın her turda AYNI id ile upsert edilmesini, dolayısıyla
> güncellenmesini (insert değil) garanti eder.

> 🐞 **Kararlı kimlik TEK BAŞINA yetmez — `IngestStampTag` neden gerekli.** Kararlı kimlik,
> "artık üretilmeyen" belgeleri (kaynak dosyası silindiğinde/küçüldüğünde) ÇÖZEMEZ: o chunk'ların
> id'leri yeni turda hiç üretilmez, dolayısıyla "bu id'yi sil" diye TEK TEK silinemezler
> (üretilmeyen bir şeye referans verilemez). Çözüm: her turda üretilen TÜM belgelere o turun
> hash'i bir etiket (`ingest`) olarak yazılır; tur sonunda `DeleteStaleAsync(Knowledge, "ingest",
> currentHash)` çağrılarak bu etikete sahip OLMAYAN (yani önceki bir turdan kalma, artık
> üretilmeyen) TÜM belgeler tek seferde silinir.

**Sıralama: önce yaz, sonra sil.** `UpsertManyAsync` tamamlandıktan SONRA `DeleteStaleAsync`
çağrılır — tersi olsaydı (önce sil, sonra yaz), yazma adımı bir hatayla yarım kalırsa bilgi
tabanı GEÇİCİ olarak BOŞ kalırdı. Bu sırayla, en kötü ihtimalle kısa süre fazladan (eski) kayıt
bulunur — eksik kayıt bulunmasından her zaman daha iyidir.

**`ChunkText` — paragraf-farkında, overlap'li parçalama:** Metin çift satır sonuyla (`\n\n`)
paragraflara bölünür; paragraflar `size` sınırına ulaşana kadar biriktirilir. Sınır aşıldığında
bir chunk kapatılır ve `overlap` kadar metin bir SONRAKİ chunk'ın başına TAŞINIR (carry) —
bu, bir cümlenin/fikrin tam olarak chunk sınırında ikiye bölünüp bağlamının kaybolmasını azaltır.
Saf, I/O içermeyen bir algoritma olduğu için doğrudan test edilebilir.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `IngestAsync(CancellationToken ct = default): Task` | Ana orkestrasyon: değişiklik tespiti → chunk'lama → toplu yazım → stale temizlik. |
| `ComputeArticlesFingerprint(IReadOnlyList<KnowledgeArticle> articles): string` *(internal static)* | Makalelerin deterministik SHA-256 parmakizi. |
| `ChunkText(string text, int size, int overlap): IEnumerable<(string Chunk, int Index)>` *(internal static)* | Paragraf-farkında, overlap'li metin parçalama; saf fonksiyon. |

## 7. Bağımlılıklar (Constructor Injection)

- `IKnowledgeBaseSource` — dosya sistemi erişimi (driven port).
- `IKnowledgeArticleStore` — panelden yönetilen makaleler.
- `ISemanticMemoryIngestor` — vektör yazımı/silme/sayım.
- `IOptions<SemanticMemoryOptions>` — chunk boyutu/overlap ayarları.
- `ILogger<KnowledgeBaseIngestionService>` — atlama/hata/tamamlanma loglaması.

## Bağlantılar

- [KnowledgeArticleService.md](KnowledgeArticleService.md) — chunk'lama algoritmasını paylaşan, anlık indeksleme yapan taraf
- [ISemanticMemoryIngestor.md](ISemanticMemoryIngestor.md) — `CountAsync`'in kritik rolü
