# Persistence — Veritabanı ve Kalıcılık

Bu doküman uygulamanın veri kalıcılık katmanını, EF Core yapısını ve InMemory/Postgres switch mekanizmasını anlatır.

---

## 1. Provider Switch

Uygulama iki persistence modunu destekler. Seçim `appsettings.json` üzerinden yapılır:

```json
{
  "Persistence": {
    "Provider": "Postgres"
  }
}
```

| Provider | Kullanım | Restart Davranışı |
|----------|----------|-------------------|
| **`Postgres`** (varsayılan) | Production ve development | Veriler kalıcı |
| **`InMemory`** | Test ve hızlı prototip | Tüm veriler kaybolur |

`AddPersistenceServices(config)` extension metodu, `Provider` değerine göre aynı interface'lere farklı implementasyonlar bağlar.

---

## 2. Interface → Implementasyon Mapping

| Interface | Postgres | InMemory |
|-----------|----------|----------|
| `ISessionManager` | `PostgresSessionManager` | `InMemorySessionManager` |
| `IReasoningTraceStore` | `PostgresReasoningTraceStore` | `InMemoryReasoningTraceStore` |
| `IApprovalQueue` | `PostgresApprovalQueue` | `InMemoryApprovalQueue` |
| `IEscalationSink` | `PostgresEscalationSink` | `InMemoryEscalationSink` |
| `IRatingStore` | `PostgresRatingStore` | `InMemoryRatingStore` |
| `IChatModeRegistry` | `PostgresChatModeRegistry` | `InMemoryChatModeRegistry` |
| `IChatBridge` | — | `InMemoryChatBridge` |
| `ICustomerProfileStore` | `PostgresCustomerProfileStore` | `InMemoryCustomerProfileStore` |
| `ILessonStore` | `PostgresLessonStore` | `InMemoryLessonStore` |
| `IWorkflowDefinitionStore` | `PostgresWorkflowDefinitionStore` | `InMemoryWorkflowDefinitionStore` |
| `ISlaEventSink` | `PostgresSlaEventSink` | `InMemorySlaEventSink` |

**Not**: `IChatBridge` her iki modda da in-memory'dir (gerçek zamanlı, geçici veri). `IChatModeRegistry` Postgres modunda kalıcı olarak `chat.session_modes` tablosuna yazar; uygulama restart'ında sohbet modları korunur.

Tüm store kayıtları `PersistenceServicesExtensions.AddPersistenceServices()` içindeki `Provider` koşuluna göre yapılır. `ApplicationServicesExtensions` artık hiçbir store kaydı içermez.

---

## 3. EF Core Yapısı

### DbContext

`CustomerSupportDbContext` — EF Core 10 + Npgsql (PostgreSQL):

```csharp
public class CustomerSupportDbContext : DbContext
{
    // chat schema
    DbSet<SessionEntity>
    DbSet<MessageEntity>
    DbSet<ChatSessionModeEntity>
    DbSet<ChatBridgeMessageEntity>
    // hitl schema
    DbSet<ApprovalRequestEntity>
    DbSet<EscalationEntity>
    // observability schema
    DbSet<ReasoningTraceEntity>
    // analytics schema
    DbSet<RatingEntity>
    DbSet<SlaEventEntity>
    // auth schema
    DbSet<UserEntity>
    DbSet<RefreshTokenEntity>
    // personalization schema
    DbSet<CustomerProfileEntity>
    // improvement schema
    DbSet<LessonEntity>
    // workflow schema
    DbSet<WorkflowDefinitionEntity>
}
```

### Entity Organizasyonu

Entity'ler ve konfigürasyonlar alan bazlı organize edilmiştir:

```
Infrastructure/Persistence/
├── Entities/
│   ├── Auth/            → UserEntity, RefreshTokenEntity
│   ├── Chat/            → SessionEntity, MessageEntity, ChatSessionModeEntity, ChatBridgeMessageEntity
│   ├── Hitl/            → ApprovalRequestEntity, EscalationEntity, HumanAgentEntity
│   ├── Analytics/       → RatingEntity, SlaEventEntity
│   ├── Observability/   → ReasoningTraceEntity
│   ├── Personalization/ → CustomerProfileEntity
│   ├── Improvement/     → LessonEntity
│   └── Workflow/        → WorkflowDefinitionEntity
├── Configurations/
│   ├── Auth/            → UserConfiguration, RefreshTokenConfiguration
│   ├── Chat/            → SessionConfiguration, MessageConfiguration, ...
│   ├── Hitl/            → ApprovalRequestConfiguration, ...
│   ├── Analytics/       → RatingConfiguration, SlaEventConfiguration
│   ├── Observability/   → ReasoningTraceConfiguration
│   ├── Personalization/ → CustomerProfileConfiguration
│   ├── Improvement/     → LessonConfiguration
│   └── Workflow/        → WorkflowDefinitionConfiguration
└── Migrations/          → Code-first migration dosyaları
```

