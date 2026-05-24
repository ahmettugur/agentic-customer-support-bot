# TelemetryPortService

**Dosya:** `Services/TelemetryPortService.cs`  
**Implements:** `ITelemetryPort`  
**Yaşam döngüsü:** Singleton

## Ne yapar?

LLM çağrı maliyetlerini ve kullanım istatistiklerini API katmanına sunar. Çok ince bir delegator servis; iş mantığı `ICostUsageStorePort` ve `ICostCalculatorPort` adaptörlerindedir.

---

## Constructor bağımlılıkları

| Bağımlılık | Tür | Açıklama |
|-----------|-----|---------|
| `ICostUsageStorePort` | Driven port | In-memory kullanım özeti (Adapters.Telemetry) |
| `ICostCalculatorPort` | Driven port | Model bazlı fiyat hesaplama (Adapters.AI) |

---

## Metodlar

### `GetCostSnapshot`

```csharp
CostUsageSnapshot GetCostSnapshot()
```

Anlık kullanım ve maliyet özetini döner.

**`CostUsageSnapshot` alanları:**

| Alan | Açıklama |
|------|---------|
| `TotalInputTokens` | Toplam gönderilen token |
| `TotalOutputTokens` | Toplam alınan token |
| `TotalCostUsd` | Toplam tahmin maliyet (USD) |
| `CallCount` | Toplam LLM çağrı sayısı |
| `ByModel` | Model başına detay (`ModelUsage[]`) |
| `LastResetAt` | Son sıfırlama zamanı |

---

### `GetKnownModels`

```csharp
IReadOnlyCollection<string> GetKnownModels()
```

`ICostCalculatorPort`'un fiyat tablosunda tanımlı model ID'lerini döner. Kullanılan modelin bu listede olup olmadığını kontrol etmek için kullanılır.

---

### `ResetCostSnapshot`

```csharp
void ResetCostSnapshot()
```

In-memory maliyet sayaçlarını sıfırlar. Yeni bir test veya periyot başlangıcında çağrılır.

---

## API endpoint'leri

```http
GET  /telemetry/cost          → GetCostSnapshot
GET  /telemetry/models        → GetKnownModels
POST /telemetry/cost/reset    → ResetCostSnapshot
```

---

## Tasarım notu

`TelemetryPortService` kasıtlı olarak minimal tutulmuştur. LLM çağrılarının maliyeti her `RunAsync` / `ReasonAsync` çağrısından sonra `ICostUsageStorePort.RecordUsage` çağrısıyla otomatik kaydedilir. Bu kayıt `Adapters.AI` katmanında gerçekleşir; `TelemetryPortService` sadece okuma sağlar.
