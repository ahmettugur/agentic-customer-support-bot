# RedisAdapterServiceCollectionExtensions

- **Kaynak:** `CustomerSupportBot.Adapters.Redis/DependencyInjection/RedisAdapterServiceCollectionExtensions.cs`
- **Tür:** `public static class`
- **Namespace:** `CustomerSupportBot.Adapters.Redis.DependencyInjection`

## Ne işe yarar?

`RedisAdapterServiceCollectionExtensions`, `StackExchange.Redis.IConnectionMultiplexer` singleton bağlantı havuzunu, `RedisDistributedLockAdapter` (`IAppDistributedLock`) ve `RedisMessageBusAdapter` (`IMessageBusPort`) bileşenlerini DI konteynerine kaydeden uzantıdır.

## Hangi amaçla kullanılır`?

- Dağıtık kilit ve mesajlaşma servislerini tek bir metot çağrısıyla (`services.AddRedisAdapters(configuration)`) IoC konteynerine bağlamak.
- Redis bağlantı dizesi tanımlı değilse uygulama başlangıcında anında fail-fast yaparak `InvalidOperationException` fırlatmak.
- Bağlantı kopmalarını (`ConnectionFailed`) ve geri gelmelerini (`ConnectionRestored`) otomatik loglamak.

## Sorumlulukları

- **Üstlendiği:**
  - `RedisOptions`'ı okuyup bağlantı dizesini çözümlemek (öncelik: `Redis:ConnectionString` → `ConnectionStrings:Redis`).
  - `IConnectionMultiplexer` singleton'ını, retry/reconnect politikalarıyla birlikte kurmak.
  - `IAppDistributedLock` ve `IMessageBusPort` portlarını somut Redis implementasyonlarına bağlamak.
- **Üstlenmediği:**
  - Redis'e gerçek komut göndermek (bu iş [RedisDistributedLockAdapter](../Locking/RedisDistributedLockAdapter.md) ve [RedisMessageBusAdapter](../Messaging/RedisMessageBusAdapter.md)'a ait).
  - `RedisHealthCheck`'i kaydetmek — bu, `CustomerSupportBot.Api/Extensions/HealthCheckExtensions.cs` içinde ayrıca yapılır (Api katmanı health-check pipeline'ını kendi kurar).

## Diğer Katman ve Bileşenlerle İlişkileri

- `CustomerSupportBot.Api/Program.cs` başlangıçta `services.AddRedisAdapters(configuration)`'ı çağırır — bu, tüm Redis altyapısının uygulamaya giriş noktasıdır.
- Application katmanındaki [`IAppDistributedLock`](../../CustomerSupportBot.Application/Ports/Outbound/Locking/IAppDistributedLock.md) ve [`IMessageBusPort`](../../CustomerSupportBot.Application/Ports/Outbound/Messaging/IMessageBusPort.md) portlarını somutlaştırır — Application katmanı bu extension'ı veya Redis'i doğrudan bilmez, sadece port arayüzlerine bağımlıdır.

## Kullanılma Nedeni ve Tasarım Yaklaşımı

Hexagonal mimaride DI bağlama kodu adapter'ın kendi projesinde tutulur (Composition Root'tan çağrılan tek bir extension metodu) — böylece Api katmanı "hangi somut sınıfın hangi arayüzü karşıladığını" bilmek zorunda kalmaz, sadece `AddRedisAdapters(configuration)` çağırır. Bağlantı dizesi eksikse **fail-fast** yaklaşımı benimsenmiştir: Redis bu projede opsiyonel bir önbellek değil, dağıtık kilit ve pod-arası mesajlaşma için zorunlu bir bağımlılıktır — sessizce bozuk çalışmak yerine uygulama hiç ayağa kalkmaz.

## Metotlar ve İç Çalışma Mantıkları

### 1. `AddRedisAdapters`
```csharp
public static IServiceCollection AddRedisAdapters(
    this IServiceCollection services,
    IConfiguration configuration)
```
- **Ne işe yarar?:** Redis altyapısını IoC konteynerine kaydeder.
- **İç Mantığı:**
  1. `RedisOptions` konfigürasyonu okunur ve `ConnectionString` çözümlenir (yoksa hata fırlatılır).
  2. `IConnectionMultiplexer` singleton olarak kaydedilir; `AbortOnConnectFail = false`, `ConnectRetry = 3` ve `ExponentialRetry(5000)` ayarları uygulanır.
  3. `IAppDistributedLock` ➔ `RedisDistributedLockAdapter` (Singleton) kaydedilir.
  4. `IMessageBusPort` ➔ `RedisMessageBusAdapter` (Singleton) kaydedilir.

## Bağımlılıklar

- [IAppDistributedLock](../Locking/RedisDistributedLockAdapter.md)
- [IMessageBusPort](../Messaging/RedisMessageBusAdapter.md)
- [RedisOptions](../Options/RedisOptions.md)
- `StackExchange.Redis.IConnectionMultiplexer`
