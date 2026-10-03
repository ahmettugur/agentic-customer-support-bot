# RedisIdempotencyStore

**Dosya:** `Idempotency/RedisIdempotencyStore.cs`
**Namespace:** `CustomerSupportBot.Adapters.Redis.Idempotency`
**Port:** [`IDistributedIdempotencyStore`](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IDistributedIdempotencyStore.md)

## 1. Ne İşe Yarar

Yan etkili tool çağrılarının mükerrer tespit kaydını Redis'te, pod'lar arası paylaşımlı tutar.

## 2. Hangi Amaçla Kullanılır

[`SideEffectIdempotencyCache`](../../CustomerSupportBot.Application/Services/Tools/SideEffectIdempotencyCache.md)'in
dağıtık katmanı olarak: aynı sipariş/şikayet isteği başka bir pod'a düştüğünde ikinci kaydın
oluşmasını önlemek.

## 3. Sorumlulukları

- **Üstlendiği:** `csbot:idempotency:{key}` anahtarına kimliği TTL ile yazmak (`SET NX PX`) ve okumak.
- **Üstlenmediği:** Hata toleransı — Redis hatası çağırana fırlatılır; fail-open kararı
  `SideEffectIdempotencyCache`'tedir.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

[`RedisAdapterServiceCollectionExtensions`](../DependencyInjection/RedisAdapterServiceCollectionExtensions.md)
singleton olarak kaydeder. API entegrasyon testleri gerçek Redis olmadan çalıştığı için
`TestWebApplicationFactory` bu kaydı kaldırır.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

`When.NotExists` ile yazılır: ilk oluşturulan kayıt kanoniktir. Senkron Redis API'si bilinçli
seçimdir (tool'lar senkron çalışır; StackExchange.Redis'in senkron çağrıları birinci sınıf yoldur).

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `string? Get(string key)` | `StringGet`; değer yoksa `null`. |
| `void Set(string key, string entityId, TimeSpan ttl)` | `StringSet(..., ttl, When.NotExists)`. |

## 7. Bağımlılıklar

- `IConnectionMultiplexer`

## Bağlantılar

- [IDistributedIdempotencyStore](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IDistributedIdempotencyStore.md)
- Testler: `tests/CustomerSupportBot.Adapters.Redis.Tests/RedisIdempotencyStoreTests.cs` (Testcontainers Redis)
