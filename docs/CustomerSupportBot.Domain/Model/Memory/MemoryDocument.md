# MemoryDocument

- **Kaynak:** `CustomerSupportBot.Domain/Model/Memory/MemoryDocument.cs`
- **Tür:** `public sealed class`
- **Namespace:** `CustomerSupportBot.Domain.Model.Memory`
- **Aynı dosyada:** `MemoryKind` (enum), `MemorySearchHit` (class)

## 1. Ne işe yarar?

`MemoryDocument`, Qdrant vektör deposuna yazılan/oradan okunan tek bir belgeyi temsil eden saf domain modelidir. Üç farklı bellek türünü (geçmiş konuşmalar, öğrenilmiş dersler, statik bilgi bankası) aynı şema altında taşır — ayrımı `Kind` alanı yapar.

## 2. Hangi amaçla kullanılır?

- **Episodic:** Bir konuşma turunun (sorgu + yanıt + sonuç) uzun-ömürlü hafızaya yazılması — sonraki bir turda "bu müşteriyle daha önce ne konuşulmuştu" sorgusu için.
- **Lesson:** Self-improvement döngüsünün ürettiği, admin onaylı "öğrenilmiş ders"lerin (bkz. [Lesson](../Improvement/Lesson.md)) semantik aranabilir hale getirilmesi.
- **Knowledge:** `KnowledgeBase/` dizinindeki statik dokümanların (politika, SSS) ingest edilip aranabilir hale getirilmesi.

Üç tür de aynı Qdrant koleksiyon yapısını (embedding + metadata) kullandığı için tek bir model altında toplanmıştır; `SemanticMemoryContextProvider` bu üç koleksiyonu paralel arar.

## 3. Sorumlulukları

- **Üstlendiği:** Vektör deposuna yazılacak/okunacak bir belgenin alanlarını (metin, başlık, kaynak, oturum bağı, etiketler, oluşturulma zamanı) taşımak.
- **Üstlenmediği:** Embedding üretmek, Qdrant'a yazmak/okumak (bu iş Adapters.AI katmanındaki `QdrantVectorMemoryAdapter`'ın işi) — `MemoryDocument` sadece veri taşıyıcısıdır, I/O yapmaz.

## 4. Diğer katman ve bileşenlerle ilişkileri

- `CustomerSupportBot.Adapters.AI`'daki `QdrantVectorMemoryAdapter`, `MemoryDocument`'ları embedding'e çevirip Qdrant'a yazar/oradan okur.
- `Application/Services/Memory/SemanticMemoryService` bu modeli kullanarak arama/ekleme işlemlerini orkestre eder.
- `Application/Services/Providers/SemanticMemoryContextProvider`, arama sonucu dönen `MemorySearchHit` listesini prompt bağlamına çevirir.

## 5. Kullanılma nedeni ve tasarım yaklaşımı

Üç farklı bellek türü (Episodic/Lesson/Knowledge) ayrı ayrı sınıflar yerine tek bir `MemoryDocument` + `Kind` ayırt edici alanı ile modellenmiştir — üçü de aynı şekilde embed edilip aranıyor, sadece filtreleme/koleksiyon seçimi farklı. Bu, DRY ilkesine hizmet eder: arama/ekleme kodu tek bir tip üzerinden çalışır, `Kind`'a göre dallanma yalnızca gerektiğinde yapılır.

## 6. Metotlar / Üyeler

### `MemoryKind` (enum)

| Değer | Anlamı |
| ----- | ------ |
| `Episodic` | Geçmiş konuşma turu (sorgu + yanıt + sonuç). |
| `Lesson` | Self-improvement döngüsünün ürettiği, admin onaylı ders. |
| `Knowledge` | `KnowledgeBase/` dizininden ingest edilmiş statik bilgi. |

### `MemoryDocument`

| Üye | Tip | Açıklama |
| ----- | ----- | ---------- |
| `Id` | `string` | Benzersiz kimlik (varsayılan: yeni GUID). |
| `Kind` | `MemoryKind` | Bu belgenin hangi bellek türüne ait olduğu. |
| `Text` | `string` | Embed edilecek/edilmiş asıl metin. |
| `Title` | `string?` | İnsanın okuyacağı kısa başlık (UI/alıntı gösterimi için). |
| `Source` | `string?` | Kaynak bilgisi (dosya adı, `traceId`, `lessonId` vb.). |
| `SessionId` | `string?` | Bağlı olduğu oturum — `Episodic` türü için zorunludur. |
| `Tags` | `Dictionary<string, string>` | Filtreleme için ek metadata (intent, agent, rating gibi). |
| `CreatedAt` | `DateTime` | Oluşturulma zamanı (UTC). |

### `MemorySearchHit`

Vektör aramasının tek bir sonucunu taşır — bulunan belge + benzerlik skoru.

| Üye | Tip | Açıklama |
| ----- | ----- | ---------- |
| `Document` | `MemoryDocument` (`required`) | Bulunan belge. |
| `Score` | `float` (`required`) | Sorgu vektörüne benzerlik skoru (0-1 arası, yükseği daha benzer). |

## 7. Bağımlılıklar

Yok — Domain katmanı kuralına uygun olarak sıfır dış bağımlılık, saf C# veri modeli.

## Bağlantılar

- [CustomerProfile](CustomerProfile.md), [CustomerUnderstanding](CustomerUnderstanding.md) — İlişkili müşteri anlama modelleri.
- [Lesson](../Improvement/Lesson.md) — `Lesson` türünde belgenin kaynağı.
- [KnowledgeArticle](KnowledgeArticle.md) — `Knowledge` türünde belgenin kaynağı.
