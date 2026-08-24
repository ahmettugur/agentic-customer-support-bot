# SlaPolicyEvaluator

- **Kaynak:** `Services/Sla/SlaPolicyEvaluator.cs`
- **Tür:** `public static class`
- **Namespace:** `CustomerSupportBot.Application.Services.Sla`

## 1. Ne İşe Yarar

Tek bir onay veya eskalasyon kaydının **yaşını** eşiklerle karşılaştırıp bir `warn`/`breach`
`SlaEvent`'i üretilip üretilmeyeceğine karar veren, tamamen saf/statik bir değerlendirme
fonksiyonu kümesi. Kendi başına hiçbir I/O yapmaz, hiçbir kaydı taramaz — tek bir kaydı
değerlendirir.

## 2. Hangi Amaçla Kullanılır

[`SlaPortService.ScanOnceAsync`](SlaPortService.md)'in periyodik taramasında, her bekleyen
onay/açık eskalasyon için çağrılır. Karar mantığının taramadan **ayrı** tutulması, mantığı
I/O'suz ve birim testi kolay hale getirir.

## 3. Sorumlulukları

**Üstlendiği:** Yaş hesaplama, eşik karşılaştırması, aynı event'in **tekrar yayınlanmamasını**
(`sink.LastEmittedAt` kontrolü) sağlamak, eskalasyon breach'inde öncelik yükseltme kararı.

**Üstlenmediği:** Event'lerin kalıcı olarak kaydedilmesi (`ISlaEventSink.Record`'un işi —
çağıran taraf, `SlaPortService`, yapar), breach sonrası otomatik red/onay uygulaması (yine
`SlaPortService`'in işi).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `ISlaEventSink.LastEmittedAt` — aynı kayıt için aynı severity'de event'in daha önce
  yayınlanıp yayınlanmadığını sorgular (idempotency).
- Girdi tipleri: `ApprovalRequest`, `EscalationRequest`, `ApprovalSlaOptions`,
  `EscalationSlaOptions` (bkz. [SlaOptions.md](SlaOptions.md)).
- Tüketicisi: [`SlaPortService`](SlaPortService.md).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

### Neden `sink.LastEmittedAt` ile idempotency kontrolü

Tarama periyodiktir (`PollIntervalSeconds`, varsayılan 5sn) — aynı bekleyen kayıt, ihlal
eşiğini aştıktan sonra her taramada tekrar tekrar kontrol edilir. `LastEmittedAt(kind, id,
severity)` `null` değilse (aynı severity için zaten bir event var), yeni bir event **tekrar
üretilmez** — aksi halde admin paneli aynı ihlal için saniyede bir yeni bildirim alırdı.

### `warn`/`breach` neden birbirini dışlıyor (`if`/`else if`)

Bir kayıt hem `breach` eşiğini hem `warn` eşiğini aşmışsa yalnızca `breach` değerlendirilir
(`if (age >= BreachAfterSeconds) ... else if (age >= WarnAfterSeconds) ...`) — `warn`,
`breach`'in bir ön-aşaması olduğundan, ihlal zaten gerçekleşmişken ayrıca bir uyarı üretmenin
anlamı yoktur.

### `EvaluateEscalation`'da öncelik yükseltme neden burada hesaplanır ama uygulanmaz

`BoostPriority(request.Priority)` çağrılıp `EscalationEvaluation.NewPriority`'ye konur, ama
`request.Priority`'nin kendisi **burada değiştirilmez** — bu metot saf bir değerlendiricidir,
yan etkisi olmamalı. Gerçek atama `SlaPortService.ScanOnceAsync` içinde yapılır.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `KindApproval`, `KindEscalation`, `SeverityWarn`, `SeverityBreach` *(const string)* | `SlaEvent.Kind`/`Severity` alanlarında kullanılan sabit string değerler. |
| `ApprovalEvaluation(Request, WarnEvent, BreachEvent, BreachAction)` *(record)* | `EvaluateApproval`'ın sonucu. |
| `EscalationEvaluation(Request, WarnEvent, BreachEvent, NewPriority)` *(record)* | `EvaluateEscalation`'ın sonucu. |
| `EvaluateApproval(request, options, sink, now)` *(static)* | `now - request.RequestedAt` yaşını eşiklerle karşılaştırır; breach ise `options.OnBreach`'i `BreachAction` olarak taşır. |
| `EvaluateEscalation(request, options, sink, now)` *(static)* | `now - request.CreatedAt` yaşını eşiklerle karşılaştırır; breach ve `BoostPriorityOnBreach=true` ise `BoostPriority` ile yeni önceliği hesaplar. |
| `BoostPriority(current)` *(static)* | `Low→Normal→High→Critical`, `Critical` sabit kalır, tanımsız değer `Normal`'a döner. |

## 7. Bağımlılıklar

Statik bir sınıf olduğu için kendi bağımlılığı yoktur; metotları çağıran taraftan
`ISlaEventSink`'i parametre olarak alır.

## Bağlantılar

- [SlaOptions.md](SlaOptions.md) — eşik değerleri
- [SlaPortService.md](SlaPortService.md) — tüketici
