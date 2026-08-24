# ImprovementsPortService

**Dosya:** `Services/Improvement/ImprovementsPortService.cs`
**Port:** `IImprovementsPort` (driving/inbound port)
**Namespace:** `CustomerSupportBot.Application.Services.Improvement`

## 1. Ne İşe Yarar

[`LessonMiner`](LessonMiner.md) ve `ILessonStore`'u `IImprovementsPort` sözleşmesine bağlayan
ince bir orkestrasyon katmanı — admin panelinin "self-improvement" (kendi kendine iyileşme)
ekranının konuştuğu tek kapı.

## 2. Hangi Amaçla Kullanılır

Api katmanındaki admin panel: tarama tetiklemek (`MineAsync`), ders önerilerini listelemek
(`GetLessons`), tekil ders görüntülemek (`GetLesson`), onaylamak/reddetmek (`ApproveAsync`/`Reject`).

## 3. Sorumlulukları

- **Üstlendiği:** `LessonMiner` ve `ILessonStore` çağrılarını `IImprovementsPort` arayüzüne
  yönlendirmek; statü filtresine göre listeleme kararını (`GetByStatus` vs `GetAll`) vermek.
- **Üstlenmediği:** Ders üretiminin/analiz mantığının kendisi (tamamı `LessonMiner`'da).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `IImprovementsPort` port'unu implemente eder.
- **Inject eder:** `LessonMiner`, `ILessonStore`.
- **Kimin tarafından çağrılır:** Api katmanındaki admin panel (self-improvement) endpoint'leri.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Bu sınıf kasıtlı olarak minimum mantık içerir — her metot tek satırlık bir delegasyondur.
Var oluş nedeni saf hexagonal mimari ayrımıdır: `LessonMiner` somut bir Application
servisidir (LLM çağrısı, prompt oluşturma gibi altyapıya yakın detaylar içerir), `IImprovementsPort`
ise Api katmanının gördüğü soyut use-case arayüzüdür. Bu servis ikisi arasındaki köprüdür —
Api katmanı `LessonMiner`'ı doğrudan görmez/inject etmez, yalnızca `IImprovementsPort`'u görür.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `MineAsync(CancellationToken ct = default): Task<MiningRunReport>` | Taramayı tetikler, `LessonMiner.MineAsync`'e delege eder. |
| `GetLessons(LessonStatus? status = null): IReadOnlyList<Lesson>` | Statü belirtilmişse filtreli, değilse tüm dersleri döner. |
| `GetLesson(string id): Lesson?` | Tekil ders getirir. |
| `ApproveAsync(string id, string decidedBy, string? reason, CancellationToken ct = default): Task<bool>` | Dersi onaylar (vektör yazımı dahil), `LessonMiner.ApproveAsync`'e delege eder. |
| `Reject(string id, string decidedBy, string? reason): bool` | Dersi reddeder, `LessonMiner.Reject`'e delege eder. |

## 7. Bağımlılıklar (Constructor Injection)

- `LessonMiner` — ders üretim/onay/red mantığı.
- `ILessonStore` — ders listeleme sorguları.

## Bağlantılar

- [LessonMiner.md](LessonMiner.md) — asıl analiz/üretim mantığı
