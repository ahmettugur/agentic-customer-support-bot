# DependencyInjection

**Dosya:** `DependencyInjection/PersistenceAdapterServiceCollectionExtensions.cs`

## Giriş noktası

```csharp
services.AddPersistenceAdapters(configuration);
```

Bu tek çağrı tüm persistence servislerini kaydeder: EF Core DbContext, Postgres adaptörler, auth altyapısı, dosya sistemi ve startup hydrator.

---

## `appsettings.json` yapılandırması

```json
{
  "ConnectionStrings": {
    "PostgreSQL": "Host=localhost;Database=csbot;Username=csbot;Password=..."
  }
}
```

---

## Kayıt akışı

```
AddPersistenceAdapters(configuration)
    │
    ├── AddCustomerSupportPersistence()     ← DbContext + DbContextFactory
    ├── AddHostedService<PersistenceHydrator>()  ← Startup kurtarma
    │
    ├── Postgres adaptörler (Singleton)
    │   ├── IReasoningTraceStore → PostgresReasoningTraceStore
    │   ├── ILlmCallPersistencePort → PostgresLlmCallUsageSink
    │   ├── IApprovalQueue → PostgresApprovalQueue
    │   ├── IEscalationSink → PostgresEscalationSink
    │   ├── IChatModeRegistry → PostgresChatModeRegistry
    │   ├── IChatBridge → PostgresChatBridge
    │   ├── ISessionManager → PostgresSessionManager
    │   ├── IRatingStore → PostgresRatingStore
    │   ├── IHumanAgentRegistry → PostgresHumanAgentRegistry
    │   ├── ICustomerProfileStore → PostgresCustomerProfileStore
    │   ├── ILessonStore → PostgresLessonStore
    │   ├── IWorkflowDefinitionStore → PostgresWorkflowDefinitionStore
    │   └── ISlaEventSink → PostgresSlaEventSink
    │
    ├── Catalog adaptörler (Singleton)
    │   ├── IOrderRepository → OrderRepository
    │   ├── ICustomerRepository → CustomerRepository
    │   ├── IProductCatalogRepository → ProductCatalogRepository
    │   └── IComplaintRepository → ComplaintRepository
    │
    ├── Auth adaptörler (Scoped)
    │   ├── IUserAuthRepository → EfUserAuthRepository
    │   └── IRefreshTokenRepository → EfRefreshTokenRepository
    │
    └── FileSystem adaptörler (Singleton)
        ├── IPromptRepository → FileSystemPromptRepository
        └── IKnowledgeBaseSource → FileSystemKnowledgeBaseSource
```

---

## Postgres Adaptörler

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
| `IOrderRepository` | `OrderRepository` | Singleton |
| `ICustomerRepository` | `CustomerRepository` | Singleton |
| `IProductCatalogRepository` | `ProductCatalogRepository` | Singleton |
| `IComplaintRepository` | `ComplaintRepository` | Singleton |

---

## Auth Adaptörler

| Port | Implementasyon | Yaşam döngüsü |
|------|---------------|--------------|
| `IUserAuthRepository` | `EfUserAuthRepository` | Scoped |
| `IRefreshTokenRepository` | `EfRefreshTokenRepository` | Scoped |

---

## FileSystem Adaptörler

| Port | Implementasyon |
|------|---------------|
| `IPromptRepository` | `FileSystemPromptRepository` |
| `IKnowledgeBaseSource` | `FileSystemKnowledgeBaseSource` |

---

## EF Core Persistence

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
builder.Services.AddPersistenceAdapters(configuration);      // 3. Persistence ← bu
builder.Services.AddAgentsAdapter();                         // 4. MAF agents
```

`AddPersistenceAdapters`, `IChatClient` veya Application servislerine bağımlı değildir — sıra esnektir.
