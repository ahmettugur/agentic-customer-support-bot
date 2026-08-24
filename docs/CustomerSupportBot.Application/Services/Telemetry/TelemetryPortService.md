# TelemetryPortService

- **Kaynak:** `Services/Telemetry/TelemetryPortService.cs`
- **Tür:** `public sealed class : ITelemetryPort`
- **Namespace:** `CustomerSupportBot.Application.Services.Telemetry`

## 1. Ne İşe Yarar

`ITelemetryPort` (Inbound port) implementasyonu — LLM kullanım maliyeti görünümünü admin
paneline sunan çok ince bir delegasyon katmanı.

## 2. Hangi Amaçla Kullanılır

Admin panelinin "maliyet" sekmesinin, `ICostUsageStorePort`/`ICostCalculatorPort` gibi
Adapters.Telemetry katmanı detaylarına değil, tek bir port sözleşmesine bağımlı olmasını
sağlamak.

## 3. Sorumlulukları

Üç metodu doğrudan alt bileşenlere delege etmek — kendi iş mantığı yok.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `ICostUsageStorePort` — biriken kullanım/maliyet anlık görüntüsü (Adapters.Telemetry'de
  implemente edilir, bkz. [CostUsageStore.md](../../../../CustomerSupportBot.Adapters.Telemetry/OpenTelemetry/CostUsageStore.md)).
- `ICostCalculatorPort` — bilinen model listesi (Adapters.Telemetry'de implemente edilir).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Hexagonal mimaride Api katmanının doğrudan Adapters.Telemetry'ye değil, Application
katmanındaki porta bağımlı olması gerekir — bu sınıf o ayrımı sağlayan ince adaptördür.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `GetCostSnapshot()` | `ICostUsageStorePort.GetUsageSnapshot()`'a delege eder. |
| `GetKnownModels()` | `ICostCalculatorPort.KnownModels`'a delege eder. |
| `ResetCostSnapshot()` | `ICostUsageStorePort.ResetUsage()`'a delege eder — admin panelinden sayaçları sıfırlama. |

## 7. Bağımlılıklar

Constructor injection ile: `ICostUsageStorePort`, `ICostCalculatorPort`.
