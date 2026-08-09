# Dependency Injection & Configuration

**Dosyalar:**
- `DependencyInjection/RedisAdapterServiceCollectionExtensions.cs`
- `RedisOptions.cs`

---

## `AddRedisAdapters`

```csharp
public static IServiceCollection AddRedisAdapters(
    this IServiceCollection services,
    IConfiguration configuration)
```

Tüm Redis altyapısını tek satırla kaydeder.

### Ne yapar?

1. `RedisOptions`'ı `appsettings.json > "Redis"` bölümünden bind eder
2. Connection string'i çözer (aşağıdaki sıra ile)
3. Connection string yoksa **InvalidOperationException** fırlatır
4. `IConnectionMultiplexer` (Singleton) kaydeder — retry policy ile
5. `RedisDistributedLockAdapter` → `IAppDistributedLock`
6. `RedisMessageBusAdapter` → `IMessageBusPort`

---

## Connection string öncelik sırası

```
1. Redis:ConnectionString              (appsettings > Redis bölümü)
2. ConnectionStrings:Redis             (.NET standart connection string)
3. Yoksa → throw InvalidOperationException
```

İki örnek:

```json
// Yöntem 1 — Redis bölümünde
{
  "Redis": {
    "ConnectionString": "localhost:6379,password=secret"
  }
}

// Yöntem 2 — standart ConnectionStrings
{
  "ConnectionStrings": {
    "Redis": "localhost:6379"
  }
}
```

**Override:** `Redis:ConnectionString` her zaman `ConnectionStrings:Redis`'i geçer — production'da environment variable veya secret manager ile override için kullanışlı.

---

## ConnectionMultiplexer ayarları

```csharp
var configOptions = ConfigurationOptions.Parse(connectionString);
configOptions.AbortOnConnectFail = false;
configOptions.ConnectRetry = 3;
configOptions.ReconnectRetryPolicy = new ExponentialRetry(5000);
```

| Ayar | Değer | Açıklama |
|---|---|---|
| `AbortOnConnectFail` | `false` | İlk bağlantı başarısız olsa bile uygulama başlasın; Redis sonra gelirse otomatik bağlanır |
| `ConnectRetry` | `3` | İlk bağlantı için 3 deneme |
| `ReconnectRetryPolicy` | `ExponentialRetry(5000)` | Kopan bağlantıyı 5s'den başlayarak exponential backoff ile yeniden dener |

### Connection event logging

```
ConnectionFailed   → "[Redis] Bağlantı koptu: localhost:6379 — SocketFailure"
ConnectionRestored → "[Redis] Bağlantı yeniden kuruldu: localhost:6379"
```

Bu loglar operasyonel görünürlük için kritik — Redis kısa süreli kopmaları development'ta tolere edilebilir ama production'da alarm tetikler.

---

## RedisOptions

```csharp
public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    public string? ConnectionString { get; set; }
    public string KeyPrefix { get; set; } = "csbot";
    public int DefaultLockTimeoutSeconds { get; set; } = 10;
    public int LockExpirySeconds { get; set; } = 30;
}
```

### Alanlar

| Alan | Default | Açıklama |
|---|---|---|
| `ConnectionString` | `null` | StackExchange.Redis bağlantı dizisi |
| `KeyPrefix` | `"csbot"` | ⚠️ **Ölü config** — okunur ama hiçbir Redis adapter'ında (`RedisDistributedLockAdapter`, `RedisMessageBusAdapter`) kullanılmaz |
| `DefaultLockTimeoutSeconds` | `10` | Lock alımı için max bekleme süresi |
| `LockExpirySeconds` | `30` | Lock otomatik serbest bırakma süresi (deadlock koruma) |

### KeyPrefix — şu an etkisiz

`RedisDistributedLockAdapter`'ın constructor'ı yalnızca `IConnectionMultiplexer` ve `ILogger` alır; `RedisOptions`'ı hiç inject etmez. `KeyPrefix` alanı config'te ve `RedisOptions` sınıfında tanımlı olsa da lock veya pub/sub key'lerinin başına eklenmez — çok kiracılı (multi-tenant) bir Redis instance paylaşımında key çakışmasını önlemez.

### LockExpirySeconds neden 30s?

Eğer lock'u tutan pod **çökerse** veya cevap vermezse, lock 30 saniye sonra otomatik serbest bırakılır. Bu deadlock'a karşı güvenlik ağı — Medallion RedLock TTL ile çalışır.

> ⚠️ **Dikkat:** Lock'u tutan iş 30 saniyeden uzun sürerse lock kaybolur ve başka pod aynı kaynağa erişebilir. Uzun işlemler için ya `LockExpirySeconds` artır ya da iş'i parçalara böl.

---

## Singleton yaşam döngüsü

`IConnectionMultiplexer` **Singleton** kaydedilir çünkü:

- StackExchange.Redis multiplexer **thread-safe**
- TCP bağlantısını **pool**'lar — her request için yeni bağlantı açmaz
- Birden fazla multiplexer açmak gereksiz kaynak kullanımı

Adapter sınıfları (`RedisDistributedLockAdapter`, `RedisMessageBusAdapter`) de Singleton — multiplexer'ı paylaşırlar.

---

## DI sonrası kullanım

Application veya başka adapter sınıfından:

```csharp
public class SomeService
{
    private readonly IAppDistributedLock _lock;
    private readonly IMessageBusPort _bus;

    public SomeService(IAppDistributedLock @lock, IMessageBusPort bus)
    {
        _lock = @lock;
        _bus = bus;
    }
}
```

Bu sınıf **`Adapters.Redis` paketine bağımlı değildir** — sadece Application port'larını bilir. Redis yerine başka backend (örn. InMemory lock, RabbitMQ bus) takılırsa kod değişmez.
