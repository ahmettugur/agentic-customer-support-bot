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
| `IEscalationSink` | DB-backed | `InMemoryEscalationSink` |
| `IRatingStore` | `PostgresRatingStore` | `InMemoryRatingStore` |
| `IChatModeRegistry` | `PostgresChatModeRegistry` | `InMemoryChatModeRegistry` |
| `IChatBridge` | — | `InMemoryChatBridge` |

**Not**: `IChatBridge` her iki modda da in-memory'dir (gerçek zamanlı, geçici veri). `IChatModeRegistry` Postgres modunda kalıcı olarak `chat.session_modes` tablosuna yazar; uygulama restart'ında sohbet modları korunur.

---

## 3. EF Core Yapısı

### DbContext

`CustomerSupportDbContext` — EF Core 10 + Npgsql (PostgreSQL):

```csharp
public class CustomerSupportDbContext : DbContext
{
    DbSet<SessionEntity>
    DbSet<MessageEntity>
    DbSet<ChatSessionModeEntity>
    DbSet<ChatBridgeMessageEntity>
    DbSet<ApprovalRequestEntity>
    DbSet<EscalationEntity>
    DbSet<ReasoningTraceEntity>
    DbSet<RatingEntity>
    DbSet<UserEntity>
    DbSet<RefreshTokenEntity>
}
```

### Entity Organizasyonu

Entity'ler ve konfigürasyonlar alan bazlı organize edilmiştir:

```
Infrastructure/Persistence/
├── Entities/
│   ├── Auth/          → UserEntity, RefreshTokenEntity
│   ├── Chat/          → SessionEntity, MessageEntity, ChatSessionModeEntity, ChatBridgeMessageEntity
│   ├── Hitl/          → ApprovalRequestEntity, EscalationEntity
│   ├── Analytics/     → RatingEntity
│   └── Observability/ → ReasoningTraceEntity
├── Configurations/
│   ├── Auth/          → UserConfiguration, RefreshTokenConfiguration
│   ├── Chat/          → SessionConfiguration, MessageConfiguration, ...
│   ├── Hitl/          → ApprovalRequestConfiguration, ...
│   ├── Analytics/     → RatingConfiguration
│   └── Observability/ → ReasoningTraceConfiguration
└── Migrations/        → Code-first migration dosyaları
```

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
| **Redis** | 6379 | Opsiyonel cache (connection string tanımlı, aktif kullanım sınırlı) |

---

## 7. InMemory Modu Detayları

InMemory modda veriler bellekte tutulur ve uygulama restart'ında kaybolur:

| Store | Sınır |
|-------|-------|
| `InMemorySessionManager` | Sınırsız (RAM) |
| `InMemoryReasoningTraceStore` | Ring buffer, max 500 trace |
| `InMemoryApprovalQueue` | Ring buffer, max 200 recent |
| `InMemoryEscalationSink` | Sınırsız (RAM) |
| `InMemoryRatingStore` | Sınırsız (RAM) |

`InMemorySessionManager` tek bir singleton olarak oluşturulup `ISessionManager` ve `IConversationStore` interface'lerine aynı instance üzerinden bağlanır.

---

## 8. Diğer Veri Depoları

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

## Çapraz Referanslar

- **Mimari + DI haritası** → [architecture.md](architecture.md)
- **Konfigürasyon** → [runtime.md](runtime.md#2-konfigürasyon)
- **Docker kurulumu** → [deployment.md](deployment.md)
- **Semantic memory** → [intelligence.md](intelligence.md)
