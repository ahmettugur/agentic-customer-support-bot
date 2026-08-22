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

## Diğer Katman ve Bileşenlerle İlişkileri

- [`IMessageBusPort`](../../CustomerSupportBot.Application/Ports/Outbound/Messaging/IMessageBusPort.md) portunu uygular.
- `CustomerSupportBot.Adapters.Persistence` katmanındaki neredeyse tüm Postgres store/registry sınıfları (`PostgresSessionManager`, `PostgresApprovalQueue`, `PostgresChatBridge`, `PostgresChatModeRegistry`, `PostgresEscalationSink`, `PostgresSlaEventSink`, `PostgresReasoningTraceStore`, `PostgresLessonStore`, `PostgresRatingStore`, `PostgresCustomerProfileStore`, `PostgresHumanAgentRegistry`) bu portu enjekte eder ve **kendi payload'larına `nodeId = _messageBus.NodeId` ekleyip, gelen mesajda `nodeId` kendi `NodeId`'siyle eşleşiyorsa mesajı yok sayarak** kendi kendine yankılanmayı (self-echo) önler — bu filtreleme mantığı bu sınıfın DEĞİL, her tüketicinin kendi sorumluluğundadır; `RedisMessageBusAdapter` sadece ham `Publish`/`Subscribe` sağlar.
- `InMemory/InMemoryMessageBusAdapter.cs` aynı portun test/tekli-pod ortamı için bellek-içi kopyasıdır — üretimde Redis, testlerde/local'de InMemory kullanılır.

## Kullanılma Nedeni ve Tasarım Yaklaşımı

Bu sınıf bilinçli olarak **ince (thin)** tutulmuştur: sadece `Publish`/`Subscribe` sağlar, mesaj formatını veya self-echo filtrelemesini bilmez. Böylece Redis'e özgü hiçbir semantik Application katmanına sızmaz — `IMessageBusPort` portu "kanal + string payload" kadar basit bir soyutlamadır, her tüketici kendi JSON şemasını ve filtreleme kuralını kendi belirler. `NodeId`'nin `Guid.NewGuid()` ile örnek-başına (instance-per) üretilmesi, her pod/worker'ın süreç ömrü boyunca sabit ve benzersiz bir kimliğe sahip olmasını sağlar.

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
