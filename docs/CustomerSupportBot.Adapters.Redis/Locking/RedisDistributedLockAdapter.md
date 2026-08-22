# RedisDistributedLockAdapter

- **Kaynak:** `CustomerSupportBot.Adapters.Redis/Locking/RedisDistributedLockAdapter.cs`
- **Tür:** `public sealed class : IAppDistributedLock`
- **Namespace:** `CustomerSupportBot.Adapters.Redis.Locking`

## Ne işe yarar?

`RedisDistributedLockAdapter`, Application katmanındaki [IAppDistributedLock](../../CustomerSupportBot.Application/Ports/Outbound/Locking/IAppDistributedLock.md) portunu uygulayan; `Medallion.Threading.Redis` kütüphanesini ve RedLock algoritmasını kullanarak dağıtık sistemlerde aynı kaynak anahtarı (`resourceKey`) üzerinde eşzamanlı yarış durumlarını (Race Condition) engelleyen kilit adaptörüdür.

## Hangi amaçla kullanılır`?

- Aynı oturumda (`session:{sessionId}`) kullanıcının art arda bastığı butonlar veya hızlı mesajlar nedeniyle birden fazla MAF iş akışının eşzamanlı çalışıp oturum durumunu bozmasını engellemek.
- Sipariş oluşturma/iptal veya HITL onay süreçlerinde aynı kayıt üzerinde çift işlem yapılmasını engellemek.

## Sorumlulukları

- **Üstlendiği:**
  - `TryAcquireAsync` ile kilit edinmeyi denemek (edinilemezse `null` dönmek).
  - `AcquireAsync` ile kilidi zorunlu almak (zaman aşımı veya Redis hatasında [ExceptionTranslator](../ExceptionTranslator.md) ile hata fırlatmak).
  - `IAsyncDisposable` sarmalayıcısı (`LockHandle`) ile kilit serbest bırakma mekanizması sunmak.

## Diğer Katman ve Bileşenlerle İlişkileri

- [`IAppDistributedLock`](../../CustomerSupportBot.Application/Ports/Outbound/Locking/IAppDistributedLock.md) portunu uygular; Application katmanındaki tüketiciler (ör. session güncelleme, onay karar akışı) somut Redis implementasyonunu bilmez, sadece bu portu enjekte eder.
- Hata durumunda [`ExceptionTranslator`](../ExceptionTranslator.md)'a delege eder.
- `IConnectionMultiplexer` singleton'ını [`RedisAdapterServiceCollectionExtensions`](../DependencyInjection/RedisAdapterServiceCollectionExtensions.md) tarafından DI'a kaydedilen örnekten alır.

## Kullanılma Nedeni ve Tasarım Yaklaşımı

RedLock algoritması, tek bir Redis düğümünün geçici olarak kilitlenip sonra çökmesi durumunda bile kilidin **kendiliğinden süresi dolarak** (lease) serbest kalmasını garanti eder — bu, uygulamanın çökmesi/restart olması halinde kalıcı kilitlenmeyi (deadlock) önler. `TryAcquireAsync`/`AcquireAsync` ayrımı bilinçlidir: bazı çağıranlar (ör. "zaten biri işliyorsa sessizce vazgeç") `null` dönüşünü bekler, bazıları ise ("kilit şart, yoksa hata ver") kesin başarı ister — tek bir metotla iki farklı semantiği zorlamak yerine iki ayrı metot sunulmuştur.

> 🐞 **Dikkat — `RedisOptions` içindeki `DefaultLockTimeoutSeconds`/`LockExpirySeconds` bu sınıfta KULLANILMIYOR:**
> `RedisOptions.cs` dosyasında bu iki alan tanımlı olsa da, bu adapter `DefaultTimeout` değerini kodda sabit `TimeSpan.FromSeconds(5)` olarak tutar ve lease süresini `Medallion.Threading.Redis.RedisDistributedLock`'ın kendi varsayılanına bırakır — `RedisOptions`'taki değerler hiçbir yerden okunmaz (bkz. [RedisOptions.md](../Options/RedisOptions.md)). appsettings.json'da bu alanları değiştirmek şu an davranışı etkilemez; bu, henüz bağlanmamış "ölü" konfigürasyondur.

## Constructor ve Başlatma Mantığı

```csharp
public RedisDistributedLockAdapter(
    IConnectionMultiplexer redis,
    ILogger<RedisDistributedLockAdapter> logger)
```

### Constructor İçerisinde Yapılan İşler:
- `_redis` (`IConnectionMultiplexer`): Thread-safe Redis bağlantı havuzu saklanır.
- `_logger`: Günlükleme motoru atanır.
- `DefaultTimeout`: 5 saniyelik varsayılan kilit edinme zaman aşımı süresi tanımlanır.

## Metotlar ve İç Çalışma Mantıkları

### 1. `TryAcquireAsync`
```csharp
public async Task<IAsyncDisposable?> TryAcquireAsync(
    string resourceKey,
    TimeSpan? timeout = null,
    CancellationToken ct = default)
```
- **Ne işe yarar?:** Belirtilen anahtar için kilit almayı dener.
- **İç Mantığı:**
  1. `new RedisDistributedLock(resourceKey, _redis.GetDatabase())` örneği kurulur.
  2. `lock.TryAcquireAsync(timeout ?? DefaultTimeout, ct)` çağrılır.
  3. Alınamazsa (`handle is null`), debug log yazılır ve `null` döner.
  4. Başarılıysa `new LockHandle(handle)` döner.

### 2. `AcquireAsync`
```csharp
public async Task<IAsyncDisposable> AcquireAsync(
    string resourceKey,
    TimeSpan? timeout = null,
    CancellationToken ct = default)
```
- **Ne işe yarar?:** Kilidi kesinlikle edinmek üzere çağrılır; başarısızlık durumunda istisna fırlatır.
- **İç Mantığı:** `TryAcquireAsync` çağrılır; `null` dönerse `TimeoutException` fırlatılıp [ExceptionTranslator](../ExceptionTranslator.md) ile `ExternalServiceException`'a çevrilir. `RedisException` durumunda da yine domain istisnası üretilir.

## Dahili Sınıflar

### `LockHandle` (Private Sealed)
`IAsyncDisposable` uygulayan sarmalayıcı sınıf; `DisposeAsync` çağrıldığında Medallion kilit tanıtıcısını asenkron olarak Redis üzerinden serbest bırakır (`await _inner.DisposeAsync()`).

## Bağımlılıklar

- [IAppDistributedLock](../../CustomerSupportBot.Application/Ports/Outbound/Locking/IAppDistributedLock.md)
- `Medallion.Threading.Redis.RedisDistributedLock`
- `StackExchange.Redis.IConnectionMultiplexer`
- [ExceptionTranslator](../ExceptionTranslator.md)
