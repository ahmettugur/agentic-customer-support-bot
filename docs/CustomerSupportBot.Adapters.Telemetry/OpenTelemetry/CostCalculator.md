# CostCalculator

- **Kaynak:** `CustomerSupportBot.Adapters.Telemetry/OpenTelemetry/CostCalculator.cs`
- **Tür:** `public sealed class : ICostCalculatorPort`
- **Namespace:** `CustomerSupportBot.Adapters.Telemetry.OpenTelemetry`

## Ne işe yarar?

`CostCalculator`, Application katmanındaki [ICostCalculatorPort](../../CustomerSupportBot.Application/Ports/Outbound/Observability/ICostCalculatorPort.md) portunu uygulayan; [TelemetryOptions](../Options/TelemetryOptions.md) içerisindeki model fiyatlandırma tablosunu (`Pricing`) kullanarak tüketilen girdi ve çıktı token'ları üzerinden çağrının USD cinsinden maliyetini hesaplayan adaptördür.

## Hangi amaçla kullanılır`?

- LLM çağrılarının anlık tahmini maliyetini çıkarmak.
- Model adına özel fiyat (Örn: `gpt-4o-mini`, `o3-mini`), sağlayıcı varsayılanı (`openai_default`, `azureopenai_default`) veya global varsayılan (`default`) üzerinden kademeli eşleşme (fallback) sağlamak.

## Sorumlulukları

- **Üstlendiği:**
  - `ICostCalculatorPort.CalculateCost` sözleşmesini karşılamak.
  - Fiyat tablosundan bilinen modellerin listesini (`KnownModels`) sunmak.
  - `(inputTokens / 1000 * InputPer1K) + (outputTokens / 1000 * OutputPer1K)` formülüyle hesaplama yapmak.

## Constructor ve Başlatma Mantığı

```csharp
public CostCalculator(IOptions<TelemetryOptions> options)
```

### Constructor İçerisinde Yapılan İşler:
- `_options`: `options.Value` üzerinden fiyatlandırma kuralları saklanır.

## Metotlar ve İç Çalışma Mantıkları

### 1. `CalculateCost`
```csharp
public decimal CalculateCost(
    string modelHint,
    string provider,
    int inputTokens,
    int outputTokens)
```
- **Ne işe yarar?:** Verilen token sayılarına göre toplam dolar maliyetini hesaplar.
- **İç Mantığı:**
  1. Token sayıları `<= 0` ise `0m` döner.
  2. `_options.Pricing` içinde önce `modelHint`, yoksa `"{provider}_default"`, yoksa `"default"` anahtarı aranır.
  3. Eşleşen `ModelPricing` bulunamazsa `0m` döner.
  4. Formül uygulanarak hesaplanan `decimal` maliyet döndürülür.

## Bağımlılıklar

- [ICostCalculatorPort](../../CustomerSupportBot.Application/Ports/Outbound/Observability/ICostCalculatorPort.md)
- [TelemetryOptions](../Options/TelemetryOptions.md)
