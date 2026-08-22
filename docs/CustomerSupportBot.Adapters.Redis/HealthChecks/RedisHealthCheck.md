# RedisHealthCheck

- **Kaynak:** `CustomerSupportBot.Adapters.Redis/HealthChecks/RedisHealthCheck.cs`
- **Tür:** `public sealed class : IHealthCheck`
- **Namespace:** `CustomerSupportBot.Adapters.Redis.HealthChecks`

## Ne işe yarar?

`RedisHealthCheck`, ASP.NET Core Health Checks altyapısına entegre olarak çalışan; Redis sunucusuna asenkron `PingAsync` komutu gönderip yanıt süresini ve erişilebilirliğini denetleyen sağlık kontrolcüsüdür.

## Hangi amaçla kullanılır`?

Kubernetes liveness/readiness probları ve `/healthz` uç noktalarında Redis altyapısının ayakta olduğunu doğrulamak için kullanılır.

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
