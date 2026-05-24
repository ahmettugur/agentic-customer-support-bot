# DependencyInjection

**Dosya:** `DependencyInjection/PersistenceAdapterServiceCollectionExtensions.cs`

## Giriş noktası

```csharp
services.AddPersistenceAdapter(configuration);
```

Bu tek çağrı, `Persistence.Provider` ayarına göre InMemory veya Postgres backend'i seçerek tüm persistence servislerini kaydeder.

---

## `appsettings.json` yapılandırması

```json
{
  "Persistence": {
    "Provider": "Postgres",
    "ConnectionString": "Host=localhost;Database=csbot;Username=csbot;Password=..."
  }
}
```

| Değer | Açıklama |
|-------|---------|
| `"InMemory"` | Geliştirme ve test — kalıcılık yok |
| `"Postgres"` | Üretim — EF Core + Npgsql + opsiyonel Redis |

---

## Kayıt akışı

```
AddPersistenceAdapter(configuration)
    │
    ├── Provider == "Postgres"?
    │   ├── AddEfCorePersistence(configuration)   ← DbContext + DbContextFactory
    │   ├── AddPostgresAdapters()                 ← 13 Postgres adapter
    │   └── AddPersistenceHydrator()              ← IHostedService startup kurtarma
    │
    ├── Provider == "InMemory"?
    │   └── AddInMemoryAdapters()                 ← 14 InMemory adapter
    │
    ├── AddAuthAdapters()                         ← BCrypt + JWT (her iki backend'de)
    ├── AddFileSystemAdapters(configuration)      ← Prompt + KB dosyaları
    └── AddHealthChecks() (Postgres ise)
```

---

## `AddInMemoryAdapters`

| Port | Implementasyon | Yaşam döngüsü |
|------|---------------|--------------|
| `ISessionManager` | `InMemorySessionManager` | Singleton |
| `IApprovalQueue` | `InMemoryApprovalQueue` | Singleton |
| `IChatBridge` | `InMemoryChatBridge` | Singleton |
| `IChatModeRegistry` | `InMemoryChatModeRegistry` | Singleton |
| `IEscalationSink` | `InMemoryEscalationSink` | Singleton |
| `IHumanAgentRegistry` | `InMemoryHumanAgentRegistry` | Singleton |
| `IOrderRepository` | `InMemoryOrderAdapter` | Singleton |
| `IComplaintRepository` | `InMemoryComplaintAdapter` | Singleton |
| `IProductCatalogRepository` | `InMemoryProductCatalogAdapter` | Singleton |
| `IRatingStore` | `InMemoryRatingStore` | Singleton |
| `IReasoningTraceStore` | `InMemoryReasoningTraceStore` | Singleton |
| `ISlaEventSink` | `InMemorySlaEventSink` | Singleton |
| `ILessonStore` | `InMemoryLessonStore` | Singleton |
| `IWorkflowDefinitionStore` | `InMemoryWorkflowDefinitionStore` | Singleton |
| `ICustomerProfileStore` | `InMemoryCustomerProfileStore` | Singleton |
| `IMessageBusPort` | `InMemoryMessageBusAdapter` | Singleton |

---

## `AddPostgresAdapters`

| Port | Implementasyon | Yaşam döngüsü |
|------|---------------|--------------|
| `ISessionManager` | `PostgresSessionManager` | Singleton |
| `IApprovalQueue` | `PostgresApprovalQueue` | Singleton |
| `IChatBridge` | `PostgresChatBridge` | Singleton |
| `IChatModeRegistry` | `PostgresChatModeRegistry` | Singleton |
| `IEscalationSink` | `PostgresEscalationSink` | Singleton |
| `IHumanAgentRegistry` | `PostgresHumanAgentRegistry` | Singleton |
| `IRatingStore` | `PostgresRatingStore` | Singleton |
| `IReasoningTraceStore` | `PostgresReasoningTraceStore` | Singleton |
| `ISlaEventSink` | `PostgresSlaEventSink` | Singleton |
| `ILessonStore` | `PostgresLessonStore` | Singleton |
| `IWorkflowDefinitionStore` | `PostgresWorkflowDefinitionStore` | Singleton |
| `ICustomerProfileStore` | `PostgresCustomerProfileStore` | Singleton |
| `ILlmCallPersistencePort` | `PostgresLlmCallUsageSink` | Singleton |
| `IOrderRepository` | `InMemoryOrderAdapter` | Singleton (demo data) |
| `IComplaintRepository` | `InMemoryComplaintAdapter` | Singleton (demo data) |
| `IProductCatalogRepository` | `InMemoryProductCatalogAdapter` | Singleton (demo data) |

> Sipariş/şikayet/ürün katalog adaptörleri Postgres modunda da InMemory kalır — demo verisi içerdiğinden.

---

## `AddAuthAdapters`

Her iki backend için ortak:

| Port | Implementasyon |
|------|---------------|
| `IPasswordHasher` | `BCryptPasswordHasher` |
| `IJwtAccessTokenProvider` | `JwtAccessTokenProvider` |
| `IRefreshTokenRepository` | `EfRefreshTokenRepository` |
| `IUserAuthRepository` | `EfUserAuthRepository` |

---

## `AddFileSystemAdapters`

| Port | Implementasyon |
|------|---------------|
| `IPromptRepository` | `FileSystemPromptRepository` |
| `IKnowledgeBaseSource` | `FileSystemKnowledgeBaseSource` |

---

## `AddEfCorePersistence`

```csharp
services.AddDbContext<CustomerSupportDbContext>(opts =>
    opts.UseNpgsql(connectionString, npgsql =>
        npgsql.EnableRetryOnFailure(3)));

services.AddDbContextFactory<CustomerSupportDbContext>(...);
```

Singleton servisler `IDbContextFactory<T>` kullanır (Scoped DbContext alamazlar). Scoped servisler direkt `DbContext` inject eder.

---

## Önerilen çağrı sırası (`Program.cs`)

```csharp
builder.Services.AddAiServices(configuration);               // 1. IChatClient
builder.Services.AddApplicationDrivingPorts(configuration);  // 2. Application
builder.Services.AddPersistenceAdapter(configuration);       // 3. Persistence ← bu
builder.Services.AddAgentsAdapter();                         // 4. MAF agents
```

`AddPersistenceAdapter`, `IChatClient` veya Application servislerine bağımlı değildir — sıra esnektir.
