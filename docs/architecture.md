# Architecture

Bu dokümanda `CustomerSupportBot`'un yüksek seviye mimarisi, bileşen haritası, bir isteğin uçtan uca nasıl işlendiği ve dependency injection akışı anlatılır.

## Tepeden görünüm

```
┌──────────────────────────────────────────────────────────────────┐
│                        FRONTEND (wwwroot/)                        │
│         index.html + chat-ui.js   — SSE streaming UI              │
└─────────────────────────────┬────────────────────────────────────┘
                              │ HTTP / SSE
┌─────────────────────────────▼────────────────────────────────────┐
│                     ENDPOINTS (Minimal API)                      │
│  Chat │ Session │ Trace │ Admin │ Realtime │ Auth │ SLA │ ...  │
└───────┬───────────────────┬──────────────────┬──────────────┬────┘
        │                   │                  │              │
        ▼                   ▼                  ▼              ▼
┌───────────────┐  ┌────────────────┐  ┌─────────────┐  ┌──────────┐
│ ReasoningSvc  │  │ CustomerSupp.  │  │ TraceStore  │  │ Evaluat. │
│  (o-series)   │─▶│ Team           │─▶│ (Postgres/  │  │ Runner   │
└───┬───────────┘  │ (6 agents +    │  │  InMemory)  │  └──────────┘
    │              │  ChatManager   │
    │              │  +Compound query │
    │              │  orchestration)│
    │              └────────┬───────┘
    │                       │
    │                ┌──────┴──────┐
    │                ▼             ▼
    │        ┌───────────────┐ ┌───────────────┐
    │        │ PromptService │ │ ContextPipe.  │
    │        │ (MD loader)   │ │ (providers)   │
    │        └───────────────┘ └───────┬───────┘
    │                                  │
    │             ┌────────────────────┴───────────────────┐
    │             ▼                                        ▼
    │     CustomerContext              ConversationSummary
    │     (repository port'ları)       (LLM özet)
    │             │                           │
    │             ▼                           ▼
    │     SemanticMemoryCtx.           CustomerProfileCtx.
    │     (Qdrant RAG)                 (per-customer profil)
    │
    │  ┌─────────────────────────────────────────────────────────────┐
    │  │ Deterministic Reasoning Helpers                             │
    └─▶│   EntityVerifier        (Katman 0: ID extract + DB verify)  │
       │   ReasoningSanityChecker(Katman 1.5: IReasoningSanityRule × 8)│
       └─────────────────────────────────────────────────────────────┘
                                  │
                                  ▼
┌─────────────────────────────────────────────────────────────────┐
│                      INFRASTRUCTURE                              │
│  AiClientFactory → { OpenAI | AzureOpenAI | Anthropic }          │
│    ├─ IChatClient            (chat / specialist / response)        │
│    └─ ReasoningChatClient    (o-series / reasoning deployment)     │
│  PostgreSQL (sessions/traces/approvals/escalations/ratings/auth) │
│  Qdrant (cs_knowledge / cs_episodic / cs_lessons collections)    │
│  Redis (opsiyonel cache — bağlantı var, aktif kullanım sınırlı)  │
│  InMemory Demo Adapters (Product/Order/Complaint — hexagonal)   │
│  IdExtractor (regex — deterministic ID extraction)              │
│  JWT Bearer Auth (access + refresh token)                        │
└─────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│                  INTELLIGENCE LAYERS                             │
│  Semantic Memory (Qdrant)                                         │
│    ├─ KnowledgeBaseIngestor (startup MD → chunks → embed → upsert)│
│    ├─ SemanticMemoryService (facade — Episodic/Lessons/Knowledge)│
│    └─ SemanticMemoryContextProvider (RAG → context pipeline)     │
│  Self-Improving Loop                                              │
│    ├─ LessonMiner (low-rated/error trace → LLM → Lesson proposals)│
│    └─ Approve → Qdrant cs_lessons → next conversation context    │
│  Replay UI (`/replay.html?traceId=...`) — step-by-step trace player│
│                                                                   │
│  Detay: docs/intelligence.md                                      │
└─────────────────────────────────────────────────────────────────┘
```

## Proje yapısı (Hexagonal Mimari)

Sistem **Ports & Adapters (Hexagonal)** mimarisine göre düzenlenmiş **8 proje**den oluşur:

