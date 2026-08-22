# CostModelUsageSnapshot

- **Kaynak:** `CustomerSupportBot.Application/Ports/Outbound/Observability/ICostUsageStorePort.cs`
- **Tür:** `public sealed record`
- **Namespace:** `CustomerSupportBot.Application.Ports.Outbound.Observability`

## Ne işe yarar?

`CostModelUsageSnapshot`, <summary> Toplam token/maliyet sayaçlarını okuyan secondary port. </summary>

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`CostModelUsageSnapshot`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Metotlar ve İç Çalışma Mantıkları

### `CostUsageSnapshot`
```csharp
public sealed record CostUsageSnapshot(
    long TotalCalls,
    long TotalInputTokens,
    long TotalOutputTokens,
    decimal TotalCostUsd,
    IReadOnlyList<CostModelUsageSnapshot> ByModel)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
