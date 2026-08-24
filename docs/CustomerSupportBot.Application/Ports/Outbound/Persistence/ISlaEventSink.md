# ISlaEventSink

**Kaynak:** `Ports/Outbound/Persistence/ISlaEventSink.cs`
**İmplementasyonlar:** [`InMemorySlaEventSink`](../../../../CustomerSupportBot.Adapters.Persistence/InMemory/InMemorySlaEventSink.md), [`PostgresSlaEventSink`](../../../../CustomerSupportBot.Adapters.Persistence/Postgres/PostgresSlaEventSink.md)

## 1. Ne İşe Yarar

SLA Guardian'ın ürettiği uyarı (`warn`) ve ihlal (`breach`) olaylarının kaydı için secondary
port.

## 2. Hangi Amaçla Kullanılır

`SlaGuardianService` (Api katmanı, periyodik worker) bekleyen eskalasyon/onay kayıtlarını
tarar; bir eşik aşıldığında `Record` ile event yazar. Admin dashboard `GetRecent` ile bu
olayları listeler, `EventRecorded` event'i canlı bildirim için dinlenir.

## 3. Sorumlulukları

- **Üstlendiği:** SLA event kaydı, tekrar-yayın önleme sorgusu (`LastEmittedAt`).
- **Üstlenmediği:** SLA eşiklerinin hesaplanması — bu `SlaGuardianService`'in işi; bu port
  yalnızca sonucu saklar.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`InMemorySlaEventSink` (test) ve `PostgresSlaEventSink` (prod) implemente eder.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

`LastEmittedAt(kind, targetId, severity)` var olma nedeni: `SlaGuardianService` periyodik
çalışır (örn. her 30 saniyede bir tarar) ve aynı ihlal her tarama turunda yeniden tespit
edilebilir — bu metot olmadan her tarama turunda aynı ihlal için tekrar tekrar event
üretilir, admin bildirim listesi aynı uyarıyla dolar. Bu sorgu "bu hedef+önem derecesi için
en son ne zaman uyardık" bilgisini vererek tekrar-yayını önler.

## 6. Metotlar / Üyeler

| Metot | Açıklama |
|---|---|
| `void Record(SlaEvent evt)` | Yeni SLA olayı kaydeder ve event yayar. |
| `IReadOnlyList<SlaEvent> GetRecent(int count = 100)` | Son N olay. |
| `DateTime? LastEmittedAt(string kind, string targetId, string severity)` | Belirli hedef+önem derecesi için en son ne zaman event yayınlandı. |
| `event EventHandler<SlaEvent>? EventRecorded` | Yeni event eklendiğinde fırlar (UI canlı bildirim). |

## 7. Bağımlılıklar

Port arayüzü `CustomerSupportBot.Domain.Model.SlaEvent`'e bağımlıdır.
