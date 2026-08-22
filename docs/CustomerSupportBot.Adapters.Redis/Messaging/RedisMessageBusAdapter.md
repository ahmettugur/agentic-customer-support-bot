# RedisMessageBusAdapter

- **Kaynak:** `CustomerSupportBot.Adapters.Redis/Messaging/RedisMessageBusAdapter.cs`
- **Tür:** `public sealed class : IMessageBusPort`
- **Namespace:** `CustomerSupportBot.Adapters.Redis.Messaging`

## Ne işe yarar?

`RedisMessageBusAdapter`, Application katmanındaki [IMessageBusPort](../../CustomerSupportBot.Application/Ports/Outbound/Messaging/IMessageBusPort.md) portunu uygulayan; `StackExchange.Redis` Pub/Sub mekanizması üzerinden çoklu pod/sunucu ortamlarında yatay ölçeklendirme mesajlaşmasını sağlayan adaptördür.

## Hangi amaçla kullanılır`?

- Bir sunucuda oluşan oturum durumu değişiklikleri veya HITL onay olaylarının diğer tüm çalışan pod'lara anında yayınlanması.
- Uygulama düğümüne benzersiz bir `NodeId` (`Guid.NewGuid().ToString("N")`) atayarak mesajın kendi kendine yankılanmasını engellemek.

## Sorumlulukları

- **Üstlendiği:**
  - `Publish` ile belirtilen kanala JSON yükünü yayınlamak.
  - `Subscribe` ile belirtilen kanala dinleyici bağlamak ve gelen mesajı işleyici fonksiyona (`handler`) iletmek.
  - Olası ağ hatalarında ve işleyici istisnalarında loglama yaparak uygulamanın çökmesini engellemek.

## Constructor ve Başlatma Mantığı

```csharp
public RedisMessageBusAdapter(
    IConnectionMultiplexer redis,
    ILogger<RedisMessageBusAdapter> logger)
```

### Constructor İçerisinde Yapılan İşler:
- `_subscriber`: `redis.GetSubscriber()` çağrılarak Redis Pub/Sub arayüzü alınır.
- `_logger`: Günlükleme motoru atanır.
- `NodeId`: Çalışan instance için benzersiz 32 karakterlik GUID string oluşturulur.

## Metotlar ve İç Çalışma Mantıkları

### 1. `Publish`
```csharp
public void Publish(string channel, string jsonPayload)
```
- **Ne işe yarar?:** Verilen kanala mesaj yayınlar.
- **İç Mantığı:** `_subscriber.Publish(RedisChannel.Literal(channel), jsonPayload)` işletilir. Hata fırlatılırsa catch bloğunda uyarı loglanır.

### 2. `Subscribe`
```csharp
public void Subscribe(string channel, Action<string> handler)
```
- **Ne işe yarar?:** Kanala abone olur ve gelen mesajları `handler` fonksiyonuna aktarır.
- **İç Mantığı:** `_subscriber.Subscribe(RedisChannel.Literal(channel), (_, value) => ...)` çağrılır; gelen değer string'e çevrilerek güvenli `try/catch` içinde `handler`'a teslim edilir.

## Özellikler (Properties)

| Özellik | Tür | Açıklama |
|---|---|---|
| `NodeId` | `string` | Mevcut sunucu düğümünün benzersiz kimliği. |

## Bağımlılıklar

- [IMessageBusPort](../../CustomerSupportBot.Application/Ports/Outbound/Messaging/IMessageBusPort.md)
- `StackExchange.Redis.ISubscriber`
- `StackExchange.Redis.IConnectionMultiplexer`
