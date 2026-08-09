# Hybrid Cache + DB Deseni

Postgres adaptörlerinin tamamı aynı mimari deseni paylaşır: **in-memory cache önünde PostgreSQL, opsiyonel Redis pub/sub ile**. Bu desen her adapter'da yeniden uygulanır; merkezi bir base class yoktur.

---

## Neden hybrid?

- **Okuma hızı:** Tüm okumalar önce cache'ten karşılanır, DB'ye gidilmez (<1ms)
- **Yazma dayanıklılığı:** Yazma işlemleri DB'ye ulaşır; restart sonrası veri korunur
- **Yatay ölçekleme:** Redis pub/sub sayesinde farklı pod'ların cache'leri senkronize tutulur
- **Startup kurtarma:** `PersistenceHydrator` uçurulmuş in-flight kayıtları onarır

---

## Lazy hydration

Her Postgres adaptörü ilk erişimde tüm tabloyu (veya son N kaydı) DB'den cache'e yükler:

```csharp
private async Task EnsureHydratedAsync()
{
    if (_hydrated) return;
    await _hydrationLock.WaitAsync();
    try
    {
        if (_hydrated) return;  // double-check
        // DB'den yükle → _cache'e doldur
        _hydrated = true;
    }
    finally { _hydrationLock.Release(); }
}
```

`SemaphoreSlim(1,1)` ile aynı anda yalnızca bir hydration işlemi çalışır.

---

## Write stratejileri

Farklı adapter'lar veri önemine göre farklı yazma sırası kullanır:

### Cache-first (hız öncelikli)

```
cache güncelle → DB async INSERT/UPDATE → (hata swallow)
```

Kullanım: SLA events, LLM usage — küçük kayıp kabul edilebilir.

### Durable-first (bütünlük öncelikli)

```
DB INSERT/UPDATE → cache güncelle
```

Kullanım: Ratings, Approval requests — kayıp kabul edilemez.

### Write-through

```
cache güncelle → DB INSERT/UPDATE → bekle
```

Kullanım: Session exchanges, escalations, chat mode.

---

## Redis pub/sub entegrasyonu

Birden fazla uygulama instance'ı varsa Redis kanalları cache senkronizasyonu sağlar:

```
Pod A: Decide(approvalId) → DB UPDATE → Redis PUBLISH "csbot:approval:decided"
Pod B: Redis SUBSCRIBE → cache güncelle → event fire
```

Redis bağlantısı yoksa adaptör yalnızca in-process çalışır (single-pod senaryosu).

**Redis kanal adları:**

| Kanal | Olay |
|-------|------|
| `csbot:approval:created` | Yeni onay isteği |
| `csbot:approval:decided` | Onay/red kararı |
| `csbot:escalation:created` | Yeni eskalasyon |
| `csbot:escalation:decided` | Eskalasyon kararı |
| `csbot:bridge:touser` | Müşteri kanalı mesajı |
| `csbot:bridge:toadmin` | Admin kanalı mesajı |
| `csbot:chatmode` | Bot/Human mod değişikliği |

---

## Dağıtık lock

Race condition riskli işlemler (çift karar verme, eş zamanlı TakeOver) `IAppDistributedLock` ile korunur:

```csharp
await using var lock = await _lock.AcquireAsync($"approval:decide:{id}");
// Sadece bir pod bu bloğu aynı anda çalıştırır
```

InMemory ortamda bu lock no-op çalışır.

---

## Ring buffer limitleri

In-memory cache'in büyümesini önlemek için her adapter'da maksimum kayıt sınırı vardır:

| Adapter | Limit |
|---------|-------|
| ChatBridge history | 200 mesaj |
| ApprovalQueue history | 200 kayıt |
| EscalationSink | 500 kayıt |
| ReasoningTraceStore | 500 trace |
| SlaEventSink | 500 event |
| SessionManager | 500 session (metadata) |

Limit aşıldığında en eski kayıt silinir (FIFO).

---

## Hydration örneği (PostgresApprovalQueue)

```
İlk GetPending() çağrısı
    → EnsureHydratedAsync()
    → DB: SELECT * FROM hitl.approval_requests ORDER BY requested_at DESC LIMIT 200
    → _cache.TryAdd(id, entity) for each row
    → _hydrated = true

Sonraki GetPending() çağrısı
    → _hydrated == true → direkt cache'ten dön
```
