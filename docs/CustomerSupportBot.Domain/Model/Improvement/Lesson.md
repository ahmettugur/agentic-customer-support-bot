# Lesson

- **Kaynak:** `CustomerSupportBot.Domain/Model/Improvement/Lesson.cs`
- **Tür:** `public sealed class` (+ `public enum LessonStatus`, aynı dosyada)
- **Namespace:** `CustomerSupportBot.Domain.Model.Improvement`

## 1. Ne İşe Yarar

Self-improving loop'un ürettiği **"öğrenilmiş ders"**i temsil eder — düşük puanlı veya hatalı
konuşma trace'lerinin analizinden çıkarılan, "X durumunda Y yapılmalı" biçiminde uygulanabilir
bir kural.

## 2. Hangi Amaçla Kullanılır

`LessonMiner` (Application katmanı) düşük-puanlı/hatalı trace'leri analiz edip `Lesson` önerir
(`Status = Proposed`). Admin panelinden bir admin bu öneriyi **Approve/Reject** eder
(`DecidedBy`/`DecidedAt`/`DecisionReason` doldurulur). `Approved` olan dersler Qdrant'ın
`Lessons` koleksiyonuna yazılır (`VectorMemoryId` set edilir) ve sonraki konuşmalarda
`SemanticMemoryContextProvider` üzerinden bağlama geri döner — yani bot geçmiş hatalarından
"öğrenip" aynı hatayı tekrarlamamaya çalışır.

## 3. Sorumlulukları

- ✅ Bir dersin içeriğini (başlık, kural metni, gözlem) taşımak
- ✅ Onay durumunu (`Proposed`/`Approved`/`Rejected`) ve kararın kim/ne zaman/neden verdiğini taşımak
- ✅ Dersi doğuran trace ID'lerini saklayarak izlenebilirlik sağlamak
- ❌ Trace analizini yapmak — bu `LessonMiner`'ın işi
- ❌ Qdrant'a yazmak — bu bir Application/Adapter servisinin işi, `VectorMemoryId` sadece sonucu tutar

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- **Kim üretir:** `LessonMiner` (Application katmanı, self-improving loop)
- **Kim tüketir/karar verir:** Admin panel endpoint'leri (Approve/Reject), `SemanticMemoryContextProvider`
  (approved dersleri context'e enjekte eder)
- **Persistence:** Approved dersler Qdrant `Lessons` koleksiyonuna yazılır

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

> 💡 Botun kendi hatalarından otomatik "öğrenmesi" riskli olabileceğinden (yanlış bir dersin
> kalıcı olarak prompt'a sızması), süreç **insan onayına bağlanmıştır** — `LessonStatus.Proposed`
> asla otomatik olarak `Approved`'a geçmez, mutlaka bir admin kararı gerekir.

## 6. Metotlar / Üyeler

### LessonStatus (enum)

| Değer | Anlamı |
|---|---|
| `Proposed` | `LessonMiner` tarafından önerildi, admin kararı bekleniyor |
| `Approved` | Admin onayladı, Qdrant'a yazılabilir |
| `Rejected` | Admin reddetti, context'e hiç girmez |

### Lesson

| Üye | Tip | Açıklama |
|---|---|---|
| `Id` | `string` | Benzersiz kimlik (varsayılan: yeni GUID) |
| `Title` | `string` | Kısa başlık — admin paneli listesinde görünür |
| `LessonText` | `string` | "X durumunda Y yapılmalı" biçiminde uygulanabilir kural |
| `Observation` | `string` | Hangi sorunun bu ders ile çözüldüğüne dair gözlem |
| `SuggestedAgent` | `string?` | Opsiyonel — hangi prompt/agent bu dersi uygulamalı önerisi |
| `SourceTraceIds` | `List<string>` | Bu dersi doğuran trace ID'leri (diagnoz için) |
| `Status` | `LessonStatus` | Varsayılan: `Proposed` |
| `DecidedBy` | `string?` | Kararı veren admin |
| `DecidedAt` | `DateTime?` | Karar anı |
| `DecisionReason` | `string?` | Karar gerekçesi |
| `CreatedAt` | `DateTime` | Oluşturulma anı (UTC) |
| `VectorMemoryId` | `string?` | Approve sonrası Qdrant'a upsert edildiğinde set edilir |

## 7. Bağımlılıklar

Yok — saf domain modeli, dış bağımlılığı yok.

## Bağlantılar

- [../Memory/MemoryDocument.md](../Memory/MemoryDocument.md) — Qdrant'a yazılan diğer bellek türü
