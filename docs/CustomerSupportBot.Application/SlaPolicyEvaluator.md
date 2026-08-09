# SlaPolicyEvaluator

## Ne İşe Yarar
Approval ve escalation taleplerinin SLA sürelerini değerlendiren saf stateless evaluator'dır.

## Hangi Amaçla Kullanılır
`SlaGuardian` periyodik tarama servisinde, her approval/escalation için "uyarı mı, ihlal mi?" kararını vermek için çağrılır.

## Sorumlulukları
- Approval talebinin yaşını hesaplayıp uyarı/ihlal eşiklerini kontrol etmek.
- Escalation talebinin yaşını hesaplayıp uyarı/ihlal eşiklerini kontrol etmek.
- İhlal durumunda hangi aksiyonun uygulanacağını belirlemek (AutoReject, BoostPriority).
- Aynı event'in tekrar yayınlanmasını önlemek (`ISlaEventSink.LastEmittedAt` kontrolü).

## Diğer Katman ve Bileşenlerle İlişkileri
- **Kullanan sınıf**: `SlaGuardian` — periyodik taramada her talep için çağırır.
- **DI bağımlılığı yok** — static metotlar; state tutmaz.
- **İlişkili modeller**: `SlaEvent`, `ApprovalRequest`, `EscalationRequest`, `ApprovalSlaOptions`, `EscalationSlaOptions`.

## Kullanılma Nedeni ve Tasarım Yaklaşımı
Saf fonksiyonlar — I/O yok, test edilebilir. SLA eşikleri `ApprovalSlaOptions`/`EscalationSlaOptions` üzerinden konfigüre edilir. `ISlaEventSink.LastEmittedAt` ile daha önce yayınlanmış event tekrar oluşturulmaz.

## Metotlar / Üyeler

| Metot | Açıklama |
|-------|----------|
| `EvaluateApproval(request, options, sink, now)` | Approval talebinin SLA durumunu değerlendirir. |
| `EvaluateEscalation(request, options, sink, now)` | Escalation talebinin SLA durumunu değerlendirir. |

## Bağımlılıklar
- `ISlaEventSink` — Son event zamanı kontrolü.