```
CustomerSupport.slnx
│
├── CustomerSupportBot.Domain/           ← Katman 1: Saf domain (bağımlılık yok)
│   ├── Exceptions/                      # Domain exception hiyerarşisi
│   │   └── DomainException.cs           # DomainException, EntityNotFoundException,
│   │                                    # PersistenceException, ConcurrencyConflictException,
│   │                                    # ExternalServiceException
│   ├── Model/                           # Tüm domain entity'leri
│   │   ├── AgentSession.cs / ApprovalRequest.cs / EscalationRequest.cs
│   │   ├── PlanningResult.cs / ReasoningResult.cs / ReasoningTrace.cs
│   │   ├── ToolResult.cs / VerifiedEntities.cs / SubTask.cs
│   │   ├── OrderInfo.cs / ProductInfo.cs / ComplaintInfo.cs
│   │   ├── SlaEvent.cs / WellKnown.cs / ConversationMessage.cs
│   │   ├── Auth/                        # UserInfo, RefreshTokenInfo
│   │   ├── Improvement/                 # Lesson
│   │   ├── Memory/                      # CustomerProfile, MemoryDocument
│   │   └── Workflow/                    # WorkflowDefinition, WorkflowStep
│   └── Services/                        # Deterministik domain servisleri (LLM çağrısı yok)
│       ├── IdExtractor.cs               # Regex ile entity ID çıkarımı
│       ├── PlanningResultParser.cs      # PlanningAgent JSON çıktısını parse eder
│       ├── ReasoningResultParser.cs     # Reasoning LLM çıktısını parse eder
│       ├── SessionStateExtractor.cs     # Mesajlardan intent/sentiment/phase çıkarır
│       ├── SpecialistReasoningParser.cs # Specialist JSON çıktısını parse eder
│       └── EscalationStates.cs          # Eskalasyon durum makinesi
│
├── CustomerSupportBot.Application/      ← Katman 2: Uygulama çekirdeği (sadece Domain'e bağımlı)
│   ├── Ports/
│   │   ├── Driving/                     # PRIMARY (inbound) portlar — dışarıdan çağrılır (17 port)
│   │   │   ├── IChatPort.cs             # Chat işleme port'u
│   │   │   ├── ISessionPort.cs          # Oturum yönetimi port'u
│   │   │   ├── IReasoningPort.cs        # Reasoning pipeline port'u
│   │   │   ├── IApprovalPort.cs         # HITL onay port'u
│   │   │   ├── IEscalationPort.cs       # Eskalasyon port'u
│   │   │   ├── IAnalyticsPort.cs        # Analitik port'u
│   │   │   ├── IChatSessionPort.cs      # Chat session takeover/replan port'u
│   │   │   ├── IHitlEventPort.cs        # HITL event subscription port'u
│   │   │   ├── ITelemetryPort.cs        # Telemetri maliyet görünümü port'u
│   │   │   ├── ITracePort.cs            # Trace yönetim port'u
│   │   │   ├── IHumanAgentPort.cs       # İnsan temsilci yönetim port'u
│   │   │   ├── IImprovementsPort.cs     # Self-improvement port'u
│   │   │   ├── IPersonalizationPort.cs  # Kişiselleştirme port'u
│   │   │   ├── IWorkflowPort.cs         # Low-code workflow port'u
│   │   │   ├── ISlaPort.cs              # SLA izleme port'u
│   │   │   ├── IMemoryPort.cs           # Semantic memory port'u
│   │   │   ├── IReplanPort.cs           # Yeniden planlama port'u
│   │   │   ├── ChatRequest.cs / ChatResponse.cs / StreamEvent.cs  # DTO'lar
│   │   │   └── IInputGuard.cs / IEvaluationPort.cs
│   │   └── Driven/                      # SECONDARY (outbound) portlar — adaptörlere bağlıdır
│   │       ├── Persistence/             # 14 repository interface'i
│   │       │   ├── ISessionManager.cs / IApprovalQueue.cs
│   │       │   ├── IEscalationSink.cs / IChatBridge.cs
│   │       │   ├── IChatModeRegistry.cs / IOrderRepository.cs
│   │       │   ├── IProductCatalogRepository.cs / IComplaintRepository.cs
│   │       │   ├── IRatingStore.cs / IHumanAgentRegistry.cs
│   │       │   ├── ICustomerProfileStore.cs / ILessonStore.cs
│   │       │   ├── ISlaEventSink.cs / IWorkflowDefinitionStore.cs
│   │       │   └── (toplam 14 interface)
│   │       ├── AI/                      # AI sağlayıcı interface'leri
│   │       │   ├── IReasoningChatClient.cs / IGeneralChatClient.cs
│   │       │   ├── IEmbeddingPort.cs / IVectorMemoryPort.cs
│   │       │   └── IKnowledgeBaseIngestor.cs / ISemanticMemoryIngestor.cs
│   │       ├── Locking/                 # IAppDistributedLock
│   │       ├── Messaging/               # IMessageBusPort (pub/sub soyutlaması)
│   │       ├── Observability/           # ICostCalculatorPort, ICostUsageStorePort, IReasoningTraceStore
│   │       ├── Auth/                    # IUserAuthRepository, IRefreshTokenRepository
│   │       ├── IAgentTeamPort.cs        # Ajan takımı port'u (driven — Agents adapter implemente eder)
│   │       ├── IPromptRepository.cs     # Prompt dosyası okuma port'u
│   │       ├── IContextProvider.cs      # Context provider arayüzü
│   │       ├── IApprovalContextAccessor.cs
│   │       ├── ISkillsBasedRouter.cs    # İnsan temsilci yönlendirme port'u
│   │       ├── IContextPipeline.cs      # Context pipeline soyutlaması
│   │       ├── ICustomerSupportToolsService.cs  # AI tool servisi port'u
│   │       ├── ISemanticMemoryWriter.cs # Episodik bellek yazma port'u
│   │       ├── ICustomerProfileService.cs # Müşteri profil servisi port'u
│   │       ├── ApprovalOptions.cs       # HITL onay yapılandırması
│   │       ├── WorkflowGuardOptions.cs  # Workflow guard ayarları
│   │       └── ParallelExecutionOptions.cs # Paralel yürütme ayarları
│   └── Services/                        # Use case implementasyonları
│       ├── ReasoningService.cs          # Reasoning pipeline (Katman 1 + 1.5)
│       ├── EntityVerifier.cs            # Katman 0 — entity extract + port lookup
│       ├── ReasoningSanityChecker.cs    # Katman 1.5 — IReasoningSanityRule × 8
│       ├── CustomerSupportToolsService.cs # AI tool implementasyonları (ICustomerSupportToolsService impl)
│       ├── ContextPipeline.cs           # IContextPipeline implementasyonu
│       ├── EscalationPolicyService.cs   # Eskalasyon routing politikası (ApprovalGateService'den extract)
│       ├── InputGuard.cs                # Girdi güvenlik filtresi
│       ├── SessionPortService.cs / ChatPortService.cs / ApprovalPortService.cs
│       ├── Providers/                   # IContextProvider implementasyonları
│       │   ├── CustomerContextProvider.cs        # müşteri geçmişi (Order=10)
│       │   ├── ConversationSummaryProvider.cs    # 8+ mesaj → LLM özet (Order=5)
│       │   ├── SemanticMemoryContextProvider.cs  # Qdrant RAG (Order=20)
│       │   └── CustomerProfileContextProvider.cs # Per-customer profil (Order=15)
│       ├── Memory/                      # Semantic memory facade + Qdrant
│       ├── Improvement/                 # Self-improving loop (LessonMiner)
│       ├── Personalization/             # Per-customer profil yönetimi
│       ├── Routing/                     # Skills-based eskalasyon
│       ├── Sla/                         # SlaGuardianService (BackgroundService)
│       ├── Workflow/                    # WorkflowExecutor + subtask orchestration
│       └── Evaluation/                  # CriteriaEvaluator / EvaluationRunner
│
├── CustomerSupportBot.Adapters.Agents/  ← Katman 3a: MAF ajan adaptörü
│   ├── CustomerSupportTeam.cs           # 6 agent + workflow builder + streaming pump (IOptions<T> ile yapılandırılır)
│   ├── CustomerSupportChatManager.cs    # GroupChatManager türevi — seçim + terminasyon
│   ├── ApprovalGateService.cs           # HITL approval gate (routing'i EscalationPolicyService'e delege eder)
│   ├── WorkflowResponseExtractor.cs     # Workflow çıktı temizleme
│   └── Routing/                         # Ajan seçim stratejileri
│
├── CustomerSupportBot.Adapters.Persistence/  ← Katman 3b: Veritabanı adaptörü
│   ├── EfCore/                          # EF Core 10 bağlam + entity'ler
│   │   ├── CustomerSupportDbContext.cs  # 10+ DbSet
│   │   ├── Entities/                    # Auth, Chat, Hitl, Analytics, Observability
│   │   ├── Configurations/              # EF fluent config'ler
│   │   └── Migrations/                  # Code-first migration'lar
│   ├── Postgres/                        # PostgreSQL implementasyonları
│   │   ├── PostgresChatModeRegistry.cs  # IChatModeRegistry → IMessageBusPort pub/sub
│   │   ├── PostgresChatBridge.cs        # IChatBridge → pub/sub
│   │   ├── PostgresEscalationSink.cs    # IEscalationSink
│   │   ├── PostgresApprovalQueue.cs     # IApprovalQueue
│   │   └── ...diğer Postgres adaptörleri
│   ├── InMemory/                        # InMemory fallback implementasyonları
│   │   ├── InMemorySessionManager.cs    # ISessionManager
│   │   ├── InMemoryMessageBusAdapter.cs # IMessageBusPort (lokal pub/sub)
│   │   ├── InMemoryProductCatalogAdapter.cs # IProductCatalogRepository (seed data dahil)
│   │   ├── InMemoryOrderAdapter.cs      # IOrderRepository
│   │   └── ...diğer InMemory adaptörler
│   ├── FileSystem/
│   │   ├── FileSystemPromptRepository.cs # IPromptRepository → disk'ten MD yükleme
│   │   └── PromptOptions.cs             # Prompts:RootPath yapılandırması
│   ├── Auth/                            # JWT kullanıcı yönetimi
│   └── HealthChecks/                    # Postgres sağlık kontrolleri
│
├── CustomerSupportBot.Adapters.Redis/   ← Katman 3c: Redis adaptörü
│   ├── Locking/                         # RedisDistributedLock → IAppDistributedLock
│   ├── Messaging/
│   │   └── RedisMessageBusAdapter.cs    # IMessageBusPort → Redis pub/sub (pod'lar arası)
│   └── DependencyInjection/
│       └── RedisAdapterServiceCollectionExtensions.cs
│
├── CustomerSupportBot.Adapters.Telemetry/  ← Katman 3d: Telemetri adaptörü
│   ├── OpenTelemetry/                   # OTLP exporter yapılandırması
│   ├── Chat/                            # Maliyet takip entegrasyonu
│   └── Models/                          # Telemetri modelleri
│
├── CustomerSupportBot.Adapters.AI/      ← Katman 3e: AI sağlayıcı adaptörü
│   ├── OpenAi/                          # OpenAI IChatClient implementasyonu
│   ├── AzureOpenAi/                     # Azure OpenAI IChatClient implementasyonu
│   ├── Anthropic/                       # Anthropic IChatClient implementasyonu
│   ├── Chat/                            # TelemetryChatClient, ReasoningChatClient
│   ├── Qdrant/                          # Qdrant vector store (IVectorMemoryPort, IEmbeddingPort)
│   ├── Realtime/                        # gpt-realtime-2 WebSocket köprüsü
│   ├── Options/                         # AiProviderOptions, QdrantOptions
│   └── DependencyInjection/
│       └── AiAdapterServiceCollectionExtensions.cs
│
├── CustomerSupportBot.Api/              ← Katman 4: Composition Root + HTTP yüzeyi
│   ├── Program.cs                       # DI bağlama + middleware + endpoint mapping
│   ├── appsettings.json                 # Tüm yapılandırma (AI, Persistence, Jwt, ...)
│   ├── Endpoints/                       # HTTP endpoint'leri (Minimal API)
│   │   ├── ChatEndpoints.cs             # POST /chat + /chat/stream (SSE) + GET /chat/events
│   │   ├── AuthEndpoints.cs             # /auth/login + /auth/refresh + /auth/logout
│   │   ├── RealtimeEndpoints.cs         # WS /chat/realtime + /chat/realtime-native
│   │   ├── SessionEndpoints.cs          # GET /sessions/... (debug + sidebar)
│   │   ├── TraceEndpoints.cs            # GET /traces/... (dashboard + replay)
│   │   ├── AdminEndpoints.cs            # HITL approvals + escalations + chat takeover
│   │   ├── EvaluationEndpoints.cs       # POST /evaluation/run
│   │   ├── MemoryEndpoints.cs           # /memory/stats|search|ingest (admin)
│   │   ├── ImprovementsEndpoints.cs     # /improvements/* (admin self-improve loop)
│   │   ├── WorkflowEndpoints.cs         # /workflows CRUD + test (admin)
│   │   ├── SlaEndpoints.cs              # /sla/status + events (admin)
│   │   └── ...diğer endpoint dosyaları
│   ├── Extensions/                      # DI kayıt modülleri (composition root)
│   │   ├── AiServicesExtensions.cs      # AI client + semantic memory
│   │   ├── ApplicationServicesExtensions.cs # domain servisleri + context providers
│   │   └── AuthServicesExtensions.cs    # JWT Bearer + Admin policy
│   ├── Services/                        # API katmanına özgü servisler
│   │   ├── ChatStreamOrchestrator.cs    # SSE streaming chat akışı
│   │   ├── ChatEventOrchestrator.cs     # SSE persistent event stream
│   │   └── HitlStreamSubscription.cs   # HITL approval/escalation event subscription
│   ├── Models/                          # HTTP kontrat modelleri
│   │   ├── EndpointModels.cs            # ChatRequest / ChatResponse DTO
│   │   └── AdminModels.cs               # Admin panel DTO'ları
│   ├── Prompts/                         # LLM prompt dosyaları (MD)
│   │   ├── agents/                      # 6 ajan instruction'ı
│   │   └── services/                    # Service-level prompt'lar
│   ├── KnowledgeBase/                   # RAG kaynak dokümanları (MD)
│   │   ├── iade-politikasi.md
│   │   ├── kargo-teslimat.md
│   │   └── sss.md
│   ├── Infrastructure/                  # API katmanı yardımcıları
│   │   ├── SseWriter.cs                 # SSE event helper
│   │   └── ScenarioLoader.cs            # Evaluation senaryo yükleyici
│   └── wwwroot/                         # Statik frontend
│       ├── index.html
│       ├── admin.html                   # admin paneli (Improvements tab dahil)
│       ├── replay.html                  # trace step-by-step replay UI
│       ├── css/styles.css
│       └── js/chat-ui.js / improvements.js / replay.js / traces.js
│
└── CustomerSupportBot.Api.Tests/        ← Test projesi
    ├── Agents/                          # MAF ajan testleri
    ├── Endpoints/                       # HTTP endpoint testleri
    ├── Services/                        # Uygulama servis testleri
    ├── Tools/                           # Tool fonksiyon testleri
    └── Evaluation/                      # Senaryo değerlendirme testleri

└── CustomerSupportBot.Web/              ← Blazor WASM frontend (bağımsız proje)
    └── (Detay → web/)            # Backend'e HTTP/SSE bağlantısı, ProjectReference yok
```

