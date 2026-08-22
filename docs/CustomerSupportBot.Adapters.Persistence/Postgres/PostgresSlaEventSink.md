# PostgresSlaEventSink

**Dosya:** `Postgres/PostgresSlaEventSink.cs`
**Namespace:** `CustomerSupportBot.Adapters.Persistence.Postgres`
**Port:** [`ISlaEventSink`](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/ISlaEventSink.md)

## 1. Ne İşe Yarar

Yanıt gecikmesi, eskalasyon süresi gibi SLA (hizmet seviyesi) olaylarını (`SlaEvent`) kaydeden, hibrit cache tabanlı bir sink. Aynı türde tekrarlayan uyarıların ne zaman son gönderildiğini de (`LastEmittedAt`) izler.

## 2. Hangi Amaçla Kullanılır

`SlaGuardianService` (Api katmanı, `IHostedService`) periyodik taramada bir eşik aşıldığını tespit ettiğinde `Record` çağırır; aynı servis, aynı uyarıyı kısa aralıklarla tekrar tekrar göndermemek için önce `LastEmittedAt`'e bakar.

## 3. Sorumlulukları

- Üstlendiği: olay kaydı, "bu tür+hedef+önem derecesi kombinasyonu en son ne zaman tetiklendi" sorgusu, cache senkronu.
- Üstlenmediği: eşik tanımları ve tarama döngüsü (bu [`SlaGuardianService`](../../CustomerSupportBot.Api/Workers/SlaGuardianService.md)'in işi).

## 4. İlişkiler

- `ISlaEventSink` portunu implemente eder.
- `IMessageBusPort`, `IDbContextFactory<CustomerSupportDbContext>` enjekte edilir.
- `EventRecorded` event'i ile dinleyicilere (örn. admin bildirim akışı) anlık haber verir.

## 5. Tasarım Yaklaşımı

`LastEmittedAt(kind, targetId, severity)`, `SlaGuardianService`'in "aynı uyarıyı spam etme" mantığının temelidir — sink kendisi rate-limit KARARI vermez, yalnızca "en son ne zamandı" bilgisini sağlar; karar çağıran tarafta kalır (tek sorumluluk ayrımı).

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `void Record(SlaEvent evt)` | Cache + DB + Redis yayını + `EventRecorded` event'i. |
| `IReadOnlyList<SlaEvent> GetRecent(int count = 100)` | Cache'ten en son N olay. |
| `DateTime? LastEmittedAt(string kind, string targetId, string severity)` | Aynı (tür, hedef, önem derecesi) kombinasyonunun en son tetiklendiği zaman; hiç yoksa `null`. |
| `event EventHandler<SlaEvent>? EventRecorded` | Yeni olay kaydında tetiklenir. |

## 7. Bağımlılıklar

- `IDbContextFactory<CustomerSupportDbContext>`
- `IMessageBusPort`
- `ILogger<PostgresSlaEventSink>`

## Bağlantılar

- [ISlaEventSink](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/ISlaEventSink.md)
- [SlaGuardianService](../../CustomerSupportBot.Api/Workers/SlaGuardianService.md)
