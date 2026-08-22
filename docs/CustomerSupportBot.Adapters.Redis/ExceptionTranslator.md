# ExceptionTranslator

- **Kaynak:** `CustomerSupportBot.Adapters.Redis/ExceptionTranslator.cs`
- **Tür:** `internal static class`
- **Namespace:** `CustomerSupportBot.Adapters.Redis`

## Ne işe yarar?

`ExceptionTranslator`, `StackExchange.Redis` kütüphanesinden fırlatılan altyapı istisnalarını (`RedisConnectionException`, `RedisTimeoutException`, `RedisServerException`) Domain katmanındaki [ExternalServiceException](../CustomerSupportBot.Domain/Exceptions/ExternalServiceException.md) türüne dönüştürür.

## Hangi amaçla kullanılır`?

Application ve Domain katmanlarının StackExchange.Redis altyapısına doğrudan bağımlı olmasını engellemek ve Redis hatalarını merkezi standart bir domain hatası olarak sunmak için kullanılır.

## Sorumlulukları

- **Üstlendiği:** Yalnızca bilinen `StackExchange.Redis` istisna tiplerini sınıflandırıp anlamlı Türkçe mesajlarla `ExternalServiceException`'a sarmak.
- **Üstlenmediği:** Retry/tekrar deneme mantığı — bu sınıf sadece dönüştürür, hatayı yutmaz veya tekrar denemez; çağıran kod (`RedisDistributedLockAdapter`) `throw` ile yeniden fırlatır.

## Diğer Katman ve Bileşenlerle İlişkileri

- Yalnızca [`RedisDistributedLockAdapter`](Locking/RedisDistributedLockAdapter.md) tarafından çağrılır — `RedisMessageBusAdapter` hataları kendi içinde loglayıp yutar, domain istisnasına çevirmez (pub/sub'da tek bir yayın başarısız olursa turu durdurmaya değmez).
- `CustomerSupportBot.Adapters.AI`, `Adapters.Agents`, `Adapters.Persistence` projelerinin her birinde aynı isimde ve aynı sorumlulukta kendi `internal ExceptionTranslator`'ı vardır — bu bilinçli bir tekrardır, bkz. aşağıdaki tasarım notu.

## Kullanılma Nedeni ve Tasarım Yaklaşımı

> 🐞 **Neden her adapter projesinde ayrı bir `ExceptionTranslator` var, paylaşılan bir tane değil:**
> Her adapter kendi altyapı kütüphanesine özgü istisna tiplerini (`RedisException`, Qdrant HTTP hataları, Npgsql hataları vb.) bilir. Paylaşılan tek bir çevirici, ya tüm adapter projelerine bağımlı olurdu (bağımlılık yönü bozulur) ya da tip kontrolünü `is`/reflection ile genelleştirmek zorunda kalırdı. Küçük, projeye özel, `internal` bir sınıf olarak tutmak hem hexagonal sınırları korur hem de her adapter'ın kendi hata sözlüğünü bağımsız evrimleştirmesine izin verir.

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