**Bağımlılık kuralı:**
```
Domain ← Application ← Adapters ← Api
   (sağa doğru bağımlılık yok)
```

- `Domain`: Hiçbir dış bağımlılık yok — sadece .NET BCL
- `Application`: Sadece `Domain`'e bağımlı; Adapter'ları port interface'leri üzerinden kullanır
- `Adapters.*`: Hem `Application` hem `Domain`'e referans verir; port'ları implemente eder; birbirlerine direkt bağımlı değil
- `Api`: Tüm katmanları bağlayan composition root

**Exception çeviri pattern'i**: Her adapter projesinde bir `ExceptionTranslator` (internal static) sınıfı vardır. Infrastructure exception'ları (ör. `DbUpdateConcurrencyException`, `RedisConnectionException`, `HttpRequestException`) domain exception'larına (`DomainException` hiyerarşisi) çevrilir. Application katmanı yalnızca domain exception'ları yakalar — altyapı detayları sızmaz.

## Dependency Injection haritası

DI kayıtları `Api/Extensions/` ve her adaptör projesinin `DependencyInjection/` klasöründeki extension metodlarla yapılır:

```
── AddAiServices(config) ──────────────────────────────────────────────
AiOptions             (IOptions)   ─┐  ← GetSection("AI") (Provider + alt blok)
IChatClient           (singleton)  ─┤  → AiClientFactory.CreateStandardChatClient(opts)
                                    │     → opsiyonel TelemetryChatClient sarmalama
ReasoningChatClient   (singleton)  ─┤  → AiClientFactory.CreateReasoningChatClient(opts)
                                    │     (Provider'a göre OpenAI / AzureOpenAI / Anthropic)
SemanticMemoryService (singleton)  ─┤  → Qdrant + embedding (SemanticMemory.Enabled ise)
KnowledgeBaseIngestor (hosted)     ─┘  → startup'ta Api/KnowledgeBase/*.md → Qdrant

── AddRedisServices(config) ────────────────────────────────────────────
(Redis bağlantısı yapılandırıldıysa)
IConnectionMultiplexer (singleton) ─┐  ← ConnectionStrings:Redis
IAppDistributedLock    (singleton) ─┤  → RedisDistributedLock
IMessageBusPort        (singleton) ─┘  → RedisMessageBusAdapter (pod'lar arası pub/sub)

── AddPersistenceServices(config) ─────────────────────────────────────
PersistenceOptions                 ─┐  ← GetSection("Persistence")
PromptOptions                      ─┤  ← GetSection("Prompts")
IPromptRepository      (singleton) ─┤  → FileSystemPromptRepository
                                    │
  ┌─ Provider == "Postgres" ────────┤  PostgresSessionManager, PostgresReasoningTraceStore,
  │                                 │  PostgresApprovalQueue, PostgresRatingStore,
  │                                 │  PostgresChatModeRegistry, PostgresChatBridge,
  │                                 │  PostgresEscalationSink, ...
  │                                 │  + IDbContextFactory<CustomerSupportDbContext>
  │                                 │  + PersistenceHydrator (IHostedService)
  │                                 │
  └─ Provider == "InMemory" ────────┤  InMemorySessionManager, InMemoryReasoningTraceStore,
                                    │  InMemoryApprovalQueue, InMemoryProductCatalogAdapter,
                                    │  InMemoryOrderAdapter, InMemoryMessageBusAdapter, ...
                                    │
── AddAuthenticationServices(config) ──────────────────────────────────
JwtOptions            (IOptions)   ─┤  ← GetSection("Jwt")
JwtBearerAuth                      ─┤  → access_token query string desteği (SSE için)
AuthorizationPolicy "Admin"        ─┤  → RequireRole("Admin")
IUserService / ITokenService       ─┘

── AddApplicationServices() ───────────────────────────────────────────
13 Driving Port Service  (singleton) ─┤  ISessionPort, IChatPort, IApprovalPort, IEscalationPort,
                                    │  IChatSessionPort, IHitlEventPort, ITelemetryPort, ITracePort,
                                    │  IHumanAgentPort, IImprovementsPort, IPersonalizationPort,
                                    │  IWorkflowPort, ISlaPort, IAnalyticsPort
EntityVerifier        (singleton)  ─┤  deterministic (port'lar üzerinden)
ReasoningSanityChecker(singleton)  ─┤
ReasoningService      (singleton)  ─┤
EvaluationRunner      (singleton)  ─┤  IEvaluationPort implementasyonu
CustomerSupportTeam   (singleton)  ─┤  6 agent + workflow builder (Adapters.Agents)
ApprovalGateService   (singleton)  ─┤  HITL onay kapısı (Adapters.Agents — routing delegasyonu EscalationPolicyService'e)
EscalationPolicyService(singleton)  ─┤  Eskalasyon routing politikası (Application)
InputGuard            (singleton)  ─┤  girdi güvenlik filtresi
WorkflowExecutor      (singleton)  ─┤  low-code workflow yürütücüsü
LessonMiner           (singleton)  ─┤  self-improvement lesson extraction
                                    │
IContextProvider      (singleton)  ─├─ CustomerContextProvider (Order=10)
                                    ├─ ConversationSummaryProvider (Order=5)
                                    ├─ SemanticMemoryContextProvider (Order=20, SemanticMemory aktifse)
                                    ├─ CustomerProfileContextProvider (Order=15)
ContextPipeline       (singleton)  ─┘  IContextPipeline impl — Order'a göre sıralı çalıştırır.
                                    │
SlaGuardianService    (hosted)     ─┤  BackgroundService — periyodik SLA taraması
KnowledgeBaseIngestor (hosted)     ─┤  KB → Qdrant (SemanticMemory aktifse)
CustomerProfileService(singleton)  ─┤  ICustomerProfileService impl — per-customer profil yönetimi
SkillsBasedRouter     (singleton)  ─┘  skills + dil + yük bazlı eskalasyon

── AddTelemetryServices(config) ───────────────────────────────────────
CostUsageStore        (singleton)  ─┤  model bazlı token + USD muhasebesi
OpenTelemetry tracing + metrics    ─┘  OTLP exporter (Jaeger) — Adapters.Telemetry
```

