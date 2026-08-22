# TelemetryPortService

- **Kaynak:** `CustomerSupportBot.Application/Services/Telemetry/TelemetryPortService.cs`
- **Tür:** `public sealed class : ITelemetryPort`
- **Namespace:** `CustomerSupportBot.Application.Services.Telemetry`

## Ne işe yarar?

`TelemetryPortService`, Application/Services/TelemetryPortService.cs DRIVING PORT IMPL — ITelemetryPort → telemetry maliyet görünümü.

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`TelemetryPortService`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

```csharp
public TelemetryPortService(ICostUsageStorePort usageStore, ICostCalculatorPort calculator)
```
- **Parametreler ve Başlatma:** Alınan servis bağımlılıkları (`readonly` alanlara) atanır ve gerekli başlatma kontrolleri yapılır.

## Metotlar ve İç Çalışma Mantıkları

### `GetCostSnapshot`
```csharp
public CostUsageSnapshot GetCostSnapshot()
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `GetKnownModels`
```csharp
public IReadOnlyCollection<string> GetKnownModels()
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `ResetCostSnapshot`
```csharp
public void ResetCostSnapshot()
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
- `ITelemetryPort`
