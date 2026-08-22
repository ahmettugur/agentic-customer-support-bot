# RedisHealthCheck

- **Kaynak:** `CustomerSupportBot.Adapters.Redis/HealthChecks/RedisHealthCheck.cs`
- **Tür:** `public sealed class : IHealthCheck`
- **Namespace:** `CustomerSupportBot.Adapters.Redis.HealthChecks`

## Ne işe yarar?

`RedisHealthCheck`, ASP.NET Core Health Checks altyapısına entegre olarak çalışan; Redis sunucusuna asenkron `PingAsync` komutu gönderip yanıt süresini ve erişilebilirliğini denetleyen sağlık kontrolcüsüdür.

## Hangi amaçla kullanılır`?

Kubernetes liveness/readiness probları ve `/healthz` uç noktalarında Redis altyapısının ayakta olduğunu doğrulamak için kullanılır.

## Sorumlulukları

- **Üstlendiği:** Redis'e tek bir hafif `PING` komutu göndermek ve sonucu ASP.NET Core'un standart `HealthCheckResult` formatına çevirmek.
- **Üstlenmediği:** Kilit/mesajlaşma altyapısının işlevsel doğruluğunu test etmek — sadece bağlantının canlı olduğunu doğrular, RedLock veya Pub/Sub'ın çalıştığını değil.

## Diğer Katman ve Bileşenlerle İlişkileri

- `CustomerSupportBot.Api/Extensions/HealthCheckExtensions.cs` içinde `sp => new RedisHealthCheck(sp.GetRequiredService<IConnectionMultiplexer>())` factory'siyle elle kaydedilir (DI konteynerine otomatik kayıt yoktur, Api katmanı health-check pipeline'ını orkestre eder).
- Aynı `IConnectionMultiplexer` singleton'ını [`RedisAdapterServiceCollectionExtensions`](../DependencyInjection/RedisAdapterServiceCollectionExtensions.md)'ın kaydettiği örnekle paylaşır — ayrı bir bağlantı açmaz.

## Kullanılma Nedeni ve Tasarım Yaklaşımı

`IHealthCheck` arayüzünü uygulayan standart bir ASP.NET Core sağlık kontrolcüsüdür; bu sayede Kubernetes/orkestrasyon katmanı `/healthz` uç noktasından Redis'in erişilebilirliğini sorgulayabilir. `PingAsync` seçilmiştir çünkü en düşük maliyetli, yan etkisiz doğrulama komutudur — veri okuma/yazma gerektirmez.

## Constructor ve Başlatma Mantığı

```csharp
public RedisHealthCheck(IConnectionMultiplexer redis)
```

### Constructor İçerisinde Yapılan İşler:
- `redis` (`IConnectionMultiplexer`): Bağlantı havuzu birincil constructor (primary constructor) ile enjekte edilir.

## Metotlar ve İç Çalışma Mantıkları

### 1. `CheckHealthAsync`
```csharp
public async Task<HealthCheckResult> CheckHealthAsync(
    HealthCheckContext context,
    CancellationToken cancellationToken = default)
```
- **Ne işe yarar?:** Redis erişimini doğrular.
- **İç Mantığı:** `redis.GetDatabase().PingAsync()` çağrılır. Başarılıysa `HealthCheckResult.Healthy("Redis erişilebilir.")`, istisna fırlatılırsa `HealthCheckResult.Unhealthy("Redis erişilemiyor.", ex)` döner.

## Bağımlılıklar

- `Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck`
- `StackExchange.Redis.IConnectionMultiplexer`
