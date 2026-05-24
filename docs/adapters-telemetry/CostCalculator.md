# CostCalculator

**Dosya:** `OpenTelemetry/CostCalculator.cs`  
**Port:** `ICostCalculatorPort`  
**Yaşam döngüsü:** Singleton

LLM token sayısını USD maliyete çevirir. Fiyat tablosu `TelemetryOptions.Pricing`'den okunur.

---

## Arayüz

```csharp
public interface ICostCalculatorPort
{
    decimal CalculateCost(string modelHint, string provider, int inputTokens, int outputTokens);
    IReadOnlyCollection<string> KnownModels { get; }
}
```

---

## Hesaplama formülü

```
cost = (inputTokens / 1000) * pricing.InputPer1K
     + (outputTokens / 1000) * pricing.OutputPer1K
```

Tüm fiyatlar **1000 token başına USD**. Decimal kullanılır (float precision sorunlarını önlemek için).

---

## Pricing lookup öncelik sırası

```csharp
public decimal CalculateCost(string modelHint, string provider, int inputTokens, int outputTokens)
{
    if (inputTokens <= 0 && outputTokens <= 0) return 0m;

    TelemetryOptions.ModelPricing? pricing = null;

    if (!string.IsNullOrWhiteSpace(modelHint) && _options.Pricing.TryGetValue(modelHint, out var match))
        pricing = match;
    else if (!string.IsNullOrWhiteSpace(provider) && _options.Pricing.TryGetValue($"{provider}_default", out var providerDefault))
        pricing = providerDefault;
    else if (_options.Pricing.TryGetValue("default", out var fallback))
        pricing = fallback;

    if (pricing == null) return 0m;

    var inputCost  = (decimal)inputTokens / 1000m * pricing.InputPer1K;
    var outputCost = (decimal)outputTokens / 1000m * pricing.OutputPer1K;
    return inputCost + outputCost;
}
```

### Üç katman fallback

| Anahtar | Örnek | Ne zaman kullanılır |
|---|---|---|
| Model adı tam eşleşme | `"gpt-4o-mini"` | İdeal — model bilinen |
| Provider default | `"openai_default"` | Bilinmeyen OpenAI modeli |
| Global default | `"default"` | Tamamen bilinmeyen model |
| Hiçbiri yok | — | `0` döner (maliyet yok ama metric/span çalışır) |

### Örnek senaryo

```json
"Pricing": {
  "gpt-4o-mini":    { "InputPer1K": 0.00015, "OutputPer1K": 0.0006 },
  "openai_default": { "InputPer1K": 0.001,   "OutputPer1K": 0.002 },
  "default":        { "InputPer1K": 0.0005,  "OutputPer1K": 0.001 }
}
```

```csharp
CalculateCost("gpt-4o-mini", "openai", 1000, 500)
  → 1000/1000 * 0.00015 + 500/1000 * 0.0006
  → 0.00015 + 0.0003 = $0.00045

CalculateCost("gpt-5-future", "openai", 1000, 500)
  → "gpt-5-future" yok → "openai_default" var
  → 0.001 + 0.001 = $0.002

CalculateCost("unknown-model", "unknown-provider", 1000, 500)
  → Hiçbiri yok → "default"
  → 0.0005 + 0.0005 = $0.001

CalculateCost("anything", "anything", 0, 0)
  → 0m (early return)
```

---

## `KnownModels`

```csharp
public IReadOnlyCollection<string> KnownModels => _options.Pricing.Keys;
```

Pricing tablosundaki tüm key'ler. Admin paneli "hangi modeller fiyatlandırılmış?" listesi için kullanır:

```
GET /api/telemetry/known-models
→ ["gpt-4o-mini", "gpt-4o", "openai_default", "default"]
```

`TelemetryPortService.GetKnownModels` bu metodu çağırır.

---

## Decimal vs double

Para hesabında `decimal` kullanılır:

```csharp
var inputCost = (decimal)inputTokens / 1000m * pricing.InputPer1K;   // decimal
```

`double` ile yapılırsa kayma oluşur:

```csharp
0.1 + 0.2 == 0.3    // double: false (0.30000000000000004)
0.1m + 0.2m == 0.3m // decimal: true
```

Toplam maliyet çok küçük artırımlarla biriktiği için (her çağrı $0.0001 gibi) precision kritik.

OpenTelemetry counter `double` kullandığı için kaydederken cast yapılır:

```csharp
CostUsdCounter.Add((double)cost, ...);
```

Bu OK — counter aggregation'ı yaklaşık olmasında sakınca yok (Grafana grafiği için). Ama in-memory store ve DB kaydı `decimal` kalır.

---

## Thread safety

Singleton — birden fazla thread eş zamanlı `CalculateCost` çağırır. Sınıf **stateless** (sadece read-only `_options`'a dokunur) — race condition yok.

`TelemetryOptions` reload edilirse (.NET options snapshot) yeni instance gelir; mevcut çağrılar etkilenmez.

---

## Test edilebilirlik

```csharp
var options = Options.Create(new TelemetryOptions
{
    Pricing = new(StringComparer.OrdinalIgnoreCase)
    {
        ["test-model"] = new() { InputPer1K = 0.01m, OutputPer1K = 0.02m }
    }
});
var calc = new CostCalculator(options);

Assert.Equal(0.03m, calc.CalculateCost("test-model", "test", 1000, 1000));
Assert.Equal(0m,    calc.CalculateCost("unknown", "unknown", 1000, 1000));   // default yok
```
