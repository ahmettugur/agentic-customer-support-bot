# Workers — Background Services

**Klasör:** `Workers/`

`IHostedService` implementasyonları — uygulama yaşam döngüsü boyunca arka planda çalışır.

| Worker | Sınıf | Tetikleyici | Sıklık |
|---|---|---|---|
| `KnowledgeBaseIngestor.cs` | `KnowledgeBaseStartupService` | Startup | Bir kez |
| `SlaGuardianService.cs` | `SlaGuardianService` | Periyodik | Her N saniye |

---

## KnowledgeBaseStartupService

**Dosya:** `Workers/KnowledgeBaseIngestor.cs`

Uygulama başlangıcında **knowledge base dosyalarını** Qdrant'a embed eder. `IHostedService` implement eder — one-shot startup işi.

### Davranış

```csharp
public Task StartAsync(CancellationToken ct)
{
    if (!_options.KnowledgeBase.AutoIngestOnStartup)
    {
        _logger.LogInformation("KnowledgeBase auto-ingest devre dışı (AutoIngestOnStartup=false).");
        return Task.CompletedTask;
    }

    return _memory.IngestAsync(ct);
}

public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
```

### Constructor injection

```csharp
public KnowledgeBaseStartupService(
    IMemoryPort memory,
    IOptions<SemanticMemoryOptions> options,
    ILogger<KnowledgeBaseStartupService> logger)
```

### Akış

```
SemanticMemory.KnowledgeBase.AutoIngestOnStartup = true
   ↓
IMemoryPort.IngestAsync(ct)
   ↓
FileSystemKnowledgeBaseSource.GetFilesAsync()
   - KnowledgeBase/ klasöründeki dosyalar
   - SHA-256 hash karşılaştırması
   - Sadece değişen dosyaları yield et
   ↓
EmbeddingAdapter.EmbedBatchAsync(texts)
   ↓
VectorMemoryAdapter.UpsertAsync(docs, vectors)
```

### Yapılandırma

```json
{
  "SemanticMemory": {
    "Enabled": true,
    "KnowledgeBase": {
      "AutoIngestOnStartup": true,
      "RootPath": "KnowledgeBase"
    }
  }
}
```

`AutoIngestOnStartup = false` ise admin manuel tetikler:
```http
POST /memory/ingest
Authorization: Bearer <admin-jwt>
```

### Idempotent

KB dosyaları **hash bazlı** kontrol edilir — değişmemiş dosyalar atlanır. Her startup'ta tüm KB yeniden embed edilmez (embedding maliyetinden tasarruf).

### Lifetime

`StartAsync` bir kez çalışır, döner. `StopAsync` no-op. Sürekli arka plan loop'u yok.

---

## SlaGuardianService

**Dosya:** `Workers/SlaGuardianService.cs`
**Base:** `BackgroundService`

Pending approval ve open escalation'ları periyodik tarar — SLA threshold aşılırsa action tetikler (auto-reject, priority boost, vb.).

### Constructor injection

```csharp
public SlaGuardianService(
    ISlaPort slaPort,
    IOptionsMonitor<SlaOptions> options,
    ILogger<SlaGuardianService> logger,
    IAppDistributedLock? distributedLock = null)
```

`IOptionsMonitor` — hot reload destekler (SLA config runtime değişebilir). `IAppDistributedLock` opsiyonel — Redis yoksa null, single-instance modda çalışır.

### Davranış

```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    var initial = _options.CurrentValue;
    if (!initial.Enabled)
    {
        _logger.LogInformation("[SLA] Guardian disabled — exiting.");
        return;
    }

    while (!stoppingToken.IsCancellationRequested)
    {
        var opts = _options.CurrentValue;
        try
        {
            await RunScanWithLockAsync(opts, stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SLA] Scan iteration failed");
        }

        await Task.Delay(
            TimeSpan.FromSeconds(Math.Max(1, opts.PollIntervalSeconds)),
            stoppingToken);
    }
}
```

### Distributed lock (multi-pod safety)

