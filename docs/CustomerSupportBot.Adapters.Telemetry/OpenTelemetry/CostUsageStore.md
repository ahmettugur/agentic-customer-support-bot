# CostUsageStore

- **Kaynak:** `CustomerSupportBot.Adapters.Telemetry/OpenTelemetry/CostUsageStore.cs`
- **Tür:** `public sealed class : ICostUsageStorePort`
- **Namespace:** `CustomerSupportBot.Adapters.Telemetry.OpenTelemetry`

## Ne işe yarar?

`CostUsageStore`, Application katmanındaki [ICostUsageStorePort](../../CustomerSupportBot.Application/Ports/Outbound/Observability/ICostUsageStorePort.md) portunu uygulayan; uygulama çalışırken gerçekleşen tüm LLM çağrılarının token harcamalarını, kümülatif USD maliyetlerini, çağrı adetlerini ve hareketli ortalama gecikme sürelerini (`AverageLatencyMs`) model bazında thread-safe olarak bellek içinde (`ConcurrentDictionary`) toplayan kullanım ambarıdır.

## Hangi amaçla kullanılır`?

- Yönetim paneli (`/api/telemetry/cost`) üzerinden sistemin toplam maliyet ve token tüketim istatistiklerini anlık olarak sunmak (`GetSnapshot`, `GetUsageSnapshot`).
- Her model için çağrı sayısı, ortalama gecikme ve son kullanım zamanını takip etmek.
- İstenildiğinde istatistikleri sıfırlayabilmek (`ResetUsage`).

## Sorumlulukları

- **Üstlendiği:**
  - `Record` ile yeni bir LLM çağrısının ölçümlerini eklemek/güncellemek.
  - Model bazında çalışan hareketli ortalama gecikmeyi (`running mean`) hesaplamak.
  - `GetSnapshot` ve `GetUsageSnapshot` ile kümülatif rapor sunmak.

## Metotlar ve İç Çalışma Mantıkları

### 1. `Record`
```csharp
public void Record(
    string model,
    long inputTokens,
    long outputTokens,
    decimal costUsd,
    double durationMs)
```
- **Ne işe yarar?:** Bir LLM çağrısının tüketim verilerini ambar kaydına ekler.
- **İç Mantığı:**
  1. `_byModel.AddOrUpdate` kullanılarak model kaydı varsa atomik kilit altında güncellenir; ortalama süre `existing.AverageLatencyMs + (durationMs - existing.AverageLatencyMs) / existing.Calls` formülüyle güncellenir.
  2. `_lock` altında toplam değişkenler (`_totalInputTokens`, `_totalOutputTokens`, `_totalCost`, `_totalCalls`) artırılır.

### 2. `GetSnapshot` & `GetUsageSnapshot`
- **Ne işe yarar?:** Toplam çağrı, token, maliyet ve model kırılımlarını içeren anlık görüntü döner.
- **İç Mantığı:** Modeller maliyete göre azalan sırada (`OrderByDescending(m => m.CostUsd)`) sıralanır ve yuvarlanmış değerlerle döndürülür.

### 3. `ResetUsage`
- **Ne işe yarar?:** Tüm sayaçları ve model sözlüğünü temizler.

## Bağımlılıklar

- [ICostUsageStorePort](../../CustomerSupportBot.Application/Ports/Outbound/Observability/ICostUsageStorePort.md)
- `System.Collections.Concurrent.ConcurrentDictionary`

## Aynı dosyada tanımlı yardımcı tipler

Bu üç sınıf ayrı dosya açılacak kadar bağımsız değil — sadece bu sınıfın döndürdüğü salt-veri (DTO) sonuçlarıdır, bu yüzden ayrı `.md` dosyası yerine burada belgeleniyor:

- **`ModelUsage`** — tek bir model için birikmiş kullanım: `Model`, `Calls`, `InputTokens`, `OutputTokens`, `CostUsd`, `AverageLatencyMs`, `LastUsed`.
- **`CostSnapshot`** — `GetSnapshot()`'ın döndürdüğü tüm sistem anlık görüntüsü: toplamlar + `List<ModelUsage> ByModel`.
- **`CostUsageSnapshot`** / **`CostModelUsageSnapshot`** — Application katmanında [ICostUsageStorePort](../../CustomerSupportBot.Application/Ports/Outbound/Observability/ICostUsageStorePort.md) sözleşmesinde tanımlı, port-bağımsız (Telemetry adaptörüne bağımlı olmayan) eşdeğer kayıtlar; `GetUsageSnapshot()` bu tiplere dönüştürerek döner. `CostSnapshot`/`ModelUsage` bu Telemetry adaptörüne özgüyken, `CostUsageSnapshot`/`CostModelUsageSnapshot` Application katmanının portunda tanımlıdır — böylece Application, Telemetry adaptörünün somut tiplerine bağımlı olmaz.
