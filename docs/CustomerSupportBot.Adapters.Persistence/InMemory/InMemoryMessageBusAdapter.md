# InMemoryMessageBusAdapter

- **Kaynak:** `CustomerSupportBot.Adapters.Persistence/InMemory/InMemoryMessageBusAdapter.cs`
- **Port:** `IMessageBusPort` (`CustomerSupportBot.Application.Ports.Outbound.Messaging`)
- **Namespace:** `CustomerSupportBot.Adapters.Persistence.InMemory`

## 1. Ne İşe Yarar

`IMessageBusPort`'un süreç-içi (in-process) implementasyonudur. `Redis`'in pod'lar arası pub/sub'ının aksine, burada `Publish` çağrısı doğrudan aynı process içindeki abone delegelerini (`Action<string>`) senkron olarak tetikler.

## 2. Hangi Amaçla Kullanıldığı

Redis Adapter'ın (`RedisMessageBusAdapter`, `Adapters.Redis` katmanı) yerini alan test/tek-pod karşılığıdır — çoklu pod senkronizasyonu (session cache invalidation, approval-queue event yayını) gerektirmeyen senaryolarda kullanılır.

## 3. Sorumlulukları

- Kanal bazlı abone listesi tutma (`ConcurrentDictionary<string, List<Action<string>>>`).
- `Publish(channel, jsonPayload)` — o kanala abone tüm handler'ları senkron çağırır; bir handler patlarsa **sessizce yutulur** (`catch { /* no-op */ }`) — tek process'te bir abonenin hatası diğerlerini etkilemesin diye.
- `Subscribe(channel, handler)` — kanala yeni bir handler ekler.
- `NodeId` — süreç başına rastgele üretilen bir GUID; "bu mesajı ben mi yayınladım" kontrolü için (self-echo önleme) kullanılabilir, port arayüzünün bir parçasıdır.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `RedisMessageBusAdapter` (`../../CustomerSupportBot.Adapters.Redis/`) ile aynı `IMessageBusPort` arayüzünü uygular.
- `PostgresSessionManager`, `PostgresApprovalQueue` gibi cache-senkronizasyonu gerektiren sınıflar bu portu (ya da Redis karşılığını) kullanır; hangisinin kayıtlı olduğu DI konfigürasyonuna bağlıdır.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Yorum satırında açıkça belirtildiği gibi: "Tek pod senaryolarında pub/sub mesajlaşma ihtiyacı yoktur" — bu sınıf gerçek bir mesaj kuyruğu simüle ETMEZ, sadece arayüz uyumluluğu için var olur; `Publish` ve `Subscribe` aynı process'te çağrıldığından "mesaj kaybı" riski (Redis'te olduğu gibi) yapısal olarak imkânsızdır.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `NodeId` | Süreç başına benzersiz kimlik (property). |
| `Publish(channel, jsonPayload)` | Kanala abone tüm handler'ları senkron, hataya toleranslı çağırır. |
| `Subscribe(channel, handler)` | Handler'ı kanal listesine ekler. |

## 7. Bağımlılıklar

- Yok.
