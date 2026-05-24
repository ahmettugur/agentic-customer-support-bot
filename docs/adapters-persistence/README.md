# CustomerSupportBot.Adapters.Persistence — Genel Bakış

Application katmanının **driven port** sözleşmelerini (ISessionManager, IApprovalQueue, vb.) implement eden persistence adaptör katmanıdır. İki persistence backend (InMemory ve Postgres), auth altyapısı ve dosya sistemi adaptörlerini içerir.

---

## Belgeler

| Konu | Dosya |
|------|-------|
| DI kayıtları ve yapılandırma | [DependencyInjection.md](DependencyInjection.md) |
| Hybrid cache+DB deseni (temel mimari) | [HybridPattern.md](HybridPattern.md) |
| EF Core DbContext ve şemalar | [DbContext.md](DbContext.md) |
| DB varlık modelleri (Entity'ler) | [Entities.md](Entities.md) |
| Startup kurtarma servisi | [PersistenceHydrator.md](PersistenceHydrator.md) |
| InMemory adaptörler | [InMemoryAdapters.md](InMemoryAdapters.md) |
| Postgres adaptörler | [PostgresAdapters.md](PostgresAdapters.md) |
| Auth adaptörleri (JWT, BCrypt, EF) | [AuthAdapters.md](AuthAdapters.md) |
| Dosya sistemi adaptörleri | [FileSystemAdapters.md](FileSystemAdapters.md) |
| Exception çevirici | [ExceptionTranslator.md](ExceptionTranslator.md) |

---

## Klasör yapısı

```
CustomerSupportBot.Adapters.Persistence/
│
├── DependencyInjection/
│   └── PersistenceAdapterServiceCollectionExtensions.cs  ← Ana DI giriş noktası
│
├── EfCore/
│   ├── CustomerSupportDbContext.cs     ← EF Core DbContext (8 şema)
│   ├── PersistenceHydrator.cs          ← Startup kurtarma (IHostedService)
│   ├── PersistenceOptions.cs           ← InMemory/Postgres seçimi
│   ├── PersistenceServiceCollectionExtensions.cs
│   ├── DesignTimeDbContextFactory.cs   ← migration üretimi için
│   ├── Schemas.cs                      ← şema sabitleri
│   ├── Auth/                           ← EF Auth repository'leri
│   ├── Entities/                       ← DB varlık sınıfları
│   └── Configurations/                 ← Fluent API konfigürasyonları
│
├── InMemory/                           ← Geliştirme / tek instance
│   ├── InMemorySessionManager.cs
│   ├── InMemoryApprovalQueue.cs
│   ├── InMemoryChatBridge.cs
│   └── ... (14 adapter)
│
├── Postgres/                           ← Üretim (Hybrid cache+DB+Redis)
│   ├── PostgresSessionManager.cs
│   ├── PostgresApprovalQueue.cs
│   ├── PostgresChatBridge.cs
│   └── ... (13 adapter)
│
├── Auth/                               ← BCrypt, JWT
│   ├── BCryptPasswordHasher.cs
│   ├── JwtAccessTokenProvider.cs
│   └── TokenService.cs
│
├── FileSystem/                         ← Prompt ve KB dosyaları
│   ├── FileSystemPromptRepository.cs
│   ├── FileSystemKnowledgeBaseSource.cs
│   └── PromptOptions.cs
│
├── HealthChecks/
│   └── PostgresHealthCheck.cs
│
└── ExceptionTranslator.cs
```

---

## İki backend karşılaştırması

| Özellik | InMemory | Postgres |
|---------|----------|---------|
| Kalıcılık | Yok (restart'ta sıfırlanır) | EF Core + Npgsql |
| Pub/Sub | Yok (işlem içi) | Redis (opsiyonel) |
| Yatay ölçekleme | Desteklenmez | Desteklenir |
| Dağıtık lock | Yok | Redis `IAppDistributedLock` |
| Startup kurtarma | Yok | `PersistenceHydrator` |
| Kullanım | Geliştirme / test | Üretim |

`appsettings.json` → `Persistence.Provider = "InMemory" | "Postgres"` ile seçilir.

---

## Port → Adapter eşleme

| Driven Port | InMemory | Postgres |
|------------|---------|---------|
| `ISessionManager` | `InMemorySessionManager` | `PostgresSessionManager` |
| `IApprovalQueue` | `InMemoryApprovalQueue` | `PostgresApprovalQueue` |
| `IChatBridge` | `InMemoryChatBridge` | `PostgresChatBridge` |
| `IChatModeRegistry` | `InMemoryChatModeRegistry` | `PostgresChatModeRegistry` |
| `IEscalationSink` | `InMemoryEscalationSink` | `PostgresEscalationSink` |
| `IHumanAgentRegistry` | `InMemoryHumanAgentRegistry` | `PostgresHumanAgentRegistry` |
| `IOrderRepository` | `InMemoryOrderAdapter` | — (InMemory only) |
| `IComplaintRepository` | `InMemoryComplaintAdapter` | — (InMemory only) |
| `IProductCatalogRepository` | `InMemoryProductCatalogAdapter` | — (InMemory only) |
| `IRatingStore` | `InMemoryRatingStore` | `PostgresRatingStore` |
| `IReasoningTraceStore` | `InMemoryReasoningTraceStore` | `PostgresReasoningTraceStore` |
| `ISlaEventSink` | `InMemorySlaEventSink` | `PostgresSlaEventSink` |
| `ILessonStore` | `InMemoryLessonStore` | `PostgresLessonStore` |
| `IWorkflowDefinitionStore` | `InMemoryWorkflowDefinitionStore` | `PostgresWorkflowDefinitionStore` |
| `ICustomerProfileStore` | `InMemoryCustomerProfileStore` | `PostgresCustomerProfileStore` |
| `IMessageBusPort` | `InMemoryMessageBusAdapter` | — (Redis ayrı adapter) |
| `ILlmCallPersistencePort` | — | `PostgresLlmCallUsageSink` |
| `IPasswordHasher` | — | `BCryptPasswordHasher` |
| `IJwtAccessTokenProvider` | — | `JwtAccessTokenProvider` |
| `IRefreshTokenRepository` | — | `EfRefreshTokenRepository` |
| `IUserAuthRepository` | — | `EfUserAuthRepository` |
| `IPromptRepository` | `FileSystemPromptRepository` | `FileSystemPromptRepository` |
| `IKnowledgeBaseSource` | `FileSystemKnowledgeBaseSource` | `FileSystemKnowledgeBaseSource` |