```csharp
private async Task RunScanWithLockAsync(SlaOptions opts, CancellationToken ct)
{
    if (_distributedLock == null)
    {
        // Redis yok — single-instance, kilit gereksiz
        _slaPort.ScanOnce(opts);
        return;
    }

    var lockTtl = TimeSpan.FromSeconds(Math.Max(1, opts.PollIntervalSeconds - 1));
    await using var handle = await _distributedLock.TryAcquireAsync(LockKey, lockTtl, ct);

    if (handle == null)
    {
        _logger.LogDebug("[SLA] Distributed lock alınamadı — bu pod taramayı atlıyor.");
        return;
    }

    _slaPort.ScanOnce(opts);
}
```

**Lock key:** `"sla:guardian:scan"`
**Lock TTL:** `PollIntervalSeconds - 1` saniye — yeni iterasyondan önce otomatik release.

### Yapılandırma

```json
{
  "Sla": {
    "Enabled": true,
    "PollIntervalSeconds": 30,
    "Approvals": {
      "WarnAfterSeconds": 60,
      "BreachAfterSeconds": 300,
      "OnBreach": "AutoReject"
    },
    "Escalations": {
      "WarnAfterSeconds": 120,
      "BreachAfterSeconds": 600,
      "BoostPriorityOnBreach": true
    }
  }
}
```

### ScanOnce çağrısı

`ISlaPort.ScanOnce(opts)` (Application katmanı) tüm iş mantığını yapar:
- Pending approval ve open escalation'ları kontrol et
- Yaşları hesapla (now - createdAt)
- Threshold aşılan'lara `SlaEvent` üret
- Action uygula (AutoReject, PriorityBoost)
- `ISlaEventSink.Record()` ile event'i sakla

Detay: [Application SlaGuardian](../CustomerSupportBot.Application/Sla/SlaPortService.md).

### Startup logu

```
[SLA] Guardian started. Poll=30s, ApprovalBreach=300s, EscalationBreach=600s, DistributedLock=Redis
```

### Single-instance vs multi-pod

| Senaryo | `IAppDistributedLock` | Davranış |
|---|---|---|
| Single-pod (dev/test) | `null` (Redis yok) | Doğrudan scan |
| Multi-pod (production) | Redis registered | Sadece lock alan pod scan eder |
| Single-pod + Redis | Redis registered | Lock alır, her zaman geçer |

### Lifetime

`BackgroundService.ExecuteAsync` uygulama yaşam döngüsü boyunca çalışır:
- `StartAsync` → ExecuteAsync başlatılır (fire-and-forget)
- Uygulama shutdown → `stoppingToken` cancel → `Task.Delay` `OperationCanceledException` → loop çıkar
- `StopAsync` graceful shutdown bekler

---

## Genel desen: HostedService vs BackgroundService

| Tip | Kullanım |
|---|---|
| `IHostedService` | One-shot startup işi (KnowledgeBaseStartupService) |
| `BackgroundService` | Sürekli loop (SlaGuardianService) |

`BackgroundService` `IHostedService`'i implement eden abstract class — `ExecuteAsync` override edilir, framework start/stop'u yönetir.

---

## DI kaydı

`ApplicationServicesExtensions.cs`:

```csharp
services.AddHostedService<SlaGuardianService>();
services.AddHostedService<KnowledgeBaseStartupService>();
```

---

## Bağlantılar

- [Application MemoryPortService](../CustomerSupportBot.Application/Memory/MemoryPortService.md) — `IngestAsync` mantığı
- [Application SlaGuardian](../CustomerSupportBot.Application/Sla/SlaPortService.md) — `ScanOnce` mantığı
- [Adapters.Persistence FileSystemAdapters](../CustomerSupportBot.Adapters.Persistence/FileSystemAdapters.md) — KB hash tracking
- [Adapters.Redis DistributedLock](../CustomerSupportBot.Adapters.Redis/DistributedLock.md)
- [Endpoints-Observability.md](Endpoints-Observability.md) — `/sla/status`, `/sla/events`