**Persistence switch:** `appsettings.json > Persistence > Provider` değerine göre aynı interface'lere farklı implementasyonlar bağlanır. Default değer **`Postgres`**'dur.

## Bir isteğin uçtan uca akışı

### `POST /chat/` — non-streaming

```
CLIENT                ChatEndpoints           ReasoningService         CustomerSupportTeam
  │                        │                        │                         │
  │── POST /chat/ ────────▶│                        │                         │
  │                        │── GetOrCreateSession ─▶│ SessionManager          │
  │                        │◀── session             │                         │
  │                        │── GetHistory ─────────▶│ ConversationStore       │
  │                        │◀── history             │                         │
  │                        │                        │                         │
  │                        │── ReasonAsync(q,s,h) ─▶│                         │
  │                        │                        │ EntityVerifier.Verify   │  ← Katman 0
  │                        │                        │  (regex+history+state   │    (deterministic)
  │                        │                        │   + repository lookup)  │
  │                        │                        │ [reasoning-system.md    │
  │                        │                        │  + verified entities    │
  │                        │                        │  + history + query]     │
  │                        │                        │── reasoning model ▶ LLM  │  ← Katman 1
  │                        │                        │◀── JSON reasoning       │
  │                        │                        │   (steps+subTasks+...)  │
  │                        │                        │ SanityChecker.Check     │  ← Katman 1.5
  │                        │                        │  (8-rule scan → issues) │    (deterministic)
  │                        │◀── ReasoningResult     │                         │
  │                        │                        │                         │
  │                        │── RunAsync(q,h,s,r) ─────────────────────────── ▶│
  │                        │                        │                         │ ShouldDecompose?
  │                        │                        │                         │   ├─ false → tek workflow
  │                        │                        │                         │   └─ true  → compound query:
  │                        │                        │                         │      her subtask için
  │                        │                        │                         │      recursive RunAsync,
  │                        │                        │                         │      sonuçları birleştir
  │                        │                        │                         │
  │                        │                        │                         │ (tek akış için:)
  │                        │                        │                         │ ContextPipeline.Build
  │                        │                        │                         │   + IdExtractor.Extract
  │                        │                        │                         │   + reasoning hint (+subTasks)
  │                        │                        │                         │
  │                        │                        │                         │ Workflow execution:
  │                        │                        │                         │   PlanningAgent ▶ LLM     ← Katman 2
  │                        │                        │                         │   ChatManager selects next
  │                        │                        │                         │   Specialist ▶ tool call ← Katman 3
  │                        │                        │                         │   ChatManager detects
  │                        │                        │                         │     postToolReflection
  │                        │                        │                         │   ResponseAgent ▶ TERMINATE ← Katman 4
  │                        │                        │                         │
  │                        │                        │                         │ Trace kayıt edilir
  │                        │◀── response text ──────────────────────────────  │
  │                        │                        │                         │
  │                        │── AddExchange ───────▶ SessionManager            │
  │◀── JSON { response, sessionId, reasoning } ─│                             │
```

