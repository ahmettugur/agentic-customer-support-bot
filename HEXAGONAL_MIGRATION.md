# Hexagonal Architecture (Ports & Adapters) Geçiş Rehberi

## Oluşturulan Yeni Proje Yapısı

```
CustomerSupportBot/
├── CustomerSupportBot.slnx                         ← Solution (yeni)
│
├── CustomerSupportBot.Core/                        ← 🔷 HEXAGON (Core)
│   ├── Ports/
│   │   ├── Driving/                                ← Primary Ports (dışarı sunulan sözleşmeler)
│   │   │   ├── IChatPort.cs
│   │   │   ├── IReasoningPort.cs
│   │   │   ├── IApprovalPort.cs
│   │   │   ├── IEscalationPort.cs
│   │   │   ├── ISessionPort.cs
│   │   │   └── IAnalyticsPort.cs
│   │   └── Driven/                                 ← Secondary Ports (dışarıya ihtiyaç duyulanlar)
│   │       ├── Persistence/
│   │       │   ├── ISessionRepository.cs
│   │       │   ├── IProductCatalogRepository.cs    ← FakeDatabase yerine
│   │       │   ├── IOrderRepository.cs             ← FakeDatabase yerine
│   │       │   ├── IComplaintRepository.cs         ← FakeDatabase yerine
│   │       │   ├── IApprovalQueueRepository.cs
│   │       │   ├── IEscalationRepository.cs
│   │       │   ├── IChatBridgeRepository.cs
│   │       │   ├── IChatModeRepository.cs
│   │       │   ├── IRatingRepository.cs
│   │       │   ├── IHumanAgentRepository.cs
│   │       │   ├── ICustomerProfileRepository.cs
│   │       │   ├── ILessonRepository.cs
│   │       │   ├── IWorkflowDefinitionRepository.cs
│   │       │   └── ISlaEventRepository.cs
│   │       ├── AI/
│   │       │   ├── IEmbeddingPort.cs
│   │       │   └── IVectorMemoryPort.cs
│   │       ├── Locking/
│   │       │   └── IDistributedLockPort.cs
│   │       └── Observability/
│   │           ├── IReasoningTraceRepository.cs
│   │           └── ICostCalculatorPort.cs
│   ├── Model/                                      ← Domain modeller
│   │   ├── ProductInfo.cs
│   │   ├── OrderInfo.cs
│   │   └── ComplaintInfo.cs
│   └── Services/
│       └── CustomerSupportToolsService.cs          ← Statik → DI tabanlı dönüşüm
│
├── CustomerSupportBot.Adapters.Persistence/        ← 🔴 Driven Adapter
│   ├── InMemory/
│   │   ├── InMemoryProductCatalogAdapter.cs        ← IProductCatalogRepository impl.
│   │   ├── InMemoryOrderAdapter.cs                 ← IOrderRepository impl.
│   │   └── InMemoryComplaintAdapter.cs             ← IComplaintRepository impl.
│   ├── Postgres/                                   ← (taşınacak)
│   └── EfCore/                                     ← (taşınacak)
│
├── CustomerSupportBot.Adapters.AI/                 ← 🔴 Driven Adapter
│   ├── OpenAi/
│   │   └── OpenAiEmbeddingAdapter.cs               ← IEmbeddingPort impl.
│   └── Qdrant/
│       └── QdrantVectorMemoryAdapter.cs            ← IVectorMemoryPort impl.
│
├── CustomerSupportBot.Adapters.Redis/              ← 🔴 Driven Adapter
│   └── Locking/
│       └── RedisDistributedLockAdapter.cs          ← IDistributedLockPort impl.
│
├── CustomerSupportBot.Adapters.Telemetry/          ← 🔴 Driven Adapter
│
└── CustomerSupportBot.Api/                         ← 🟢 Driving Adapter (mevcut, dönüşecek)
```

## Bağımlılık Kuralı (Dependency Rule)

```
[Driving Adapter]              [Core / Hexagon]              [Driven Adapter]
CustomerSupportBot.Api   ───→  CustomerSupportBot.Core  ←───  CustomerSupportBot.Adapters.*
  (HTTP Endpoints)              (Ports + Model + Services)     (EF, Redis, Qdrant, OpenAI)
```

**Core hiçbir adaptöre bağımlı değildir.**
Adaptörler Core'daki port'ları implement eder ve Core'a bağımlıdır.

## Tamamlanan Dönüşümler

### 1. FakeDatabase Statik Bağımlılığı Kırıldı ✅

**Önce (Anti-pattern):**
```csharp
// CustomerSupportTools.cs — statik, test edilemez, değiştirilemez
var product = FakeDatabase.ProductCatalog[matchedName];
```

