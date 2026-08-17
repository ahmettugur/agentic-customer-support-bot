# RedisMessageBusAdapter

**Dosya:** `Messaging/RedisMessageBusAdapter.cs`  
**Port:** `IMessageBusPort`  
**Kütüphane:** `StackExchange.Redis` pub/sub

---

## Neden message bus?

Multi-pod deployment'ta her pod kendi **in-memory cache**'ini taşır. Bir pod'da yapılan değişiklik diğer pod'larda görünmez:

```
Pod A: Approval ID=5 onaylandı, cache güncellendi → "Approved"
Pod B: Cache hâlâ "Pending"  ← stale!
```

**Çözüm:** Değişikliği pub/sub kanalına yayınla, diğer pod'lar abone olup cache'lerini güncellesin.

Bu pattern adapter katmanında uygulanır — Application bilmez. Adapter (örn. `PostgresApprovalQueue`):
1. DB'ye yazar
2. Cache'ini günceller
3. Redis'e event yayınlar
4. Diğer pod'lar event'i alır, kendi cache'lerini günceller

---

## Arayüz

```csharp
public interface IMessageBusPort
{
    string NodeId { get; }
    void Publish(string channel, string jsonPayload);
    void Subscribe(string channel, Action<string> handler);
}
```

---

## NodeId

```csharp
public string NodeId { get; } = Guid.NewGuid().ToString("N");
```

Her pod **benzersiz bir ID** alır (32-char hex GUID). Publish edilen mesajlara `senderNodeId` eklenir; subscriber kendi mesajını **echo etmemek** için bu ID'yi kontrol eder:

```csharp
// Adapter publish ederken:
bus.Publish("csbot:approval:decided", JsonSerializer.Serialize(new {
    senderNodeId = bus.NodeId,
    approvalId = "abc",
    status = "Approved"
}));

// Adapter subscribe ederken:
bus.Subscribe("csbot:approval:decided", json => {
    var msg = JsonSerializer.Deserialize<...>(json);
    if (msg.senderNodeId == bus.NodeId) return;   // kendi mesajım, atla
    // Diğer pod'dan geldi → cache güncelle
});
```

**Neden?** Aksi halde adapter kendi yayınladığı mesajı tekrar işleyip cache'i iki kez günceller veya circular update yaparz.

---

## `Publish`

```csharp
public void Publish(string channel, string jsonPayload)
{
    try
    {
        _subscriber.Publish(RedisChannel.Literal(channel), jsonPayload);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "[MessageBus] Redis publish başarısız: {Channel}", channel);
    }
}
```

- **Senkron** — Redis pub/sub fire-and-forget; abone yoksa mesaj kaybolur (durable değil)
- **Exception swallow** — Redis kopsa bile uygulama çökmez; warning loglanır
- `RedisChannel.Literal()` — exact match (pattern subscribe yok)

### Neden exception swallow?

Pub/sub bir **best-effort** mekanizma. Cache senkronizasyonu için kullanılır; mesaj kaybolursa diğer pod stale kalır ama:
- Sonraki request'te DB'den fresh okuma yapılır (lazy hydration)
- Periyodik full sync (örn. her N dakika) gerçekleşir

Bu yüzden tek mesaj kaybı kritik değil — uygulamayı durdurmaktansa devam etmek daha iyi.

---

## `Subscribe`

```csharp
public void Subscribe(string channel, Action<string> handler)
{
    _subscriber.Subscribe(RedisChannel.Literal(channel), (_, value) =>
    {
        try
        {
            handler(value.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[MessageBus] Handler hatası: {Channel}", channel);
        }
    });
}
```

- StackExchange.Redis kendi thread'ini kullanır — handler async olmak zorunda değil
- **Handler exception'ları yutulur** — bir mesajdaki bug subscribe'ı çökertmez
- Kanal kapatma yok — uygulama yaşam döngüsünce subscribe aktif

---

## Kanal isimlendirme konvansiyonu

```
csbot:<bölge>:<event>
```

Örnekler:

| Kanal | Yayıncı | Format (JSON) |
|---|---|---|
| `csbot:approval:created` | PostgresApprovalQueue | `{senderNodeId, approvalId, sessionId, toolName}` |
| `csbot:approval:decided` | PostgresApprovalQueue | `{senderNodeId, approvalId, status, decidedBy}` |
| `csbot:escalation:created` | PostgresEscalationSink | `{senderNodeId, escalationId, sessionId, reason}` |
| `csbot:escalation:decided` | PostgresEscalationSink | `{senderNodeId, escalationId, status}` |
| `csbot:bridge:touser` | PostgresChatBridge | `{senderNodeId, sessionId, message: {...}}` |
| `csbot:bridge:toadmin` | PostgresChatBridge | `{senderNodeId, sessionId, message: {...}}` |
| `csbot:chatmode` | PostgresChatModeRegistry | `{senderNodeId, sessionId, mode, agentId?}` |

`csbot:` prefix tüm uygulama mesajlarını ortak namespace altında toplar — Redis instance başka uygulamalarla paylaşılıyorsa çakışmayı önler.

---

## Subscribe akışı

```
Adapter constructor'da:
   _bus.Subscribe("csbot:approval:decided", HandleApprovalDecided);

Pod B karar verdi:
   Pod B → Publish("csbot:approval:decided", "{senderNodeId: B, ...}")
   Redis → tüm subscriber'lara fan-out
   ↓
Pod A → HandleApprovalDecided(json)
   → JSON parse
   → senderNodeId == A.NodeId ? return : continue
   → Cache update (Pod A artık güncel)
```

---

## Limitler

| Özellik | Var mı? |
|---|---|
| Durable delivery (mesaj kaybolmama) | ❌ — fire-and-forget |
| Mesaj sırası garantisi | ⚠️ — tek kanal içinde Redis sıralar |
| Acknowledgement (consumer onayı) | ❌ |
| Pattern subscribe (`csbot:*`) | ❌ kullanılmıyor — Literal match |
| Backpressure | ❌ — yavaş consumer mesaj kaybeder |

Mesaj **broker** değil — sadece cache sync notification. Durable mesajlaşma için Kafka/RabbitMQ gerekir.

---

## Performans

- Publish latency: ~1 ms (Redis LAN)
- Fan-out: O(subscriber sayısı) — Redis'in pub/sub overhead'i
- Mesaj boyutu: pratikte 1-5 KB (event metadata)

Yüksek frekanslı channel'lar (örn. `csbot:bridge:touser` her chat mesajı için) Redis CPU kullanımını artırabilir — production'da monitor edilmeli.

---

## Bağlantılar

- [Application IMessageBusPort](../CustomerSupportBot.Application/README.md)
- [PostgresChatBridge](../CustomerSupportBot.Adapters.Persistence/PostgresAdapters.md#postgreschatbridge) — pub/sub kullanan adapter örneği
