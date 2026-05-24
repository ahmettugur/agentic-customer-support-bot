# RedisDistributedLockAdapter

**Dosya:** `Locking/RedisDistributedLockAdapter.cs`  
**Port:** `IAppDistributedLock`  
**Kütüphane:** `Medallion.Threading.Redis` (RedLock algoritması)

---

## Neden distributed lock?

Multi-pod ortamda **aynı kaynağa eş zamanlı erişim** yarış koşulu yaratır:

```
Senaryo: İki admin aynı approval'ı 100ms arayla onaylar

Pod A: SELECT status WHERE id=X → "Pending"
                                                Pod B: SELECT status WHERE id=X → "Pending"
Pod A: UPDATE → "Approved"
                                                Pod B: UPDATE → "Rejected"   ← çakışma!
```

DB optimistic concurrency yardım edebilir ama:
- Hata kullanıcıya yansır
- Yan etkiler (notification, event yayını) iki kez tetiklenebilir

**Çözüm:** Karar verme bölgesini lock altına al — yalnızca bir pod aynı anda girer.

---

## Arayüz

```csharp
public interface IAppDistributedLock
{
    Task<IAsyncDisposable?> TryAcquireAsync(
        string resourceKey,
        TimeSpan? timeout = null,
        CancellationToken ct = default);

    Task<IAsyncDisposable> AcquireAsync(
        string resourceKey,
        TimeSpan? timeout = null,
        CancellationToken ct = default);
}
```

İki varyant:

| Metod | Davranış |
|---|---|
| `TryAcquireAsync` | Timeout içinde lock alınamazsa `null` döner — caller karar verir |
| `AcquireAsync` | Timeout içinde alınamazsa `ExternalServiceException` fırlatır |

---

## Kullanım deseni

```csharp
await using var handle = await _lock.AcquireAsync($"approval:decide:{approvalId}", TimeSpan.FromSeconds(5));
// Burada kritik bölge — yalnızca bir pod girer
var approval = await _approvalQueue.GetAsync(approvalId);
if (approval.Status != ApprovalStatus.Pending)
    throw new ConcurrencyConflictException("APPROVAL_ALREADY_DECIDED", "Başkası karar verdi");
await _approvalQueue.DecideAsync(approvalId, true);
// `await using` blok bitince lock otomatik serbest bırakılır
```

`await using` sayesinde exception fırlatılsa bile lock release edilir.

---

## Lock key konvansiyonu

Projede kullanılan tipik key'ler:

| Key | Kullanan | Amaç |
|---|---|---|
| `approval:decide:{approvalId}` | PostgresApprovalQueue | Approval karar yarışını önler |
| `chatmode:{sessionId}` | PostgresChatModeRegistry | İki TakeOver eş zamanlı olamaz |
| `customer-profile:{customerId}` | CustomerProfileService | Profile güncelleme yarışını önler |

Key'ler **kaynak bazında** seçilir — global lock yok. İki farklı approval ID'si eş zamanlı işlenebilir; yalnızca aynı ID için seri çalışma garantilenir.

---

## Default timeout

```csharp
private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
```

`timeout` parametresi geçilmezse 5 saniye. Bu çoğu senaryo için yeterli — kritik bölgeler hızlı çalışmalı.

Lock TTL (auto-release) için `RedisOptions.LockExpirySeconds` (default 30s) — caller release etmezse Redis kendi serbest bırakır.

---

## RedLock algoritması

`Medallion.Threading.Redis` RedLock kullanır:

1. `SET key value NX PX <expiry>` (atomic) — yalnızca key yoksa set et
2. Key'in unique bir token ile yazılması (UUID) — release ederken doğrula
3. TTL ile auto-release (deadlock koruma)
4. Lua script ile atomic release (token doğrulama)

Tek Redis instance ile çalışır. Multi-redis Redlock için ek yapılandırma gerekir (bu projede tek instance varsayılır).

---

## `LockHandle` (inner)

```csharp
private sealed class LockHandle : IAsyncDisposable
{
    private readonly IAsyncDisposable _inner;
    public LockHandle(IAsyncDisposable inner) => _inner = inner;
    public async ValueTask DisposeAsync() => await _inner.DisposeAsync();
}
```

Medallion'ın `RedisDistributedLockHandle`'ını wrap eder — bu sayede caller Medallion tipine doğrudan bağımlı olmaz. **Adaptör enkapsülasyonu**.

---

## Hata davranışı

| Senaryo | Davranış |
|---|---|
| Lock alındı, kullanıldı, dispose edildi | ✅ Normal akış |
| Lock 5s içinde alınamadı (TryAcquire) | `null` döner |
| Lock 5s içinde alınamadı (Acquire) | `ExternalServiceException("Redis", "Lock acquire timeout: ...")` |
| Redis bağlantı koptu | `ExceptionTranslator.Translate` ile `ExternalServiceException` |
| Lock alındı ama dispose edilmedi | 30s sonra Redis TTL ile auto-release |

---

## Performans

- Lock alımı: tipik 1-5 ms (LAN içinde Redis)
- Lock release: ~1 ms
- Lock bekleme: timeout süresi kadar bloklar (async)

Yüksek frekanslı işlemler için lock granularity'ye dikkat:
- ❌ Tüm DB tablosu için tek lock — bottleneck
- ✅ Row-level lock key — paralel işlem mümkün

---

## Bağlantılar

- [Application IAppDistributedLock arayüzü](../application/README.md) — port tanımı
- [PostgresAdapters.md](../adapters-persistence/PostgresAdapters.md) — Lock kullanan adapter'lar (Approval, ChatMode)
