# PostgresHealthCheck

- **Kaynak:** `CustomerSupportBot.Adapters.Persistence/HealthChecks/PostgresHealthCheck.cs`
- **Tür:** `public sealed class : IHealthCheck`
- **Namespace:** `CustomerSupportBot.Adapters.Persistence.HealthChecks`

## Ne işe yarar?

`PostgresHealthCheck`, ASP.NET Core Health Checks altyapısı için `IDbContextFactory<CustomerSupportDbContext>` üzerinden PostgreSQL veritabanına `SELECT 1` sorgusu göndererek veritabanı bağlantı durumunu test eden sağlık kontrolcüsüdür.

## Hangi Amaçla Kullanıldığı

ASP.NET Core'un `/health` uç noktası (Program.cs'te `MapHealthChecks` ile bağlanır) ve container orkestrasyon araçlarının (Docker/Kubernetes liveness-readiness probe'ları) uygulamanın PostgreSQL'e erişip erişemediğini anlamasını sağlar.

## Sorumlulukları

- `IDbContextFactory<CustomerSupportDbContext>` üzerinden **kısa ömürlü** bir `DbContext` açıp `SELECT 1` çalıştırarak gerçek bir round-trip testi yapmak (sadece connection string doğrulaması değil).
- Başarısızlığı `Unhealthy` sonucuna, istisnayı (`ex`) kaybetmeden dönmek — health check UI/log'larında kök nedeni görünür kılmak için.

**Üstlenmediği:** Redis, Qdrant gibi diğer bağımlılıkların sağlık kontrolü — her biri kendi `IHealthCheck` implementasyonuna sahiptir (ör. `Adapters.Redis` katmanındaki Redis health check).

## Diğer Katman ve Bileşenlerle İlişkileri

- `IDbContextFactory<CustomerSupportDbContext>`'i constructor injection (primary constructor sözdizimi) ile alır — `CustomerSupportDbContext`'in kendisini değil, factory'sini kullanır çünkü health check kendi kısa ömürlü context'ini açıp kapatmalı, uzun ömürlü bir context paylaşmamalı.
- `Program.cs`'te `AddHealthChecks().AddCheck<PostgresHealthCheck>(...)` ile kaydedilir.

## Kullanılma Nedeni ve Tasarım Yaklaşımı

`ExecuteSqlRawAsync("SELECT 1", ...)` seçimi — en ucuz, yan etkisiz sorgu; herhangi bir tabloya bağımlı olmadığından şema değişikliklerinden etkilenmez. `primary constructor` (C# 12 sözdizimi, `(IDbContextFactory<...> factory) : IHealthCheck`) — tek bağımlılıklı, durumsuz sınıflar için gereksiz boilerplate'i azaltmak amacıyla tercih edilmiştir.

## Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `CheckHealthAsync(context, cancellationToken)` | Veritabanı bağlantısını test eder; başarılıysa `Healthy`, hata durumunda istisnayı taşıyan `Unhealthy` döner. |

## Bağımlılıklar

- `IDbContextFactory<CustomerSupportDbContext>` (primary constructor parametresi)
- `Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck`
