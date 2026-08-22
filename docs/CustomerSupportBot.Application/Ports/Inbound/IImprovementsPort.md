# IImprovementsPort

**Dosya:** `Ports/Inbound/IImprovementsPort.cs`
**Tür:** `interface`
**Namespace:** `CustomerSupportBot.Application.Ports.Inbound`

## 1. Ne işe yarar?

"Self-improving loop" (kendi kendini geliştirme döngüsü) özelliğinin admin tarafı için primary port — geçmiş konuşmalardan çıkarılan "ders"leri (Lesson) listeler, admin onay/red kararını uygular.

## 2. Hangi amaçla kullanılır?

Bir arka plan işi (mining) geçmiş konuşmaları tarayıp iyileştirme fırsatlarını (`Lesson`) tespit eder; admin panelinde bu dersler listelenir ve admin onaylarsa (muhtemelen prompt/davranış güncellemesine girdi olarak) kullanılır.

## 3. Sorumlulukları

- **Üstlendiği:** Mining sürecini tetiklemek, ders listesini sunmak, onay/red kararını uygulamak.
- **Üstlenmediği:** Onaylanan dersin nasıl uygulanacağı (prompt güncelleme vb.) — bu port sadece karar sürecini kapsar.

## 4. Diğer katman/bileşenlerle ilişkileri

- Implementasyonu `Services/Improvement` altında.
- `Lesson`, `LessonStatus`, `MiningRunReport` tiplerini kullanır (Domain.Model.Improvement / Services.Improvement).
- Admin panelindeki "improvements" sayfası tüketicisidir.

## 5. Kullanılma nedeni ve tasarım yaklaşımı

Otomatik üretilen "ders"lerin doğrudan uygulanmaması, admin onayından geçmesi (`ApproveAsync`/`Reject`) bilinçli bir HITL tasarımıdır — LLM'in çıkardığı bir gözlemin yanlış/aşırı genelleyici olma riskine karşı insan denetimi katmanı ekler.

## 6. Metotlar / Üyeler

| Metot | Açıklama |
|---|---|
| `Task<MiningRunReport> MineAsync(CancellationToken ct = default)` | Ders madenciliği sürecini tetikler ve raporunu döner. |
| `IReadOnlyList<Lesson> GetLessons(LessonStatus? status = null)` | Dersleri (isteğe bağlı durum filtresiyle) listeler. |
| `Lesson? GetLesson(string id)` | Tek ders. |
| `Task<bool> ApproveAsync(string id, string decidedBy, string? reason, CancellationToken ct = default)` | Dersi onaylar. |
| `bool Reject(string id, string decidedBy, string? reason)` | Dersi reddeder. |

## 7. Bağımlılıklar

`CustomerSupportBot.Application.Services.Improvement.MiningRunReport`, `CustomerSupportBot.Domain.Model.Improvement.Lesson`.

## Bağlantılar

- [Lesson](../../Domain/Model/Improvement/Lesson.md)
