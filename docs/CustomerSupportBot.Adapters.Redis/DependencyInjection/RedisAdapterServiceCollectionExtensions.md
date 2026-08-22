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
