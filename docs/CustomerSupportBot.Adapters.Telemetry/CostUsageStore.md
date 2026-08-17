# CostUsageStore

**Dosya:** `OpenTelemetry/CostUsageStore.cs`  
**Port:** `ICostUsageStorePort`  
**Yaşam döngüsü:** Singleton

In-memory toplam LLM kullanım istatistikleri. OpenTelemetry counter'lara **paralel** olarak çalışır — admin paneli için "anlık snapshot" verir.

---

## Neden in-memory store?

OpenTelemetry zaten counter tutar; neden ikinci store?

| OpenTelemetry | CostUsageStore |
|---|---|
| Production'da Grafana/Prometheus ile sorgulanır | Process içinden direkt okunur (zero-latency) |
| Aggregation window'lı (1m, 5m bucket'lar) | Snapshot anlık + total |
| Setup gerektirir (collector, Prometheus, dashboard) | DI ile hazır gelir |

**Demo/admin paneli için pratik:** Production observability altyapısı kurulmadan da `/api/telemetry/cost` çalışır.

Production'da tercihen `CostUsageStore` kaldırılıp Prometheus query API'sı kullanılır — ama bu projede "her şey hazır" demo değeri için tutulur.

---

## Veri yapısı

```csharp
private readonly ConcurrentDictionary<string, ModelUsage> _byModel =
    new(StringComparer.OrdinalIgnoreCase);

private readonly object _lock = new();
private long _totalInputTokens;
private long _totalOutputTokens;
private decimal _totalCost;
private long _totalCalls;
```

