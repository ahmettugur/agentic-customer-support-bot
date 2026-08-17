# HealthCheck & ExceptionTranslator

**Dosyalar:**
- `HealthChecks/RedisHealthCheck.cs`
- `ExceptionTranslator.cs`

İki küçük altyapı sınıfı — Redis durumu raporlama ve exception mapping.

---

## RedisHealthCheck

ASP.NET Core'un `IHealthCheck` interface'ini implement eder.

```csharp
public sealed class RedisHealthCheck(IConnectionMultiplexer redis) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = redis.GetDatabase();
            await db.PingAsync();
            return HealthCheckResult.Healthy("Redis erişilebilir.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Redis erişilemiyor.", ex);
        }
    }
}
```

### Ne yapar?

`PING` komutu Redis'e gönderir, cevap gelirse `Healthy`. Çok hafif (Redis kendi içinde PING'i optimize eder).

### Kullanım

`Program.cs`:

```csharp
services.AddHealthChecks()
    .AddCheck<RedisHealthCheck>("redis", tags: new[] { "ready" });

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = c => c.Tags.Contains("ready")
});
```

Kubernetes readiness probe için:

```yaml
readinessProbe:
  httpGet:
    path: /health/ready
    port: 8080
  periodSeconds: 10
```

Redis kopuksa pod traffic almaz — service mesh otomatik trafiği başka pod'a yönlendirir.

### Liveness vs Readiness

| Tip | Redis kontrolü olmalı mı? |
|---|---|
| **Liveness** (pod ölü mü?) | ❌ — Redis kopması pod'u kill etmemeli; restart fayda etmez |
| **Readiness** (trafik alabilir mi?) | ✅ — Redis yoksa adapter'lar çalışmaz, trafik almasın |

`RedisHealthCheck` readiness için kullanılır.

---

## ExceptionTranslator

```csharp
internal static class ExceptionTranslator
{
    public static DomainException Translate(Exception ex, string? context = null)
    {
        return ex switch
        {
            RedisConnectionException =>
                new ExternalServiceException("Redis",
                    context ?? "Redis bağlantısı kurulamadı.", ex),

            RedisTimeoutException =>
                new ExternalServiceException("Redis",
                    context ?? "Redis işlemi zaman aşımına uğradı.", ex),

            RedisServerException { Message: var msg } when msg.Contains("BUSY") =>
                new ExternalServiceException("Redis",
                    context ?? "Redis sunucusu meşgul.", ex),

            _ => new ExternalServiceException("Redis",
                    context ?? "Redis işlemi başarısız oldu.", ex)
        };
    }
}
```

### Eşleme tablosu

| Gelen Exception | Koşul | Çıkan Exception |
|---|---|---|
| `RedisConnectionException` | — | `ExternalServiceException("Redis", "bağlantı")` |
| `RedisTimeoutException` | — | `ExternalServiceException("Redis", "timeout")` |
| `RedisServerException` | Message contains `"BUSY"` | `ExternalServiceException("Redis", "meşgul")` |
| Diğer | — | `ExternalServiceException("Redis", "başarısız")` (orijinal sarılır) |

### Neden hepsi `ExternalServiceException`?

Redis bu sistem için **dışsal bir servis** (3rd-party). Domain açısından Redis'in bağlantı problemi, timeout'u veya server hatası **aynı kategoride**: "dış servis erişilemedi/yanıt vermedi".

API katmanı bu exception'ı görür → `502 Bad Gateway` döner. Client retry yapabilir.

### Kullanım deseni

`RedisDistributedLockAdapter`'da:

```csharp
catch (RedisException ex)
{
    throw ExceptionTranslator.Translate(ex, $"Lock acquire hatası: {resourceKey}");
}
```

`context` parametresi log'larda hangi işlemin başarısız olduğunu anlatır. `RedisMessageBusAdapter` exception'ı **yutar** (cache sync best-effort) — translator kullanmaz.

### "BUSY" özel durumu

Redis'in `BUSY Redis is busy running a script` hatası — uzun süren Lua script çalışıyor. Bu **transient** bir hata; retry mantıklı. Mesaj farklı tutuluyor ki logging'de ayırt edilebilsin.

---

## Internal mi public mi?

`ExceptionTranslator` **internal** — yalnızca `Adapters.Redis` projesi içinden kullanılır. Diğer projeler `DomainException` hiyerarşisini doğrudan yakalar.

Bu adapter encapsulation'ın bir parçası: StackExchange.Redis tipleri (RedisException, RedisConnectionException, vb.) Adapter dışına sızmaz.

---

## Bağlantılar

- [Domain Exceptions](../CustomerSupportBot.Domain/Exceptions/DomainException.md) — `ExternalServiceException` tanımı
- [Adapters.Persistence ExceptionTranslator](../CustomerSupportBot.Adapters.Persistence/ExceptionTranslator.md) — paralel desen (PostgreSQL için)
