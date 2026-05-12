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
└───┬───────────┘  │ (7 agents +    │  │  InMemory)  │  └──────────┘
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
    │     (FakeDatabase)               (LLM özet)
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
│  FakeDatabase (Product/Order/Complaint demo)  │  IdExtractor (regex)│
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

## Dizin yapısı

```
├── Program.cs                       # DI + endpoint mapping
├── appsettings.json                 # AI + Persistence + Jwt + WorkflowGuards + HumanInTheLoop + Routing + Telemetry + SLA
│
├── Agents/                          # Ajan orkestrasyonu
│   ├── CustomerSupportTeam.cs       # 7 agent + workflow builder + streaming pump
│   ├── CustomerSupportChatManager.cs# GroupChatManager türevi — seçim + terminasyon
│   ├── SubTaskOrchestrator.cs       # Compound query alt görev partitioning
│   ├── ApprovalGateService.cs       # HITL approval gate (yan etkili tool'lar)
│   ├── WorkflowResponseExtractor.cs # Workflow çıktı temizleme
│   └── Routing/                     # Ajan seçim stratejileri
│
├── Endpoints/                       # HTTP yüzeyi
│   ├── ChatEndpoints.cs             # POST /chat + /chat/stream (SSE) + GET /chat/events
│   ├── AuthEndpoints.cs             # /auth/login + /auth/refresh + /auth/logout
│   ├── RealtimeEndpoints.cs         # WS /chat/realtime + /chat/realtime-native
│   ├── SessionEndpoints.cs          # GET /sessions/... (debug + sidebar)
│   ├── TraceEndpoints.cs            # GET /traces/... (dashboard + replay)
│   ├── AdminEndpoints.cs            # HITL approvals + escalations + chat takeover (Admin)
│   ├── AgentPanelEndpoints.cs       # /agent/* — agent eskalasyon/onay/sohbet (AdminOrAgent)
│   ├── EvaluationEndpoints.cs       # POST /evaluation/run
│   ├── MemoryEndpoints.cs           # /memory/stats|search|ingest (admin)
│   ├── ImprovementsEndpoints.cs     # /improvements/* (admin self-improve loop)
│   ├── TelemetryEndpoints.cs        # /telemetry/cost (admin maliyet takip)
│   ├── PersonalizationEndpoints.cs  # /customers/... (admin profil yönetimi)
│   ├── AgentsEndpoints.cs           # /agents CRUD (insan temsilci kayıt)
│   ├── WorkflowEndpoints.cs         # /workflows CRUD + test (admin)
│   ├── SlaEndpoints.cs              # /sla/status + events (admin)
│   ├── AnalyticsEndpoints.cs        # /analytics/dashboard + ratings
│   └── SseWriter.cs                 # SSE event helper
│
├── Evaluation/                      # Senaryo tabanlı test
│   ├── EvaluationRunner.cs          # YAML → senaryoları koştur
│   ├── CriteriaEvaluator.cs         # Her criterion için pass/fail
│   └── ScenarioModels.cs            # YAML şeması
│
├── Models/                          # Domain + DTO
│   ├── ChatRequest/Response.cs      # HTTP kontratı
│   ├── StreamEvent.cs               # SSE event tipleri
│   ├── AgentSession.cs              # oturum + SessionState
│   ├── PlanningResult.cs            # PlanningAgent çıktı şeması
│   ├── ReasoningResult.cs           # ReasoningService çıktı şeması (+steps+subTasks+sanityIssues)
│   ├── ReasoningStep.cs             # structured step (order/action/grounding/conf)
│   ├── ReasoningIssue.cs            # sanity check issue (code/severity/fix)
│   ├── SubTask.cs                   # compound query alt görev
│   ├── VerifiedEntities.cs          # entity grounding sonucu (DB verify)
│   ├── SpecialistReasoning.cs       # Specialist pre/post-tool JSON
│   ├── ReasoningTrace.cs            # tüm trace modeli
│   ├── ToolResult.cs                # tool dönüş zarfı (+ ToolError)
│   ├── WorkflowGuardOptions.cs      # timeout/iteration/token limiti
│   ├── ProductInfo.cs / OrderInfo.cs / ComplaintInfo.cs
│   └── FakeDatabase.cs              # in-memory "DB"
│
├── Prompts/                         # LLM prompt'ları (MD)
│   ├── agents/                      # 7 ajan instruction'ı
│   │   ├── planning-agent.md
│   │   ├── product-inquiry-agent.md
│   │   ├── order-placement-agent.md
│   │   ├── order-inquiry-agent.md
│   │   ├── complaint-agent.md
│   │   ├── human-handoff-agent.md
│   │   └── response-agent.md
│   └── services/                    # service-level prompt'lar
│       ├── reasoning-system.md
│       ├── reasoning-history-note.md
│       ├── reasoning-hint.md
│       ├── routing-rewrite-system.md
│       └── routing-rewrite-user.md
│
├── Services/                        # Domain servisleri
│   ├── PromptService.cs             # MD loader + {{placeholder}} render
│   ├── ReasoningService.cs          # o-series ön-analiz (streaming de destekler)
│   ├── ReasoningChatClient.cs       # IChatClient wrapper (reasoning model)
│   ├── EntityVerifier.cs            # Katman 0 — entity extract + DB verify (no LLM)
│   ├── ReasoningSanityChecker.cs    # Katman 1.5 — IReasoningSanityRule + 8 rule sınıfı
│   ├── ChatStreamOrchestrator.cs    # SSE streaming chat akışı
│   ├── ChatEventOrchestrator.cs     # SSE persistent event stream (per-session)
│   ├── HitlStreamSubscription.cs    # HITL approval/escalation event subscription
│   ├── ChatEventSubscription.cs     # Mode change + escalation lifecycle subscription
│   ├── IdExtractor.cs               # regex ile ID çıkarımı
│   ├── ContextPipeline.cs           # provider zinciri
│   ├── IContextProvider.cs          # provider arayüzü
│   ├── Providers/
│   │   ├── CustomerContextProvider.cs        # FakeDatabase'den müşteri geçmişi
│   │   ├── ConversationSummaryProvider.cs    # 8+ mesaj → LLM özet
│   │   ├── SemanticMemoryContextProvider.cs  # Qdrant RAG (KB + lessons)
│   │   └── CustomerProfileContextProvider.cs # Per-customer profil enjeksiyon
│   ├── Memory/                          # Semantic memory (Qdrant + embedding)
│   │   ├── IEmbeddingService.cs / OpenAiEmbeddingService.cs
│   │   ├── IVectorMemoryStore.cs / QdrantVectorMemoryStore.cs
│   │   ├── SemanticMemoryService.cs        # facade (Episodic/Lessons/Knowledge)
│   │   └── KnowledgeBaseIngestor.cs        # IHostedService (KB → chunks → Qdrant)
│   ├── Improvement/                     # Self-improving loop
│   │   ├── ILessonStore.cs / InMemoryLessonStore.cs
│   │   └── LessonMiner.cs                  # mine + approve + reject
│   ├── Auth/                            # JWT kimlik doğrulama
│   │   ├── UserService.cs / TokenService.cs
│   │   └── IPasswordHasher.cs (BCrypt)
│   ├── Personalization/                 # Per-customer profil
│   │   ├── CustomerProfileService.cs
│   │   └── InMemoryCustomerProfileStore.cs
│   ├── Routing/                         # Skills-based eskalasyon
│   │   └── InMemoryHumanAgentRegistry.cs
│   ├── Sla/
│   │   └── SlaGuardianService.cs        # BackgroundService — onay/eskalasyon SLA
│   ├── Workflow/                        # Low-code deterministik workflow
│   │   ├── WorkflowExecutor.cs
│   │   └── InMemoryWorkflowDefinitionStore.cs
│   ├── Realtime/                        # Sesli konuşma (OpenAI Realtime API)
│   │   └── RealtimeFunctionTools.cs
│   ├── Telemetry/                       # Maliyet + token takip
│   │   └── CostUsageStore.cs
│   ├── Persistence/                     # Postgres implementasyonları
│   │   ├── PostgresSessionManager.cs
│   │   ├── PostgresReasoningTraceStore.cs
│   │   ├── PostgresApprovalQueue.cs
│   │   └── PostgresRatingStore.cs
│   ├── ISessionManager.cs              # oturum arayüzü
│   ├── InMemorySessionManager.cs       # in-memory fallback
│   ├── IReasoningTraceStore.cs         # trace arayüzü
│   ├── InMemoryReasoningTraceStore.cs  # ring-buffer fallback (max 500)
│   ├── InputGuard.cs                   # Girdi güvenlik filtresi
│   ├── PlanningResultParser.cs         # PlanningAgent JSON parser
│   └── SpecialistReasoningParser.cs    # specialist JSON parser
│
├── Infrastructure/
│   └── Persistence/                    # EF Core 10 + PostgreSQL
│       ├── CustomerSupportDbContext.cs  # 10 DbSet
│       ├── Entities/                   # Auth, Chat, Hitl, Analytics, Observability
│       ├── Configurations/             # EF fluent config'ler
│       ├── Migrations/                 # Code-first migration'lar
│       └── PersistenceHydrator.cs      # Startup seed/hydration
│
├── Extensions/                         # DI kayıt modülleri
│   ├── AiServicesExtensions.cs         # AI client + semantic memory
│   ├── ApplicationServicesExtensions.cs# CORS, rate limit, domain servisleri
│   ├── AuthServicesExtensions.cs       # JWT Bearer + Admin policy
│   ├── PersistenceServicesExtensions.cs# InMemory ↔ Postgres switch
│   └── TelemetryExtensions.cs          # OpenTelemetry trace + metric
│
├── Tools/
│   └── CustomerSupportTools.cs      # 7 static tool fonksiyonu (+ idempotency cache)
│
├── KnowledgeBase/                    # RAG kaynak dokümanları (md)
│   ├── iade-politikasi.md
│   ├── kargo-teslimat.md
│   └── sss.md
│
└── wwwroot/                         # Statik frontend
    ├── index.html
    ├── admin.html                   # admin paneli (Improvements tab dahil)
    ├── replay.html                  # trace step-by-step replay UI
    ├── css/styles.css
    ├── js/chat-ui.js / improvements.js / replay.js / traces.js
    └── chatbot.png / user.png
```

