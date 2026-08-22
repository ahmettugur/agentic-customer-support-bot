# IAppDistributedLock

**Kaynak:** `Ports/Outbound/Locking/IAppDistributedLock.cs`
**Implementasyon:** [`RedisDistributedLockAdapter`](../../../../CustomerSupportBot.Adapters.Redis/Locking/RedisDistributedLockAdapter.md)

## 1. Ne İşe Yarar

Pod'lar arası dağıtık kilit için secondary port. `TryAcquireAsync` (timeout içinde denemeli) ve
`AcquireAsync` (kilit alana kadar bekleyen, timeout aşılırsa `TimeoutException` fırlatan) iki
varyant sunar; ikisi de kilit tutan bir `IAsyncDisposable` handle döner — dispose edildiğinde
kilit otomatik serbest kalır.

## 2. Hangi Amaçla Kullanılır

Aynı oturuma (`sessionId`) çoklu pod'dan aynı anda gelen turların birbirini ezmemesi için tur
işleme kilidi olarak kullanılır — `ChatPortService` bir turu işlerken aynı session için gelen
ikinci bir isteği bu kilitle serialize eder.

## 3. Sorumlulukları

- **Üstlendiği:** Verilen `resourceKey` için karşılıklı dışlama (mutual exclusion) sağlamak.
- **Üstlenmediği:** Neyin kilitlendiği/korunduğu — çağıran taraf hangi kaynağı hangi anahtarla
  temsil edeceğine karar verir.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`Adapters.Redis/Locking/RedisDistributedLockAdapter` implemente eder (Redis `SET NX PX` tabanlı
kilit). ⚠️ `RedisOptions`'taki `DefaultLockTimeoutSeconds`/`LockExpirySeconds` alanları **ölü
konfigürasyondur** — hiçbiri okunmaz, gerçek kilit süresi adaptörde sabit kodlanmıştır (bkz.
`Adapters.Redis/README.md`).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

`using`/`await using` ile kullanılabilir bir `IAsyncDisposable` dönmesi, kilit serbest bırakmayı
unutma riskini ortadan kaldırır — çağıran kodun try/finally yazmasına gerek kalmaz.
`TryAcquireAsync` ile `AcquireAsync` ayrımı, çağıranın "kilit alınamazsa hemen vazgeç" ile
"kilit alınana kadar bekle" arasında bilinçli bir seçim yapmasını sağlar.

## 6. Metotlar / Üyeler

| Metot | Açıklama |
|---|---|
| `Task<IAsyncDisposable?> TryAcquireAsync(string resourceKey, TimeSpan? timeout = null, CancellationToken ct = default)` | Kilit almayı dener; timeout içinde alınamazsa `null` döner. |
| `Task<IAsyncDisposable> AcquireAsync(string resourceKey, TimeSpan? timeout = null, CancellationToken ct = default)` | Kilit alana kadar bekler; timeout aşılırsa `TimeoutException` fırlatır. |

## 7. Bağımlılıklar

Yok — port arayüzü bağımlılıksızdır.