Bu akış **dört adım** olarak yapılandırılmıştır. **Katman 0 + 1.5** deterministic (LLM'siz) — reasoning service içinde şeffaf. `InputGuard` bu akıştan önce girdiyi kontrol eder ve gerekirse reddeder.

### Compound query decomposition

Reasoning `SubTasks.Count >= 2` ve 2+ farklı targetAgent üretirse, `CustomerSupportTeam` tek bir workflow yerine **N workflow** çalıştırır:

```
RunAsync(query, reasoning)
    └─ ShouldDecompose(reasoning) == true
        └─ RunDecomposedAsync:
              ├─ subTask #1 (OrderAgent, ORD-1)
              │   └─ Recursive RunAsync (subReasoning.SubTasks=[])
              │      └─ Tam planning→specialist→response döngüsü
              ├─ subTask #2 (ComplaintAgent, ORD-2)
              │   └─ history[önceki sonuç eklendi]
              │   └─ Recursive RunAsync
              └─ JoinAggregatedParts(results)
                  → "**1) ...**\n\n<r1>\n\n---\n\n**2) ...**\n\n<r2>"
```

Detay → [domain/Model-Reasoning.md](domain/Model-Reasoning.md).

### `POST /chat/stream` — SSE streaming

Benzer akış ama her adım ayrı bir SSE event'i olarak akıtılır:

| Event tipi | Ne zaman? | Veri |
|---|---|---|
| `session` | Başta | `{ sessionId }` |
| `reasoning_start` | Reasoning başlangıcı | `null` |
| `reasoning_delta` | Her ~20ms reasoning token chunk'ı | `{ text }` |
| `reasoning_complete` | Reasoning JSON parse + sanity check tamamlanınca | `ReasoningResult` (`steps`, `subTasks`, `sanityIssues` dahil) |
| `agent` | Her executor invoke/complete / orchestrator / subtask boundary | `{ name, status, ...[decomposed metadata] }` |
| `response_start` | ResponseAgent TERMINATE üretince veya aggregated sonuç hazırsa | `{ terminationReason, [decomposed, subTaskCount] }` |
| `response_delta` | Kelime kelime son yanıt | `{ text }` |
| `response_complete` | Yanıt bitince | `{ text, terminationReason, [decomposed, subTaskCount] }` |
| `done` | Stream sonu | `{ sessionId }` |
| `error` | Hata / timeout | `{ message }` |

Bkz. `Models/StreamEvent.cs` — tüm tip sabitleri.

**Compound query ek event'leri** (compound query sırasında):

| Sıra | Event | Veri |
|---|---|---|
| 1 | `agent` | `{ name: "Orchestrator", status: "decomposing", subTaskCount: N }` |
| 2 | `agent` | `{ name: "SubTask#1", status: "running", description, targetAgent, order, total }` |
| ... | (her subtask için iç workflow event'leri forward edilir — PlanningAgent, specialist, ResponseAgent) | — |
| 2' | `agent` | `{ name: "SubTask#1", status: "done", order }` |
| ... | (2-2' tekrarlanır N kez) | — |
| son | `agent` | `{ name: "Orchestrator", status: "aggregating" }` |
| son+1 | `response_start/delta/complete` | aggregated text, `decomposed: true` bayrağıyla |

İç workflow'un `response_start/delta/complete` event'leri **yutulur** (subtask seviyesinde dışarı çıkmaz) — yalnızca final aggregated response gerçek `response_*` event'leri olarak yayılır. Böylece UI tek bir yanıt bloğu görür.

## İki LLM, iki rol

Tasarımda **iki ayrı chat client** kullanılır. Her ikisi de `AiClientFactory` tarafından seçili sağlayıcı (OpenAI / Azure OpenAI / Anthropic) için üretilir:

1. **`IChatClient`** (default: OpenAI → `gpt-5.4`, Azure → deployment config, Anthropic → `claude-haiku-4-5`) — tüm ajanlar, ConversationSummaryProvider, RewriteRoutingMessage, LessonMiner.
2. **`ReasoningChatClient`** (default: OpenAI → `gpt-5.4-nano`, Azure → reasoning deployment, Anthropic → reasoning modeli) — **sadece** `ReasoningService` kullanır; OpenAI/Azure'da `reasoning_effort` parametresi gönderilir.

Bu ayrım sayesinde:
- Ön-analiz (niyet tespiti, requiredInfo) **daha uzun iç düşünme** zamanı olan o-series modelde yapılır.
- Esas müşteri yanıtı, araç çağrısı ve ton duyarlı çıktılar **daha hızlı ve daha ucuz** gpt-4o'da üretilir.
- İki modeli bağımsız upgrade/downgrade edebilirsiniz (ayrı config anahtarları).

## Uygulama başlangıç sırası

`Program.cs` incelenirse:

1. **Config okunur** — `AI:Provider` ile sağlayıcı seçilir; ilgili sağlayıcının alt bloğundaki zorunlu alanlar (`ApiKey`, `Endpoint` vb.) eksikse `AiClientFactory` `InvalidOperationException` fırlatır.
2. **DI modülleri** çağrılır: `AddTelemetryServices` → `AddAiServices` → `AddRedisServices` → `AddPersistenceServices` → `AddApplicationServices` → `AddAuthenticationServices`.
3. **`MigrateIfDevelopmentAsync`** — Development ortamında PostgreSQL migration'ları otomatik çalışır.
4. **`WireRoutingLoadTracking`** — Eskalasyon çözümlendiğinde insan temsilci yükünü otomatik azaltan event subscription'ı bağlar.
5. **Middleware pipeline**: CORS → Rate Limiter → Static Files → WebSockets → Auth → Authorization.
6. **Endpoint mapping**: Public (chat, realtime, session, auth) + Admin scope (trace, eval, memory, improvements, telemetry, personalization, agents, workflows, SLA) + Analytics.
7. **IHostedService'ler** başlatılır: `KnowledgeBaseIngestor` (KB → Qdrant), `SlaGuardianService` (periyodik SLA taraması), `PersistenceHydrator` (seed data).
8. **`CustomerSupportTeam` construct edildiğinde** 6 agent yaratılır ve OpenTelemetry middleware ile sarılır — **bu lazy'dir**, ilk `/chat/` isteğinde tetiklenir.
9. `app.Run()` ile Kestrel dinlemeye başlar.

## Veri yaşam döngüsü (session + trace)

Varsayılan persistence provider **Postgres**'dur (`appsettings.json > Persistence > Provider`). InMemory fallback geliştirme/test amaçlıdır.

| Veri | Postgres modu | InMemory modu |
|------|---------------|---------------|
| **Session** | `PostgresSessionManager` — kalıcı, restart'a dayanıklı | `InMemorySessionManager` — restart'ta sıfırlanır |
| **History** | Mesajlar `MessageEntity` olarak DB'de saklanır | `List<ChatMessage>` bellekte birikir |
| **Trace** | `PostgresReasoningTraceStore` — kalıcı | Ring buffer, max 500 |
| **HITL Approvals** | `PostgresApprovalQueue` | `InMemoryApprovalQueue` |
| **Ratings** | `PostgresRatingStore` | `InMemoryRatingStore` |
| **Auth (Users)** | `UserEntity` + `RefreshTokenEntity` (her zaman Postgres) | — |

- **History optimizasyonu**: `ConversationSummaryProvider` 8+ mesaj olunca eski mesajları LLM ile özetleyip `SessionState.ConversationSummary` alanına yazar — token tasarrufu.
- **Episodic memory**: Her workflow tamamlandığında soru+yanıt+intent Qdrant'a vektör olarak yazılır (fire & forget).

## Genişletme noktaları

| İhtiyaç | Nereye dokun? |
|---|---|
| Yeni ajan ekle | `Api/Prompts/agents/<name>.md` + `Adapters.Agents/CustomerSupportTeam` ctor + specialist list güncellemesi |
| Yeni tool | `Application/Services/CustomerSupportToolsService.cs` + ilgili ajanın `tools:` listesi |
| Yeni prompt | `Api/Prompts/services/<key>.md` + `_prompts.Get()` / `Render()` çağrısı |
| Yeni context bilgisi | `Application/Services/Providers/` altında yeni `IContextProvider` impl + `Api/Extensions/ApplicationServicesExtensions.cs`'de kayıt |
| Yeni endpoint | `Api/Endpoints/<Feature>Endpoints.cs` extension sınıfı + `Api/Program.cs` map çağrısı |
| Yeni repository port'u | `Application/Ports/Driven/Persistence/` altında interface + Postgres/InMemory implementasyonları + `AddPersistenceAdapters`'da kayıt |
| Farklı LLM sağlayıcı | `Adapters.AI`'da yeni factory + `AI:Provider` config değeri; Application katmanı etkilenmez |
| Yeni message bus | `Application/Ports/Driven/Messaging/IMessageBusPort.cs` implementasyonu + DI kaydı |

Detaylar → [developer-guide.md](developer-guide.md).

## Multi-Provider Stratejisi

Sistem, altyapı bileşenlerini çalışma zamanında değiştirmeye olanak tanıyan **runtime provider switch** deseni kullanır. Bu strateji hexagonal mimarinin (ports & adapters) doğal bir uzantısıdır — tüm altyapı erişimi interface (port) arkasındadır ve DI composition root'unda hangi adaptörün bağlanacağı yapılandırma ile belirlenir.

### AI Provider Switch

```
appsettings.json → AI:Provider
```

| Değer | Chat Client | Reasoning Client | Gerekli Config |
|-------|-------------|------------------|----------------|
| `OpenAI` | `gpt-5.4` | `gpt-5.4-nano` | `AI:OpenAI:ApiKey` |
| `AzureOpenAI` | deployment config | reasoning deployment | `AI:AzureOpenAI:Endpoint`, `ApiKey` |
| `Anthropic` | `claude-haiku-4-5` | `claude-haiku-4-5` | `AI:Anthropic:ApiKey` |

**Karar noktası:** `AiClientFactory.CreateStandardChatClient()` ve `CreateReasoningChatClient()` — `AI:Provider` değerine göre sadece ilgili sağlayıcının alt bloğundaki alanlar zorunlu kılınır.

**İki bağımsız model:** Standard (hız/maliyet optimize) + Reasoning (derin düşünme). Bağımsız upgrade/downgrade yapılabilir.

### Persistence Provider Switch

```
appsettings.json → Persistence:Provider
```

| Değer | Adaptörler | Kullanım |
|-------|-----------|----------|
| `Postgres` | `PostgresSessionManager`, `PostgresApprovalQueue`, `PostgresEscalationSink`, ... | Production — kalıcı, yatay ölçeklenebilir |
| `InMemory` | `InMemorySessionManager`, `InMemoryApprovalQueue`, `InMemoryEscalationSink`, ... | Development, test — restart'ta sıfırlanır |

**Karar noktası:** `PersistenceAdapterServiceCollectionExtensions.AddPersistenceAdapters()` — tek `if/else` ile 14+ repository aynı interface'lere farklı implementasyonlar bağlar.

### Message Bus Provider Switch

```
Redis varsa → RedisMessageBusAdapter (pod'lar arası pub/sub)
Redis yoksa → InMemoryMessageBusAdapter (tek instance, lokal pub/sub)
```

**Karar noktası:** `IMessageBusPort` — Redis adaptörü DI'da kayıtlıysa Redis kullanılır; InMemory modda `TryAddSingleton` ile fallback devreye girer. Persistence adaptörleri hiçbir zaman doğrudan Redis'e bağımlı değildir.

### Prompt Source Switch

```
appsettings.json → Prompts:RootPath (opsiyonel)
```

| Senaryo | Yapılandırma |
|---------|-------------|
| Varsayılan (local) | Boş bırakılır → `AppContext.BaseDirectory/Prompts` |
| Azure Files mount | `"/mnt/shared/prompts"` |
| NFS volume | `"/data/prompts"` |
| Göreli path | `"../shared-prompts"` → BaseDirectory'e göre çözümlenir |

### Strateji Özeti

```
                    ┌─────────────────────────────────────────┐
                    │          appsettings.json                │
                    │  AI:Provider      = OpenAI|Azure|Anthro  │
                    │  Persistence:Provider = Postgres|InMemory│
                    │  Prompts:RootPath = (opsiyonel path)     │
                    │  ConnectionStrings:Redis = (gerekli)     │
                    └──────────────┬──────────────────────────┘
                                   │ IConfiguration
                    ┌──────────────▼──────────────────────────┐
                    │      Program.cs (Composition Root)       │
                    │  .AddTelemetryServices(config)           │
                    │  .AddAiServices(config)                  │
                    │  .AddRedisServices(config)               │
                    │  .AddPersistenceServices(config)         │
                    │  .AddApplicationServices(config)         │
                    │  .AddAuthenticationServices(config)      │
                    └──────────────┬──────────────────────────┘
                                   │ DI Container
                    ┌──────────────▼──────────────────────────┐
                    │        Runtime Port Bindings             │
                    │  IChatClient ──→ OpenAI | Azure | Anthr. │
                    │  ISessionManager ──→ Postgres | InMem    │
                    │  IMessageBusPort ──→ Redis | InMemory    │
                    │  IDistributedLockPort ──→ Redis          │
                    │  IPromptRepository ──→ FileSystem(path)  │
                    └─────────────────────────────────────────┘
```

**Yeni provider ekleme:**
1. `Application/Ports/Driven/` altında yeni port tanımla (veya mevcut portu kullan)
2. Yeni adapter projesi veya mevcut adapter'da implementasyon yaz
3. DI extension metodunda yapılandırmaya göre kayıt ekle
4. `appsettings.json`'a yeni provider bloğu ekle

## Çapraz referanslar

- **Her class/interface ne iş yapar?** → [class-reference.md](class-reference.md)
- **HTTP endpoint şemaları + SSE event payload'ları** → [api/](api/README.md)
- **Agent davranışı + iç sub-component anatomisi** → [adapters-agents/](adapters-agents/README.md)
- **Workflow akışı + Compound query orkestrasyon** → [domain/Model-Workflow.md](domain/Model-Workflow.md)
- **Reasoning pipeline katmanları** → [domain/Model-Reasoning.md](domain/Model-Reasoning.md)
- **Tasarım pattern'leri** → [agentic-patterns.md](agentic-patterns.md)
- **Semantic memory, Self-Improving Loop, Replay, Personalization** → [intelligence.md](intelligence.md)
- **Sesli konuşma (Realtime)** → [adapters-ai/Realtime.md](adapters-ai/Realtime.md)
- **Low-code workflow designer** → [web/Pages-Workflow.md](web/Pages-Workflow.md)
- **Güvenlik ve kimlik doğrulama** → [security.md](security.md)
- **Telemetri ve maliyet takibi** → [adapters-telemetry/](adapters-telemetry/README.md)
- **Veritabanı ve kalıcılık** → [adapters-persistence/](adapters-persistence/README.md)
- **Kurulum ve dağıtım** → [deployment.md](deployment.md)
- **Çalıştırma ve operasyon** → [operations.md](operations.md)
- **Geliştirici rehberi** → [developer-guide.md](developer-guide.md)