### Schema Haritası

| PostgreSQL Şeması | Tablolar |
|-------------------|----------|
| `chat` | sessions, messages, session_modes, bridge_messages |
| `hitl` | approvals, escalations, human_agents |
| `observability` | reasoning_traces |
| `analytics` | ratings, sla_events |
| `auth` | users, refresh_tokens |
| `personalization` | customer_profiles |
| `improvement` | lessons |
| `workflow` | workflow_definitions |

### JSONB Mapping

`SessionEntity.StateJson` alanı PostgreSQL JSONB olarak saklanır. `SessionState` nesnesi serialize/deserialize edilir:

```csharp
public class SessionEntity
{
    public string Id { get; set; }
    public string StateJson { get; set; }  // → SessionState JSONB
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

### Schema Yönetimi

`Schemas` sınıfı tablo schema isimlerini tanımlar. Tüm konfigürasyonlar `ApplyConfigurationsFromAssembly` ile otomatik yüklenir.

---

## 4. IDbContextFactory Kullanımı

Singleton servisler (ör. `PostgresSessionManager`) kısa ömürlü `DbContext` instance'ları oluşturmak için `IDbContextFactory<CustomerSupportDbContext>` kullanır:

```csharp
public class PostgresSessionManager : ISessionManager
{
    private readonly IDbContextFactory<CustomerSupportDbContext> _factory;

