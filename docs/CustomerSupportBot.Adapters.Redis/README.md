# CustomerSupportBot.Adapters.Redis

Bu klasör, hexagonal mimaride **Driven Adapter (Çıkış Adaptörü)** rolünü üstlenen; dağıtık kilit yönetimi (Distributed Lock - RedLock), yatay ölçeklendirme mesajlaşması (Pub/Sub Message Bus) ve sistem sağlık kontrollerini (Health Check) Redis altyapısıyla somutlaştıran adaptördür.

## Dizin Yapısı

- [Locking/RedisDistributedLockAdapter](Locking/RedisDistributedLockAdapter.md) — [IAppDistributedLock](../CustomerSupportBot.Application/Ports/Outbound/Locking/IAppDistributedLock.md) portunu uygulayan; `Medallion.Threading.Redis` tabanlı dağıtık kilit adaptörü.
- [Messaging/RedisMessageBusAdapter](Messaging/RedisMessageBusAdapter.md) — [IMessageBusPort](../CustomerSupportBot.Application/Ports/Outbound/Messaging/IMessageBusPort.md) portunu uygulayan; Redis Pub/Sub üzerinden pod'lar arası asenkron olay yayını ve abonelik adaptörü.
- [HealthChecks/RedisHealthCheck](HealthChecks/RedisHealthCheck.md) — ASP.NET Core Health Checks için `IHealthCheck` uygulayıcısı.
- [Options/RedisOptions](Options/RedisOptions.md) — `RedisOptions` strongly-typed yapılandırma modeli.
- [DependencyInjection/RedisAdapterServiceCollectionExtensions](DependencyInjection/RedisAdapterServiceCollectionExtensions.md) — `AddRedisAdapters` DI kayıt uzantısı.
- [ExceptionTranslator](ExceptionTranslator.md) — Redis istisnalarını DomainException'a çevirici.

## Mimari Rolü ve Yetenekleri

- **RedLock Algoritması:** Aynı oturumda veya aynı sipariş üzerinde aynı anda birden fazla isteğin yarışmasını (Race Condition) önlemek için güvenli dağıtık kilit mekanizması.
- **Yatay Ölçeklendirme (Pub/Sub):** Çoklu container/pod ortamlarında canlı oturum güncellemelerinin ve olayların tüm sunuculara anında dağıtımı.
- **Otomatik Yeniden Bağlanma:** Bağlantı koptuğunda `ExponentialRetry(5000)` politikası ile otomatik iyileşme ve loglama.
