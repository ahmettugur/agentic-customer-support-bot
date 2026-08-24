# ISemanticMemoryIngestor

**Dosya:** `Services/Memory/ISemanticMemoryIngestor.cs`
**Tür:** `public interface` (Application-internal servis arayüzü)
**Namespace:** `CustomerSupportBot.Application.Services.Memory`

## 1. Ne İşe Yarar

Vektör depoya (Qdrant) toplu doküman yazma/silme kapasitesini soyutlayan arayüz. Somut vektör
veritabanı detaylarını (Adapters.AI katmanındaki Qdrant implementasyonu) Application
katmanından gizler.

## 2. Hangi Amaçla Kullanılır

[`SemanticMemoryService`](SemanticMemoryService.md), [`KnowledgeBaseIngestionService`](KnowledgeBaseIngestionService.md)
ve [`LessonMiner`](../Improvement/LessonMiner.md) bu arayüz üzerinden vektör belleğe
yazar/siler — hangi somut vektör veritabanının kullanıldığını bilmeden.

## 3. Sorumlulukları

- **Üstlendiği:** Toplu upsert, tekil silme, "stale" (artık üretilmeyen) belge temizliği,
  koleksiyon varlığını garanti etme, sayım.
- **Üstlenmediği:** Vektör aramanın kendisi (bu ayrı bir port/servis zincirinde — embedding +
  arama), somut vektör veritabanı bağlantısı (Adapters.AI'de).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- İki implementasyonu vardır: gerçek Qdrant tabanlı (Adapters.AI katmanı) ve
  [`DisabledSemanticMemoryIngestor`](DisabledSemanticMemoryIngestor.md) (Null Object, bellek
  kapalıyken).
- **Kimin tarafından kullanılır:** [`SemanticMemoryService`](SemanticMemoryService.md),
  [`KnowledgeBaseIngestionService`](KnowledgeBaseIngestionService.md), `LessonMiner`.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

**`DeleteStaleAsync` — neden var, sadece `UpsertManyAsync` yeterli değil mi:** Bir KB kaynağı
yeniden indekslendiğinde (ör. bir makale silindi/birleştirildi), yeni indeksleme sadece
GEÇERLİ belgeleri yazar — ama vektör deposunda ESKİ, artık kaynakta olmayan belgeler kalmaya
devam eder (upsert var olanı günceller, fazlalığı silmez). `DeleteStaleAsync`, belirli bir
etiket değerini (ör. `"source" = "kb-v3"`) TAŞIMAYAN tüm belgeleri siler — yeniden
indeksleme sonrası "artık üretilmeyen" belgelerin temizlenmesini sağlar.

> 🐞 **`CountAsync` neden var — "kaynak değişmedi" kısayolunun gizli bağımlılığı.**
> İndeksleme mantığı (bkz. [`KnowledgeBaseIngestionService`](KnowledgeBaseIngestionService.md))
> kaynak dosyanın hash'i değişmediyse yeniden indekslemeyi ATLAR (performans optimizasyonu).
> Ama bu kısayol TEK BAŞINA yanlış olabilir: koleksiyon dışarıdan (ör. Qdrant'ı manuel
> sıfırlama, konteyner yeniden oluşturma) BOŞALTILMIŞ olabilir — kaynak hash'i hâlâ aynıdır ama
> koleksiyon artık boştur. `CountAsync` bu durumu yakalamak için eklendi: "kaynak değişmedi"
> kısayolu artık YALNIZCA hash aynı VE koleksiyon dolu ise geçerli sayılır; aksi halde
> re-ingest yine de tetiklenir.

`DeleteAsync`, kayıt yoksa **sessizce geçer (idempotent)** — silme işleminin "zaten yok"
durumunu bir hata olarak ele almaması, çağıran tarafların ayrıca "önce var mı diye kontrol et"
yazmasını gereksiz kılar.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Enabled` (`bool`) | Semantik bellek özelliği açık mı. |
| `IsConfigured` (`bool`) | Vektör deposu bağlantısı yapılandırılmış mı. |
| `EnsureCollectionsAsync(CancellationToken ct = default): Task` | Gerekli koleksiyonların var olduğunu garanti eder. |
| `UpsertManyAsync(MemoryKind kind, IReadOnlyList<MemoryDocument> docs, CancellationToken ct = default): Task` | Toplu belge yazar/günceller. |
| `DeleteAsync(MemoryKind kind, string documentId, CancellationToken ct = default): Task` | Tekil belge siler; kayıt yoksa sessizce geçer. |
| `DeleteStaleAsync(MemoryKind kind, string tagKey, string tagValue, CancellationToken ct = default): Task` | Belirtilen etiket değerini taşımayan tüm belgeleri siler. |
| `CountAsync(MemoryKind kind, CancellationToken ct = default): Task<long>` | Koleksiyondaki kayıt sayısı — "kaynak değişmedi" kısayolunu doğrulamak için kritik. |

## 7. Bağımlılıklar

Yok (arayüz).

## Bağlantılar

- [DisabledSemanticMemoryIngestor.md](DisabledSemanticMemoryIngestor.md) — Null Object implementasyonu
- [SemanticMemoryService.md](SemanticMemoryService.md) — ana tüketici
- [KnowledgeBaseIngestionService.md](KnowledgeBaseIngestionService.md) — `CountAsync`'i "kaynak değişmedi" kısayolunda kullanan taraf
