# SlaPolicyEvaluator

- **Kaynak:** `CustomerSupportBot.Application/Services/Sla/SlaPolicyEvaluator.cs`
- **Tür:** `public static class`
- **Namespace:** `CustomerSupportBot.Application.Services.Sla`

## Ne işe yarar?

`SlaPolicyEvaluator`, Application katmanında ilgili iş akışını ve domain kurallarını yürüten temel bileşendir.

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`SlaPolicyEvaluator`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Metotlar ve İç Çalışma Mantıkları

### `ApprovalEvaluation`
```csharp
public record ApprovalEvaluation(
        ApprovalRequest Request,
        SlaEvent? WarnEvent,
        SlaEvent? BreachEvent,
        SlaBreachAction BreachAction)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `EscalationEvaluation`
```csharp
public record EscalationEvaluation(
        EscalationRequest Request,
        SlaEvent? WarnEvent,
        SlaEvent? BreachEvent,
        EscalationPriority? NewPriority)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `EvaluateApproval`
```csharp
public static ApprovalEvaluation EvaluateApproval(
        ApprovalRequest request,
        ApprovalSlaOptions options,
        ISlaEventSink sink,
        DateTime now)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `EvaluateEscalation`
```csharp
public static EscalationEvaluation EvaluateEscalation(
        EscalationRequest request,
        EscalationSlaOptions options,
        ISlaEventSink sink,
        DateTime now)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `BoostPriority`
```csharp
public static EscalationPriority BoostPriority(EscalationPriority current)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
