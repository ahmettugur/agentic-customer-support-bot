# TracedSessionSummary

- **Kaynak:** `CustomerSupportBot.Application/Ports/Inbound/ITracePort.cs`
- **Tür:** `public sealed record`
- **Namespace:** `CustomerSupportBot.Application.Ports.Inbound`

## Ne işe yarar?

`TracedSessionSummary`, <summary> Reasoning trace okuma ve istatistik için primary (driving) port. </summary>

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`TracedSessionSummary`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Metotlar ve İç Çalışma Mantıkları

### `TraceStatsSummary`
```csharp
public sealed record TraceStatsSummary(
    int TotalTraces,
    int CompletedCount,
    int ErrorCount,
    double AvgDurationMs,
    double AvgIterationCount,
    IReadOnlyDictionary<string, int> TerminationReasons)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
