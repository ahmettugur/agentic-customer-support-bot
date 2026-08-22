# ReasoningTraceEntity

**Dosya:** `EfCore/Entities/Observability/ReasoningTraceEntity.cs`
**Şema/Tablo:** `observability.reasoning_traces`
**Configuration:** [ReasoningTraceConfiguration](../../Configurations/Observability/ReasoningTraceConfiguration.md)

## 1. Ne İşe Yarar

Bir kullanıcı turunun (tek bir soru-cevap döngüsünün) **tüm akıl yürütme sürecinin** kalıcı
kaydıdır — planlama, uzman ajan gerekçelendirmeleri, öz-eleştiri, araç çağrıları, ajan
ziyaretleri — hepsi bu tek satırda toplanır.

## 2. Hangi Amaçla Kullanılır

`WorkflowRunner`/`WorkflowTraceEventProcessor` (Adapters.Agents katmanı) bir tur boyunca bu
kaydı bellekte biriktirir (`ReasoningTrace` Domain modeli) ve tur tamamlandığında
(`Complete()`) tek seferde veritabanına yazar; "model bu turda neyi biliyordu, neden bu
kararı verdi" sorusunun cevabı için hem debug hem denetim amacıyla kullanılır.

## 3. Sorumlulukları

- **Üstlendiği:** Tur boyunca üretilen tüm ara akıl yürütme adımlarının JSON serileşmiş
  halini taşımak.
- **Üstlenmediği:** Bu adımların anlamlandırılması/gösterimi — admin panelindeki trace
  görüntüleyici bu JSON'ları deserialize edip okunabilir hale getirir.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`SessionId` üzerinden [SessionEntity](../Chat/SessionEntity.md)'ye mantıksal referans verir.
[LessonEntity.SourceTraceIdsJson](../Improvement/LessonEntity.md) bu entity'nin `TraceId`'lerine
referans verebilir.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

> 🐞 **"Update hot-path: in-memory mutate, DB sadece `Complete()`'te yazılır" ne anlama gelir:**
> Bir tur boyunca onlarca ara adım (her ajan ziyareti, her tool çağrısı) trace'e eklenir. Her
> ara adımda veritabanına yazmak (her adımda bir `UPDATE`) ciddi bir performans maliyeti
> olurdu. Bunun yerine trace nesnesi bellekte (Domain modeli olarak) mutate edilir, sadece tur
> tamamlandığında (başarı/hata/timeout fark etmeksizin) **tek bir** veritabanı yazması yapılır.
> Bunun bedeli: pod tur ortasında çökerse o turun trace'i kalıcı olarak kaybolur (bilinçli bir
> tradeoff — trace gözlemlenebilirlik içindir, tur sonucunun kendisi değil).

Neredeyse tüm alt-yapılar (`ReasoningJson`, `PlanningJson`, `SpecialistReasoningsJson` vb.)
`jsonb` olarak saklanır çünkü her biri kendi iç yapısına sahip karmaşık nesnelerdir (Domain
katmanındaki `ReasoningResult`, `PlanningResult` vb.) — ayrı tablolara normalize etmek bu
"yaz-bir-kere, oku-nadiren" erişim paterni için gereksiz karmaşıklık olurdu.

## 6. Metotlar / Üyeler

| Üye | Tip | Açıklama |
|---|---|---|
| `TraceId` | `string` | Birincil anahtar. |
| `SessionId` | `string` | İlgili oturum. |
| `UserQuery` | `string` | Kullanıcının bu turdaki sorusu. |
| `StartedAt` | `DateTime` | Turun başlangıç zamanı. |
| `CompletedAt` | `DateTime?` | Turun bitiş zamanı. |
| `TerminationReason` | `string?` | Tur neden sona erdi (tamamlandı/hata/timeout). |
| `FinalResponse` | `string?` | Modelin nihai cevabı. |
| `IterationCount` | `int` | Kaç iterasyon/döngü yapıldığı. |
| `Error` | `string?` | Hata mesajı (varsa). |
| `EstimatedTokens` | `long` | Tahmini toplam token kullanımı. |
| `FirstDraftResponse` | `string?` | Öz-eleştiriden önceki ilk taslak cevap. |
| `WasRevised` | `bool` | Öz-eleştiri sonucu cevap revize edildi mi. |
| `ReasoningJson` | `string?` | 1. aşama akıl yürütme sonucu, `jsonb`. |
| `PlanningJson` | `string?` | Orkestrasyon planı, `jsonb`. |
| `SpecialistReasoningsJson` | `string` | Uzman ajan gerekçelendirmeleri, `jsonb` dizi. |
| `FinalCritiqueJson` | `string?` | Öz-eleştiri sonucu, `jsonb`. |
| `AgentVisitsJson` | `string` | Hangi ajanların ziyaret edildiği, `jsonb` dizi. |
| `ToolCallsJson` | `string` | Yapılan tool çağrıları, `jsonb` dizi. |

## 7. Bağımlılıklar

Yok — saf veri sınıfı.

## Bağlantılar

- [ReasoningTraceConfiguration](../../Configurations/Observability/ReasoningTraceConfiguration.md)
- [SessionEntity](../Chat/SessionEntity.md), [LessonEntity](../Improvement/LessonEntity.md)
- [README](../README.md)
