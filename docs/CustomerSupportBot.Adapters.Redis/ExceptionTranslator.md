# ExceptionTranslator

- **Kaynak:** `CustomerSupportBot.Adapters.Redis/ExceptionTranslator.cs`
- **Tür:** `internal static class`
- **Namespace:** `CustomerSupportBot.Adapters.Redis`

## Ne işe yarar?

`ExceptionTranslator`, `StackExchange.Redis` kütüphanesinden fırlatılan altyapı istisnalarını (`RedisConnectionException`, `RedisTimeoutException`, `RedisServerException`) Domain katmanındaki [ExternalServiceException](../CustomerSupportBot.Domain/Exceptions/ExternalServiceException.md) türüne dönüştürür.

## Hangi amaçla kullanılır`?

Application ve Domain katmanlarının StackExchange.Redis altyapısına doğrudan bağımlı olmasını engellemek ve Redis hatalarını merkezi standart bir domain hatası olarak sunmak için kullanılır.

## Metotlar ve İç Çalışma Mantıkları

### 1. `Translate`
```csharp
public static DomainException Translate(Exception ex, string? context = null)
```
- **Ne işe yarar?:** Redis istisnasını domain istisnasına dönüştürür.
- **İç Mantığı:**
  - `RedisConnectionException` ➔ `ExternalServiceException("Redis", "Redis bağlantısı kurulamadı.", ex)`
  - `RedisTimeoutException` ➔ `ExternalServiceException("Redis", "Redis işlemi zaman aşımına uğradı.", ex)`
  - `RedisServerException (BUSY)` ➔ `ExternalServiceException("Redis", "Redis sunucusu meşgul.", ex)`
  - Diğer ➔ `ExternalServiceException("Redis", "Redis işlemi başarısız oldu.", ex)`

## Bağımlılıklar

- [ExternalServiceException](../CustomerSupportBot.Domain/Exceptions/ExternalServiceException.md)
- `StackExchange.Redis.RedisException`
