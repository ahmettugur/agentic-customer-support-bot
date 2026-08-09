# CustomerSupportBot.Adapters.Persistence — Genel Bakış

Application katmanının **driven port** sözleşmelerini (ISessionManager, IApprovalQueue, vb.) implement eden persistence adaptör katmanıdır. Auth altyapısı ve dosya sistemi adaptörlerini de içerir.

> ⚠️ Üretimde çalışan tek backend **Postgres**'tir. `InMemory/` altında 13 adapter sınıfı mevcuttur ve testlerde kullanılır, ancak `PersistenceOptions.Provider` enum'u yalnızca `Postgres` değerine sahiptir — `PersistenceAdapterServiceCollectionExtensions.AddPersistenceAdapters` hiçbir koşula bağlı kalmadan sadece Postgres implementasyonlarını kaydeder. Runtime'da seçilebilen bir "InMemory modu" yoktur.

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
| Demo veri seed servisi | [DemoDataSeeder.md](DemoDataSeeder.md) |
| Northwind statik seed verisi | [NorthwindSeedData.md](NorthwindSeedData.md) |
| Stale approval temizleme servisi | [StaleApprovalSweepService.md](StaleApprovalSweepService.md) |

---

## Klasör yapısı

```
CustomerSupportBot.Adapters.Persistence/
│
├── DependencyInjection/
│   └── PersistenceAdapterServiceCollectionExtensions.cs  ← Ana DI giriş noktası
│
├── EfCore/
│   ├── CustomerSupportDbContext.cs     ← EF Core DbContext (9 şema)
│   ├── PersistenceHydrator.cs          ← Startup kurtarma (IHostedService)
│   ├── PersistenceOptions.cs           ← Provider ayarı (şu an yalnızca Postgres)
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
│   └── ... (13 adapter)
│
├── Postgres/                           ← Üretim (Hybrid cache+DB+Redis)
│   ├── PostgresSessionManager.cs
│   ├── PostgresApprovalQueue.cs
│   ├── PostgresChatBridge.cs
│   ├── OrderRepository.cs
│   ├── ComplaintRepository.cs
│   ├── ProductCatalogRepository.cs
│   ├── CustomerRepository.cs
│   └── ... (17 adapter)
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

## Postgres vs InMemory

| Özellik | Postgres (üretimde kayıtlı olan) | InMemory (yalnızca testlerde kullanılır) |
|---------|---------|----------|
| Kalıcılık | EF Core + Npgsql | Yok (process sonlanınca sıfırlanır) |
| Yatay ölçekleme | Desteklenir | — |
| Dağıtık lock | Redis `IAppDistributedLock` | — |
| Startup kurtarma | `PersistenceHydrator` | — |
| DI'da nasıl seçilir | `AddPersistenceAdapters` içinde koşulsuz kayıtlıdır | Yalnızca test projelerinde `new InMemoryXxx(...)` ile elle örneklenir |

---

## Port → Adapter eşleme

Aşağıdaki tablo **üretimde gerçekten kayıtlı olan** (Postgres) implementasyonları gösterir. "InMemory" sütunu, testlerde kullanılabilen — ama runtime'da bir config anahtarıyla seçilemeyen — karşılıkları listeler.

| Driven Port | Postgres (kayıtlı) | InMemory (yalnızca test) |
|------------|---------|---------|
| `ISessionManager` | `PostgresSessionManager` | `InMemorySessionManager` |
| `IApprovalQueue` | `PostgresApprovalQueue` | `InMemoryApprovalQueue` |
| `IChatBridge` | `PostgresChatBridge` | `InMemoryChatBridge` |
| `IChatModeRegistry` | `PostgresChatModeRegistry` | `InMemoryChatModeRegistry` |
| `IEscalationSink` | `PostgresEscalationSink` | `InMemoryEscalationSink` |
| `IHumanAgentRegistry` | `PostgresHumanAgentRegistry` | `InMemoryHumanAgentRegistry` |
| `IOrderRepository` | `OrderRepository` | — |
| `ICustomerRepository` | `CustomerRepository` | — |
| `IComplaintRepository` | `ComplaintRepository` | — |
| `IProductCatalogRepository` | `ProductCatalogRepository` | — |
| `IRatingStore` | `PostgresRatingStore` | `InMemoryRatingStore` |
| `IReasoningTraceStore` | `PostgresReasoningTraceStore` | `InMemoryReasoningTraceStore` |
| `ISlaEventSink` | `PostgresSlaEventSink` | `InMemorySlaEventSink` |
| `ILessonStore` | `PostgresLessonStore` | `InMemoryLessonStore` |
| `ICustomerProfileStore` | `PostgresCustomerProfileStore` | `InMemoryCustomerProfileStore` |
| `IMessageBusPort` | — (Redis ayrı adapter, `Adapters.Redis`) | `InMemoryMessageBusAdapter` |
| `ILlmCallPersistencePort` | `PostgresLlmCallUsageSink` | — |
| `IPasswordHasher` | `BCryptPasswordHasher` | — |
| `IJwtAccessTokenProvider` | `JwtAccessTokenProvider` | — |
| `IRefreshTokenRepository` | `EfRefreshTokenRepository` | — |
| `IUserAuthRepository` | `EfUserAuthRepository` | — |
| `IPromptRepository` | `FileSystemPromptRepository` (provider'dan bağımsız) | — |
| `IKnowledgeBaseSource` | `FileSystemKnowledgeBaseSource` (provider'dan bağımsız) | — |