**Sonra (Hexagonal):**
```csharp
// CustomerSupportToolsService.cs — DI ile inject, test edilebilir, değiştirilebilir
public CustomerSupportToolsService(
    IProductCatalogRepository products,   // port
    IOrderRepository orders,              // port
    IComplaintRepository complaints)      // port
```

### 2. Driving Port'lar Tanımlandı ✅

| Port | Açıklama |
|---|---|
| `IChatPort` | HTTP adaptörü → Core chat use case |
| `IReasoningPort` | HTTP adaptörü → Core reasoning |
| `IApprovalPort` | Admin panel → HITL onay akışı |
| `IEscalationPort` | Admin panel → eskalasyon yönetimi |
| `ISessionPort` | HTTP adaptörü → oturum yönetimi |
| `IAnalyticsPort` | HTTP adaptörü → analitik |

### 3. Driven Port'lar Tanımlandı ✅

14 persistence port'u + 2 AI port'u + 1 locking port'u + 2 observability port'u

### 4. Adapter Projeleri Oluşturuldu ✅

- `CustomerSupportBot.Adapters.Persistence` — EF Core, Postgres, InMemory
- `CustomerSupportBot.Adapters.AI` — OpenAI, Azure OpenAI, Qdrant
- `CustomerSupportBot.Adapters.Redis` — Distributed lock
- `CustomerSupportBot.Adapters.Telemetry` — OpenTelemetry, maliyet hesaplama

## Sonraki Adımlar (Kalan Görevler)

### Yüksek Öncelik

1. **Mevcut `CustomerSupportBot.Api` Refactoring**
   - `Services/Persistence/Postgres*` → `CustomerSupportBot.Adapters.Persistence/Postgres/` taşı
   - `Infrastructure/Persistence/` → `CustomerSupportBot.Adapters.Persistence/EfCore/` taşı
   - `Services/Locking/RedisDistributedLock.cs` → `CustomerSupportBot.Adapters.Redis/` taşı
   - `Services/Memory/Qdrant*` ve `OpenAiEmbedding*` → `CustomerSupportBot.Adapters.AI/` taşı

2. **`CustomerSupportTeam` Refactoring (God Class)**
   - Agent oluşturma → `AgentFactory` ayrı sınıfa
   - Side-effect'ler (episodic memory, profile update) → ayrı `PostProcessingService`
   - `CustomerSupportTeam` → `IChatPort` driving port implementasyonu olarak kalır

3. **`CustomerSupportBot.Api` → Driving Adapter'a Dönüştür**
   - `CustomerSupportBot.Api.csproj`'a `CustomerSupportBot.Core` project reference ekle
   - Endpoint'ler doğrudan service inject yerine driving port'lardan çağırsın

### Orta Öncelik

4. **Composition Root Güncellemesi**
   - `PersistenceServicesExtensions.cs` → `CustomerSupportBot.Adapters.Persistence` namespace'lerini kullan
   - `IApprovalQueue` → `IApprovalQueueRepository`, `ISessionManager` → `ISessionRepository` vb.

5. **Namespace Geçişi**
   - `CustomerSupportBot.Api.*` → `CustomerSupportBot.Core.*` (Core modeller için)

## DI Kayıt Örneği (Yeni Composition Root)

```csharp
// Program.cs — adapter seçimi hâlâ aynı pattern, port isimleri değişti

if (opts.Provider == PersistenceProvider.Postgres)
{
    // Driven adapters: Postgres
    services.AddSingleton<IProductCatalogRepository, PostgresProductCatalogAdapter>();
    services.AddSingleton<IOrderRepository, PostgresOrderAdapter>();
    services.AddSingleton<IComplaintRepository, PostgresComplaintAdapter>();
    services.AddSingleton<IApprovalQueueRepository, PostgresApprovalQueue>();
    // ...
}
else
{
    // Driven adapters: InMemory
    services.AddSingleton<IProductCatalogRepository, InMemoryProductCatalogAdapter>();
    services.AddSingleton<IOrderRepository, InMemoryOrderAdapter>();
    services.AddSingleton<IComplaintRepository, InMemoryComplaintAdapter>();
    services.AddSingleton<IApprovalQueueRepository, InMemoryApprovalQueue>();
    // ...
}

// Core service (artık FakeDatabase'e bağımlı değil)
services.AddSingleton<CustomerSupportToolsService>();

// Driving port implementasyonları (Core servisleri)
services.AddSingleton<IChatPort, ChatOrchestrator>();
services.AddSingleton<IReasoningPort, ReasoningService>();
```
