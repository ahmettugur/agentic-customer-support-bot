# PostgresHealthCheck

- **Kaynak:** `CustomerSupportBot.Adapters.Persistence/HealthChecks/PostgresHealthCheck.cs`
- **Tür:** `public sealed class : IHealthCheck`
- **Namespace:** `CustomerSupportBot.Adapters.Persistence.HealthChecks`

## Ne işe yarar?

`PostgresHealthCheck`, ASP.NET Core Health Checks altyapısı için `IDbContextFactory<CustomerSupportDbContext>` üzerinden PostgreSQL veritabanına `SELECT 1` sorgusu göndererek veritabanı bağlantı durumunu test eden sağlık kontrolcüsüdür.

## Metotlar ve İç Çalışma Mantıkları

### 1. `CheckHealthAsync`
```csharp
public async Task<HealthCheckResult> CheckHealthAsync(
    HealthCheckContext context,
    CancellationToken cancellationToken = default)
```
- **Ne işe yarar?:** Veritabanı bağlantısını test eder; başarılıysa `Healthy`, hata durumunda `Unhealthy` döner.

## Bağımlılıklar

- `Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck`
- [CustomerSupportDbContext](../EfCore/CustomerSupportDbContext.md)