İki paralel toplam:
- `_byModel`: Her model için detaylı (calls, tokens, cost, avg latency, last used)
- `_total*`: Global toplamlar (lock-protected scalar field'lar)

---

## `Record`

```csharp
public void Record(string model, long inputTokens, long outputTokens, decimal costUsd, double durationMs)
{
    if (string.IsNullOrWhiteSpace(model)) model = "(unknown)";

    _byModel.AddOrUpdate(model,
        // Yeni model
        _ => new ModelUsage { Model = model, Calls = 1, ... },
        // Var olan model
        (_, existing) =>
        {
            lock (existing)
            {
                existing.Calls++;
                existing.InputTokens += inputTokens;
                existing.OutputTokens += outputTokens;
                existing.CostUsd += costUsd;
                existing.AverageLatencyMs = existing.AverageLatencyMs +
                    (durationMs - existing.AverageLatencyMs) / existing.Calls;   // running mean
                existing.LastUsed = DateTime.UtcNow;
                return existing;
            }
        });

    lock (_lock)
    {
        _totalInputTokens += inputTokens;
        _totalOutputTokens += outputTokens;
        _totalCost += costUsd;
        _totalCalls++;
    }
}
```

### Running mean (Welford-vari)

```
new_mean = old_mean + (new_value - old_mean) / count
```

Klasik `sum / count` yerine bu formül kullanılır çünkü:
- `sum` overflow olabilir (büyük sayılarda)
- Tek pass — geçmiş tüm değerleri tutmaya gerek yok

Pratikte LLM latency için bunlar overkill ama doğru yöntem.

### Thread safety

İki kademe:
1. **Per-model:** `lock (existing)` — aynı modelin update'leri seri
2. **Global:** `lock (_lock)` — total field'lar atomic

`ConcurrentDictionary.AddOrUpdate` factory'leri atomic — yeni model ekleme race-free.

Farklı modeller paralel update edilebilir (lock granular).

---

## `GetSnapshot`

```csharp
public CostSnapshot GetSnapshot()
{
    lock (_lock)
    {
        return new CostSnapshot
        {
            TotalCalls = _totalCalls,
            TotalInputTokens = _totalInputTokens,
            TotalOutputTokens = _totalOutputTokens,
            TotalCostUsd = _totalCost,
            ByModel = _byModel.Values
                .OrderByDescending(m => m.CostUsd)        // En pahalı model üstte
                .Select(m => new ModelUsage
                {
                    Model = m.Model,
                    Calls = m.Calls,
                    InputTokens = m.InputTokens,
                    OutputTokens = m.OutputTokens,
                    CostUsd = Math.Round(m.CostUsd, 6),
                    AverageLatencyMs = Math.Round(m.AverageLatencyMs, 2),
                    LastUsed = m.LastUsed
                })
                .ToList()
        };
    }
}
```

- **Copy döner** (referans değil) — caller'ın elindeki snapshot dondurulmuş
- En yüksek maliyetli model en üstte
- Cost 6 ondalık, latency 2 ondalık (UI'da göstermek için)

### CostSnapshot

```csharp
public sealed class CostSnapshot
{
    public long TotalCalls { get; set; }
    public long TotalInputTokens { get; set; }
    public long TotalOutputTokens { get; set; }
    public decimal TotalCostUsd { get; set; }
    public List<ModelUsage> ByModel { get; set; } = new();
}
```

### Admin endpoint çıktısı

```
GET /api/telemetry/cost
→
{
  "totalCalls": 1234,
  "totalInputTokens": 567890,
  "totalOutputTokens": 234567,
  "totalCostUsd": 1.234567,
  "byModel": [
    {
      "model": "gpt-4o",
      "calls": 100,
      "inputTokens": 50000,
      "outputTokens": 25000,
      "costUsd": 1.000000,
      "averageLatencyMs": 1450.23,
      "lastUsed": "2025-05-24T10:30:00Z"
    },
    {
      "model": "gpt-4o-mini",
      "calls": 1134,
      ...
    }
  ]
}
```

---

## `GetUsageSnapshot` (port arayüzü)

```csharp
public CostUsageSnapshot GetUsageSnapshot()
{
    var snapshot = GetSnapshot();
    return new CostUsageSnapshot(
        snapshot.TotalCalls,
        snapshot.TotalInputTokens,
        snapshot.TotalOutputTokens,
        snapshot.TotalCostUsd,
        snapshot.ByModel.Select(m => new CostModelUsageSnapshot(...)).ToList()
    );
}
```

`CostSnapshot` adapter-internal tip; `CostUsageSnapshot` Application port tip. Bu metod ikisi arasında dönüşüm yapar.

Application katmanı (`TelemetryPortService`) `ICostUsageStorePort.GetUsageSnapshot` çağırır — adapter-internal tipi görmez.

---

## `Reset` / `ResetUsage`

```csharp
public void Reset()
{
    lock (_lock)
    {
        _byModel.Clear();
        _totalInputTokens = 0;
        _totalOutputTokens = 0;
        _totalCost = 0;
        _totalCalls = 0;
    }
}

public void ResetUsage() => Reset();
```

Admin paneli "Sıfırla" butonu — günlük/oturum bazlı reset için. İki isim aynı işi yapar (geriye uyumluluk).

⚠️ **Restart sonrası otomatik sıfırlanır** — store in-memory. Production'da kalıcı toplam için `PostgresLlmCallUsageSink` (DB) kullanılmalı.

---

## ModelUsage

```csharp
public sealed class ModelUsage
{
    public string Model { get; set; } = "";
    public long Calls { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public decimal CostUsd { get; set; }
    public double AverageLatencyMs { get; set; }
    public DateTime LastUsed { get; set; }
}
```

Mutable class — `Record` içinde update edilir. Snapshot alındığında copy oluşturulur, çünkü dışarı verilen liste'nin mutate edilmemesi gerekir.

---

## Performans

- `Record`: ~5 µs (lock + dict update)
- `GetSnapshot`: O(model sayısı), tipik <50 model, ~50 µs
- Memory: ~100 bytes/model

Bellek pratikte sorun değil — birkaç düzine model, sabit-büyüklük.

---

## Bağlantılar

- [TelemetryChatClient.md](TelemetryChatClient.md) — `Record`'u çağıran decorator
- [Application TelemetryPortService](../CustomerSupportBot.Application/Telemetry/TelemetryPortService.md) — endpoint'i sunan port
