# ITelemetryPort

**Dosya:** `Ports/Inbound/ITelemetryPort.cs`
**Tür:** `interface`
**Namespace:** `CustomerSupportBot.Application.Ports.Inbound`

## 1. Ne işe yarar?

LLM kullanım maliyeti (cost) endpoint'lerinin kullandığı primary port — o ana kadarki token/maliyet birikimini okumak ve sıfırlamak.

## 2. Hangi amaçla kullanılır?

Admin panelindeki "maliyet" sayfası, hangi modellerin ne kadar token tükettiğini ve toplam maliyeti göstermek için `GetCostSnapshot`'ı; sayaçları sıfırlamak için `ResetCostSnapshot`'ı çağırır.

## 3. Sorumlulukları

- **Üstlendiği:** Birikmiş maliyet verisini okuma/sıfırlama sözleşmesini sunmak.
- **Üstlenmediği:** Maliyetin nasıl hesaplandığı/biriktirildiği — bu `CostCalculator`/`CostUsageStore` (Adapters.Telemetry) içindedir.

## 4. Diğer katman/bileşenlerle ilişkileri

- Implementasyonu Adapters.Telemetry katmanındaki `CostUsageStore`'u sarar.
- Admin panelindeki telemetry/maliyet endpoint'i tüketicisidir.

## 5. Kullanılma nedeni ve tasarım yaklaşımı

Application katmanının Adapters.Telemetry'e (somut depoya) değil bu porta bağımlı olması, hexagonal mimarinin "iç katman dış katmana bağımlı olmaz" kuralına uyar — maliyet takibinin altyapısı (bellek içi, DB, harici servis) değişse bile bu port sabit kalır.

## 6. Metotlar / Üyeler

| Metot | Açıklama |
|---|---|
| `CostUsageSnapshot GetCostSnapshot()` | O ana kadarki toplam kullanım/maliyet özetini döner. |
| `IReadOnlyCollection<string> GetKnownModels()` | Takip edilen bilinen model adlarını döner. |
| `void ResetCostSnapshot()` | Sayaçları sıfırlar. |

## 7. Bağımlılıklar

`CustomerSupportBot.Application.Ports.Outbound.Observability.CostUsageSnapshot`.

## Bağlantılar

- [CostUsageStore](../../Adapters.Telemetry/OpenTelemetry/CostUsageStore.md)
