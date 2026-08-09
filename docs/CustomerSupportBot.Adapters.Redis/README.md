# CustomerSupportBot.Adapters.Redis

Redis tabanlı **dağıtık altyapı** adapter'ları. İki ana hizmet sunar:

1. **Distributed Lock** — `IAppDistributedLock` (Medallion RedLock)
2. **Message Bus** — `IMessageBusPort` (Redis pub/sub)

Ek olarak: health check, exception translator, DI extension.

---

## Redis zorunlu mu?

**Evet, zorunlu.** Bağlantı dizisi yoksa uygulama startup'ta `InvalidOperationException` fırlatır.

### Neden?

- **Distributed lock** olmazsa multi-pod ortamda race condition'lar oluşur:
  - İki admin aynı approval'ı aynı anda onaylar → duplicate karar
  - İki pod aynı session için TakeOver yapar → conflicting state
- **Pub/sub** olmazsa multi-pod cache senkronizasyonu çalışmaz:
  - Pod A'da yapılan değişiklik Pod B'nin cache'inde görünmez
  - Live chat mesajları doğru pod'a ulaşmaz

Tek pod ile dev/test yapıyor olsan bile Redis lazım — uygulama mimari olarak Redis varlığını varsayar.

---

## Klasör yapısı

```
CustomerSupportBot.Adapters.Redis/
├── DependencyInjection/
│   └── RedisAdapterServiceCollectionExtensions.cs
├── HealthChecks/
│   └── RedisHealthCheck.cs
├── Locking/
│   └── RedisDistributedLockAdapter.cs
├── Messaging/
│   └── RedisMessageBusAdapter.cs
├── ExceptionTranslator.cs
└── RedisOptions.cs
```

---

## Dokümantasyon haritası

| Doküman | Kapsam |
|---|---|
| [DependencyInjection.md](DependencyInjection.md) | `AddRedisAdapters`, connection string çözümleme, retry policy, RedisOptions |
| [DistributedLock.md](DistributedLock.md) | RedisDistributedLockAdapter — Medallion RedLock |
| [MessageBus.md](MessageBus.md) | RedisMessageBusAdapter — pub/sub kanalları, NodeId |
| [HealthCheck.md](HealthCheck.md) | RedisHealthCheck + ExceptionTranslator |

---

## Port → Adapter eşlemesi

| Port (Application) | Adapter (Redis) |
|---|---|
| `IAppDistributedLock` | `RedisDistributedLockAdapter` |
| `IMessageBusPort` | `RedisMessageBusAdapter` |

Her ikisi de `Singleton` olarak DI'a kaydedilir — `IConnectionMultiplexer` paylaşılan.

---

## Bağımlılıklar

| Paket | Amaç |
|---|---|
| `StackExchange.Redis` | Redis client (multiplexer, pub/sub, DB) |
| `Medallion.Threading.Redis` | RedLock algoritması (dağıtık lock) |

---

## Kullanım örneği

`Program.cs`:

```csharp
services.AddRedisAdapters(configuration);
```

`appsettings.json`:

```json
{
  "Redis": {
    "ConnectionString": "localhost:6379",
    "KeyPrefix": "csbot",
    "DefaultLockTimeoutSeconds": 10,
    "LockExpirySeconds": 30
  }
}
```

Alternatif olarak `ConnectionStrings:Redis` da kullanılabilir.

---

## Pub/sub kanal kataloğu

Adapter **hardcoded kanal tanımlamaz** — Application/Adapters katmanları runtime'da kanal adı geçer. Projedeki tanımlı kanallar:

| Kanal | Yayınlayan | Amaç |
|---|---|---|
| `csbot:approval:created` | PostgresApprovalQueue | Yeni approval bildirimi |
| `csbot:approval:decided` | PostgresApprovalQueue | Karar bildirimi |
| `csbot:escalation:created` | PostgresEscalationSink | Yeni escalation |
| `csbot:escalation:decided` | PostgresEscalationSink | Escalation karar |
| `csbot:bridge:touser` | PostgresChatBridge | Bot/Admin → kullanıcı mesajları |
| `csbot:bridge:toadmin` | PostgresChatBridge | Kullanıcı → admin mesajları |
| `csbot:chatmode` | PostgresChatModeRegistry | Bot/Human mod değişikliği |

Detay için ilgili adapter dokümanlarına bak: [PostgresAdapters.md](../adapters-persistence/PostgresAdapters.md).