## Dependency Injection haritası

DI kayıtları `Extensions/` altındaki modüller aracılığıyla yapılır:

```
── AddAiServices(config) ──────────────────────────────────────────────
AiOptions             (IOptions)   ─┐  ← GetSection("AI") (Provider + alt blok)
IChatClient           (singleton)  ─┤  → AiClientFactory.CreateStandardChatClient(opts)
                                    │     → opsiyonel TelemetryChatClient sarmalama
ReasoningChatClient   (singleton)  ─┤  → AiClientFactory.CreateReasoningChatClient(opts)
                                    │     (Provider'a göre OpenAI / AzureOpenAI / Anthropic)
SemanticMemoryService (singleton)  ─┤  → Qdrant + embedding (SemanticMemory.Enabled ise)
KnowledgeBaseIngestor (hosted)     ─┘  → startup'ta KnowledgeBase/*.md → Qdrant

── AddPersistenceServices(config) ─────────────────────────────────────
PersistenceOptions                 ─┐  ← GetSection("Persistence")
                                    │
  ┌─ Provider == "Postgres" ────────┤  PostgresSessionManager, PostgresReasoningTraceStore,
  │                                 │  PostgresApprovalQueue, PostgresRatingStore, ...
  │                                 │  + IDbContextFactory<CustomerSupportDbContext>
  │                                 │  + PersistenceHydrator (IHostedService)
  │                                 │
  └─ Provider == "InMemory" ────────┤  InMemorySessionManager, InMemoryReasoningTraceStore,
                                    │  InMemoryApprovalQueue, InMemoryRatingStore, ...
                                    │
── AddAuthenticationServices(config) ──────────────────────────────────
JwtOptions            (IOptions)   ─┤  ← GetSection("Jwt")
JwtBearerAuth                      ─┤  → access_token query string desteği (SSE için)
AuthorizationPolicy "Admin"        ─┤  → RequireRole("Admin")
IUserService / ITokenService       ─┘

── AddApplicationServices() ───────────────────────────────────────────
PromptService         (singleton)  ─┤  startup'ta Prompts/**/*.md yükler
EntityVerifier        (singleton)  ─┤  deterministic
ReasoningSanityChecker(singleton)  ─┤
ReasoningService      (singleton)  ─┤
CustomerSupportTeam   (singleton)  ─┤  7 agent + workflow builder
ApprovalGateService   (singleton)  ─┤  HITL onay kapısı
InputGuard            (singleton)  ─┤  girdi güvenlik filtresi
                                    │
IContextProvider      (singleton)  ─├─ CustomerContextProvider (Order=10)
                                    ├─ ConversationSummaryProvider (Order=5)
                                    ├─ SemanticMemoryContextProvider (Order=20)
                                    ├─ CustomerProfileContextProvider (Order=15)
ContextPipeline       (singleton)  ─┘  Order'a göre sıralı çalıştırır.
                                    │
SlaGuardianService    (hosted)     ─┤  BackgroundService — periyodik SLA taraması
CustomerProfileService(singleton)  ─┤  per-customer profil yönetimi
SkillsBasedRouter     (singleton)  ─┘  skills + dil + yük bazlı eskalasyon

── AddTelemetryServices(config) ───────────────────────────────────────
CostUsageStore        (singleton)  ─┤  model bazlı token + USD muhasebesi
OpenTelemetry tracing + metrics    ─┘  OTLP exporter (Jaeger)
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
  │                        │                        │   + FakeDatabase lookup)│
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
              ├─ subTask #1 (OrderInquiryAgent, ORD-1)
              │   └─ Recursive RunAsync (subReasoning.SubTasks=[])
              │      └─ Tam planning→specialist→response döngüsü
              ├─ subTask #2 (ComplaintAgent, ORD-2)
              │   └─ history[önceki sonuç eklendi]
              │   └─ Recursive RunAsync
              └─ JoinAggregatedParts(results)
                  → "**1) ...**\n\n<r1>\n\n---\n\n**2) ...**\n\n<r2>"
```

