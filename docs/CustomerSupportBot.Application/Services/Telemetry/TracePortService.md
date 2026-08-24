# TracePortService

- **Kaynak:** `Services/Telemetry/TracePortService.cs`
- **Tür:** `public sealed class : ITracePort`
- **Namespace:** `CustomerSupportBot.Application.Services.Telemetry`

## 1. Ne İşe Yarar

`ITracePort` (Inbound port) implementasyonu — admin panelinin "reasoning trace" görünümünü
besler: hangi oturumda hangi turlarda ne kadar sürede, kaç iterasyonla, hangi sonlanma
sebebiyle reasoning çalıştığını gösterir.

## 2. Hangi Amaçla Kullanılır

Geliştiricinin/admin'in "bu turda model neden böyle davrandı" sorusuna, `ReasoningTrace`
kayıtları üzerinden cevap bulabilmesi için. `GetSessionsSummaryAsync` ve `GetStats`, ham
trace listesinden panele hazır özet üretir.

## 3. Sorumlulukları

**Üstlendiği:**
- Ham trace sorguları (`GetRecentTraces`, `GetTrace`, `GetTracesBySession`) — doğrudan
  `IReasoningTraceStore`'a delege.
- `GetSessionsSummaryAsync` — trace'leri `SessionId`'ye göre gruplayıp her oturum için bir
  özet satırı (başlık, trace sayısı, son trace zamanı) üretmek; gerçek mesaj sayısını almak
  için `ISessionManager.GetHistoryAsync`'i de çağırmak.
- `GetStats` — son 500 trace üzerinden toplam/tamamlanan/hatalı sayıları, ortalama süre,
  ortalama iterasyon sayısı ve sonlanma sebebi dağılımını hesaplamak.

**Üstlenmediği:** Trace'lerin yazılması (workflow çalışırken `IReasoningTraceStore`'a
doğrudan yazılır — bu servis yalnızca **okuma** tarafıdır).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `IReasoningTraceStore` — trace kayıtlarının kaynağı.
- `ISessionManager` — oturum başına gerçek mesaj sayısı (`GetSessionsSummaryAsync` içinde).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

### `GetSessionsSummaryAsync`'te başlık nasıl üretiliyor

Bir oturumun **en eski** trace'inin `UserQuery`'si (`traces.LastOrDefault()` — trace'ler
`StartedAt`'a göre azalan sıralı olduğundan liste sonu en eski) başlık olarak kullanılır, 60
karakterde kırpılır. Böylece admin paneli "bu oturum neyle başladı" sorusuna liste görünümünde
bile cevap verebilir.

### İstatistikler neden yalnızca "son 500 trace" üzerinden

`GetStats` ve `GetSessionsSummaryAsync`, tüm trace geçmişini değil `GetRecent(500)`'ü kullanır
— sınırsız bir agregasyon, trace sayısı büyüdükçe admin panelinin yanıt süresini öngörülemez
hale getirirdi; 500 son trace, "yakın zamanda ne oluyor" sorusuna cevap için yeterli bir
pencere.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `GetRecentTraces(count = 20)` | `IReasoningTraceStore.GetRecent`'e delege eder. |
| `GetTrace(traceId)` | Tek bir trace'i ID ile döner (`IReasoningTraceStore.Get`). |
| `GetTracesBySession(sessionId)` | Bir oturumun tüm trace'lerini döner. |
| `GetSessionsSummaryAsync(ct)` | Son 500 trace'i `SessionId`'ye göre gruplar; her grup için başlık (en eski sorgunun kırpılmışı), trace sayısı, son trace zamanı ve gerçek mesaj sayısını (`ISessionManager.GetHistoryAsync`) içeren `TracedSessionSummary` üretir; sonucu en son aktiviteye göre sıralar. |
| `GetStats()` | Son 500 trace üzerinden toplam/tamamlanan/hatalı sayı, ortalama süre (`DurationMs`), ortalama iterasyon sayısı ve `TerminationReason` dağılımını içeren `TraceStatsSummary` üretir; hiç trace yoksa sıfırlanmış bir özet döner. |

## 7. Bağımlılıklar

Constructor injection ile: `IReasoningTraceStore`, `ISessionManager`.

## Bağlantılar

- [../Reasoning/ReasoningService.md](../Reasoning/ReasoningService.md) — trace'lerin kaynağı olan reasoning akışı