    public async Task<AgentSession> GetOrCreateSessionAsync(string? sessionId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        // ...
    }
}
```

Bu pattern, singleton servislerin `DbContext`'i uzun süre tutmasını (memory leak, stale data) önler.

---

## 5. Migration Stratejisi

### Development Ortamı

`MigrateIfDevelopmentAsync` — Development ortamında uygulama başlarken migration'lar otomatik çalışır:

```csharp
// Program.cs
await app.MigrateIfDevelopmentAsync();
```

### Yeni Migration Oluşturma

```powershell
cd CustomerSupportBot
dotnet ef migrations add <MigrationName>
dotnet ef database update
```

### PersistenceHydrator

`IHostedService` olarak çalışır. Uygulama başlarken seed data oluşturur (ör. default admin kullanıcı, demo veriler).

---

## 6. Connection Strings

```json
{
  "ConnectionStrings": {
    "PostgreSQL": "Host=localhost;Port=5433;Database=CustomerSupportDb;Username=postgres;Password=...;Minimum Pool Size=10;Maximum Pool Size=100",
    "Redis": "localhost:6379,abortConnect=false,connectTimeout=5000"
  }
}
```

| Bağlantı | Port | Açıklama |
|----------|------|----------|
| **PostgreSQL** | 5433 (docker) → 5432 (container) | Ana kalıcı veri deposu |
| **Redis** | 6379 | **Zorunlu** — distributed lock altyapısı (bağlantı string yoksa uygulama başlamaz) |

---

## 7. Distributed Lock

Uygulama, aynı kaynağa eşzamanlı erişimi serialize etmek için Redis tabanlı distributed lock kullanır.

### Implementasyon

| Sınıf | Paket | Kullanım |
|-------|-------|----------|
| `RedisDistributedLock` | `DistributedLock.Redis` v1.1.1 (Medallion.Threading) | Production — tüm ortamlar |
| `InMemoryDistributedLock` | — (test projesi) | Yalnızca birim testleri |

`RedisServicesExtensions.AddRedisServices()` Redis bağlantı string'i yoksa `InvalidOperationException` fırlatır — Redis her zaman zorunludur.

### Lock Key'leri

| Key Şablonu | Kullanan Servis | Amaç |
|-------------|-----------------|------|
| `csbot:lock:profile:{customerId}` | `CustomerProfileService` | Per-customer profil güncellemelerini serialize et |
| `csbot:lock:session:{sessionId}` | `ISessionManager.MutateStateAsync` | Session state atomic mutasyonu |

### Konfigürasyon

```json
{
  "Redis": {
    "KeyPrefix": "csbot",
    "DefaultLockTimeoutSeconds": 10,
    "LockExpirySeconds": 30
  }
}
```

| Parametre | Varsayılan | Açıklama |
|-----------|-----------|----------|
| `KeyPrefix` | `csbot` | Tüm lock key'lerinin öneki — multi-tenant çakışmasını önler |
| `DefaultLockTimeoutSeconds` | `10` | Lock alınamazsa bu süre sonunda `TimeoutException` |
| `LockExpirySeconds` | `30` | Lock TTL — process çöküse otomatik release |

### Multi-Pod Davranışı

Tüm pod'lar aynı Redis'e bağlandığı için lock **gerçekten global**'dir. Redis bağlantısı kesilirse `TryAcquireAsync` null döner, `AcquireAsync` `TimeoutException` fırlatır.

> ⚠️ **Multi-pod deployment için Redis Sentinel veya Cluster** kullanılması önerilir. Tek Redis node, lock altyapısının SPOF'udur.

---

## 9. InMemory Modu Detayları

InMemory modda veriler bellekte tutulur ve uygulama restart'ında kaybolur:

| Store | Sınır |
|-------|-------|
| `InMemorySessionManager` | Sınırsız (RAM) |
| `InMemoryReasoningTraceStore` | Ring buffer, max 500 trace |
| `InMemoryApprovalQueue` | Ring buffer, max 200 recent |
| `InMemoryEscalationSink` | Sınırsız (RAM) |
| `InMemoryRatingStore` | Sınırsız (RAM) |
| `InMemoryCustomerProfileStore` | Sınırsız (RAM) |
| `InMemoryLessonStore` | Sınırsız (RAM) |
| `InMemoryWorkflowDefinitionStore` | Sınırsız (RAM) |
| `InMemorySlaEventSink` | Ring buffer, max 500 olay |

`InMemorySessionManager` tek bir singleton olarak oluşturulup `ISessionManager` ve `IConversationStore` interface'lerine aynı instance üzerinden bağlanır.

> ⚠️ **Production uyarısı**: InMemory modda `CustomerProfile`, `Lesson`, `WorkflowDefinition` ve `SlaEvent` verileri uygulama restart'ında kaybolur. Production'da her zaman `Persistence:Provider = "Postgres"` kullanın.

---

## 10. Diğer Veri Depoları

### Qdrant (Vektör Veritabanı)

Semantic memory için kullanılır. EF Core dışında, `Qdrant.Client` gRPC ile doğrudan erişilir:

| Collection | İçerik |
|------------|--------|
| `cs_knowledge` | KnowledgeBase/*.md chunk'ları |
| `cs_episodic` | Konuşma geçmişi vektörleri |
| `cs_lessons` | Onaylanmış iyileştirme dersleri |

Detay → [intelligence.md](intelligence.md).

### FakeDatabase

Demo amaçlı in-memory product/order/complaint deposu. Static seed verilerle başlar ve uygulama restart'ında sıfırlanır.

---

## 11. CORS Yapılandırması

CORS policy `appsettings.json` üzerinden kontrol edilir. `Cors:AllowedOrigins` boş bırakılırsa tüm origin'lere izin verilir (development). Production'da belirli origin listesi tanımlanmalıdır:

```json
{
  "Cors": {
    "AllowedOrigins": [
      "https://your-frontend.example.com",
      "https://admin.example.com"
    ]
  }
}
```

| `AllowedOrigins` Değeri | Davranış |
|-------------------------|----------|
| Boş dizi `[]` | `AllowAnyOrigin()` — development için |
| Dolu liste | `WithOrigins(...)` — production için |

---

## Çapraz Referanslar

- **Mimari + DI haritası** → [architecture.md](architecture.md)
- **Konfigürasyon** → [runtime.md](runtime.md#2-konfigürasyon)
- **Docker kurulumu** → [deployment.md](deployment.md)
- **Semantic memory** → [intelligence.md](intelligence.md)
- **Blazor frontend** → [frontend.md](frontend.md)
