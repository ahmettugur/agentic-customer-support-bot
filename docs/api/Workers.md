# Workers — Background Services

**Klasör:** `Workers/`

`IHostedService` implementasyonları — uygulama yaşam döngüsü boyunca arka planda çalışır.

| Worker | Tetikleyici | Sıklık |
|---|---|---|
| `KnowledgeBaseIngestor` | Startup | Bir kez |
| `SlaGuardianService` | Periyodik | Her N saniye |

---

## KnowledgeBaseIngestor

**Dosya:** `Workers/KnowledgeBaseIngestor.cs` (registered as `KnowledgeBaseStartupService`)

Uygulama başlangıcında **knowledge base dosyalarını** Qdrant'a embed eder.

### Davranış

```csharp
public async Task StartAsync(CancellationToken ct)
{
    var autoIngest = _options.KnowledgeBase.AutoIngestOnStartup;
    if (!autoIngest)
    {
        _logger.LogInformation("[KB] Auto-ingest disabled, skipping");
        return;
    }

    _logger.LogInformation("[KB] Starting knowledge base ingestion");
    await _memory.IngestAsync(ct);
    _logger.LogInformation("[KB] Knowledge base ingestion complete");
}

public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
```

### Akış

```
SemanticMemory.KnowledgeBase.AutoIngestOnStartup = true
   ↓
IMemoryPort.IngestAsync(ct)
   ↓
FileSystemKnowledgeBaseSource.GetFilesAsync()
   - KnowledgeBase/ klasöründeki .md/.txt/.pdf
   - SHA-256 hash karşılaştırması (.kb_hashes.json)
   - Sadece değişen dosyaları yield et
   ↓
OpenAiEmbeddingAdapter.EmbedBatchAsync(texts)
   ↓
QdrantVectorMemoryAdapter.UpsertAsync(docs, vectors)
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

KB dosyaları **hash bazlı** kontrol edilir. İkinci kez çalışınca:
- Değişmemiş dosyalar atlanır (skip)
- Sadece yeni veya hash'i değişen embed edilir

Bu sayede her startup'ta tüm KB yeniden embed edilmez (OpenAI maliyetinden tasarruf).

### Logging

```
[KB] Auto-ingest enabled, scanning KnowledgeBase/
[KB] Found 12 files, 3 changed (hash mismatch)
[KB] Embedding 3 files...
[KB] Qdrant upsert: 3 documents
[KB] Knowledge base ingestion complete (1.2s)
```

### Hata davranışı

Embed veya Qdrant hatası → uygulama **başlamaya devam eder**, ama memory boş olur:

```csharp
try { await _memory.IngestAsync(ct); }
catch (Exception ex)
{
    _logger.LogError(ex, "[KB] Ingestion failed — memory will be empty");
    // Uygulama yaşamaya devam
}
```

KB olmadan da bot çalışır (sadece semantic context'ten yoksun). Admin sorunu çözünce manuel tetikleyebilir.

### Lifetime

`StartAsync` bir kez çalışır, döner. `StopAsync` no-op. Sürekli arka plan loop'u yok.

---

## SlaGuardianService

**Dosya:** `Workers/SlaGuardianService.cs`  
**Base:** `BackgroundService`

Pending approval ve open escalation'ları periyodik tarar — SLA threshold aşılırsa action tetikler (auto-reject, priority boost, vb.).

### Davranış

```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    if (!_options.Enabled)
    {
        _logger.LogInformation("[SLA] Guardian disabled");
        return;
    }

    _logger.LogInformation("[SLA] Guardian started (poll: {Interval}s)", _options.PollIntervalSeconds);

    while (!stoppingToken.IsCancellationRequested)
    {
        try
        {
            await TryScanOnceAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SLA] Scan failed");
        }

        await Task.Delay(TimeSpan.FromSeconds(_options.PollIntervalSeconds), stoppingToken);
    }
}
```

### Distributed lock (multi-pod safety)

Birden fazla pod aynı anda scan yaparsa duplicate action olur. `IAppDistributedLock` çözer:

```csharp
private async Task TryScanOnceAsync(CancellationToken ct)
{
    var lockKey = "sla:guardian:scan";
    var lockTtl = TimeSpan.FromSeconds(_options.PollIntervalSeconds - 1);

    if (_distributedLock != null)
    {
        await using var handle = await _distributedLock.TryAcquireAsync(lockKey, lockTtl, ct);
        if (handle is null)
        {
            _logger.LogDebug("[SLA] Skipping scan — another pod has the lock");
            return;
        }

        await _slaPort.ScanOnce(_options, ct);
    }
    else
    {
        // Single-instance: lock yok, doğrudan scan
        await _slaPort.ScanOnce(_options, ct);
    }
}
```

**Lock TTL = `PollIntervalSeconds - 1`** — yeni iterasyondan **önce** otomatik release.

### Yapılandırma

```json
{
  "Sla": {
    "Enabled": true,
    "PollIntervalSeconds": 30,
    "Approval": {
      "WarnThresholdSeconds": 60,
      "BreachThresholdSeconds": 300,
      "OnBreach": "AutoReject"
    },
    "Escalation": {
      "WarnThresholdSeconds": 120,
      "BreachThresholdSeconds": 600,
      "OnBreach": "PriorityBoost"
    }
  }
}
```

### ScanOnce çağrısı

`ISlaPort.ScanOnce` (Application katmanı) tüm iş mantığını yapar:
- Pending approval'ları DB'den çek
- Yaşları kontrol et (now - createdAt)
- Threshold aşılan'lara `SlaEvent` üret
- Action uygula (AutoReject, AutoApprove, PriorityBoost)
- `ISlaEventSink.Record()` ile event'i sakla

Detay: [Application SlaGuardian](../application/SlaGuardian.md).

### Lifetime

`BackgroundService.ExecuteAsync` uygulama yaşam döngüsü boyunca çalışır:
- `StartAsync` → ExecuteAsync başlatılır (fire-and-forget)
- Uygulama shutdown → `stoppingToken` cancel → loop çıkar
- `StopAsync` graceful shutdown bekler

### Single-instance vs multi-pod

| Senaryo | `IAppDistributedLock` | Davranış |
|---|---|---|
| Single-pod (dev/test) | Redis varsa registered | Lock alır ama tek pod olduğu için her zaman geçer |
| Multi-pod (production) | Redis registered | Sadece lock alan pod scan eder |
| Redis yok / disabled | `null` | Tek instance olduğu varsayılır, doğrudan scan |

Production'da Redis zorunlu olduğu için pratikte ilk iki senaryo görülür.

---

## Genel desen: HostedService vs BackgroundService

| Tip | Kullanım |
|---|---|
| `IHostedService` | One-shot startup işi (KnowledgeBaseIngestor) |
| `BackgroundService` | Sürekli loop (SlaGuardianService) |

`BackgroundService` `IHostedService`'i implement eden abstract class — `ExecuteAsync` override edilir, framework start/stop'u yönetir.

---

## Bağlantılar

- [Application MemoryPortService](../application/MemoryPortService.md) — `IngestAsync` mantığı
- [Application SlaGuardian](../application/SlaGuardian.md) — `ScanOnce` mantığı
- [Adapters.Persistence FileSystemAdapters](../adapters-persistence/FileSystemAdapters.md) — KB hash tracking
- [Adapters.Redis DistributedLock](../adapters-redis/DistributedLock.md)