Detay → [reasoning.md#compound-query--tam-orkestrasyon-tamamlandı](reasoning.md).

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
2. **DI modülleri** çağrılır: `AddTelemetryServices` → `AddAiServices` → `AddPersistenceServices` → `AddApplicationServices` → `AddAuthenticationServices`.
3. **`MigrateIfDevelopmentAsync`** — Development ortamında PostgreSQL migration'ları otomatik çalışır.
4. **`WireRoutingLoadTracking`** — Eskalasyon çözümlendiğinde insan temsilci yükünü otomatik azaltan event subscription'ı bağlar.
5. **Middleware pipeline**: CORS → Rate Limiter → Static Files → WebSockets → Auth → Authorization.
6. **Endpoint mapping**: Public (chat, realtime, session, auth) + Admin scope (trace, eval, memory, improvements, telemetry, personalization, agents, workflows, SLA) + Analytics.
7. **IHostedService'ler** başlatılır: `KnowledgeBaseIngestor` (KB → Qdrant), `SlaGuardianService` (periyodik SLA taraması), `PersistenceHydrator` (seed data).
8. **`CustomerSupportTeam` construct edildiğinde** 7 agent yaratılır ve OpenTelemetry middleware ile sarılır — **bu lazy'dir**, ilk `/chat/` isteğinde tetiklenir.
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
| Yeni ajan ekle | `Prompts/agents/<name>.md` + `CustomerSupportTeam` ctor + specialist list güncellemesi |
| Yeni tool | `CustomerSupportTools` + ilgili ajanın `tools:` listesi |
| Yeni prompt | `Prompts/services/<key>.md` + `_prompts.Get()` / `Render()` çağrısı |
| Yeni context bilgisi | Yeni `IContextProvider` impl + `Program.cs`'de kayıt |
| Yeni endpoint | `Endpoints/*.cs` extension sınıfı + `Program.cs` map çağrısı |
| Kalıcı session/trace | `ISessionManager` / `IReasoningTraceStore` için yeni impl + `Program.cs` kaydı |
| Farklı LLM sağlayıcı | `IChatClient` factory'sini değiştir; MAF abstraction katmanı korur |

Detaylar → [developer-guide.md](developer-guide.md).

## Çapraz referanslar

- **Her class/interface ne iş yapar?** → [reference.md](reference.md)
- **HTTP endpoint şemaları + SSE event payload'ları** → [api.md](api.md)
- **Agent davranışı + iç sub-component anatomisi** → [agents.md](agents.md)
- **Workflow akışı + Compound query orkestrasyon** → [workflow.md](workflow.md)
- **Reasoning pipeline katmanları** → [reasoning.md](reasoning.md)
- **Tasarım pattern'leri** → [patterns.md](patterns.md)
- **Semantic memory, Self-Improving Loop, Replay, Personalization** → [intelligence.md](intelligence.md)
- **Sesli konuşma (Realtime)** → [realtime.md](realtime.md)
- **Low-code workflow designer** → [workflow-designer.md](workflow-designer.md)
- **Güvenlik ve kimlik doğrulama** → [security.md](security.md)
- **Telemetri ve maliyet takibi** → [telemetry.md](telemetry.md)
- **Veritabanı ve kalıcılık** → [persistence.md](persistence.md)
- **Kurulum ve dağıtım** → [deployment.md](deployment.md)
- **Geliştirici rehberi** → [developer-guide.md](developer-guide.md)
