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
│   ChatEndpoints │ SessionEndpoints │ TraceEndpoints │ Evaluation │
└───────┬───────────────────┬──────────────────┬──────────────┬────┘
        │                   │                  │              │
        ▼                   ▼                  ▼              ▼
┌───────────────┐  ┌────────────────┐  ┌─────────────┐  ┌──────────┐
│ ReasoningSvc  │  │ CustomerSupp.  │  │ TraceStore  │  │ Evaluat. │
│  (o-series)   │─▶│ Team           │─▶│ (in-memory) │  │ Runner   │
└───┬───────────┘  │ (6 agents +    │  └─────────────┘  └──────────┘
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
    │     CustomerContext                         ConversationSummary
    │     (FakeDatabase)                          (LLM özet)
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
│  PostgreSQL (sessions/traces/approvals/escalations/lessons*)     │
│  Qdrant (cs_knowledge / cs_episodic / cs_lessons collections)    │
│  FakeDatabase (Product/Order/Complaint demo)  │  IdExtractor (regex)│
└─────────────────────────────────────────────────────────────────┘

(*) Lesson'lar şu an in-memory; PostgresLessonStore opsiyoneldir.

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
├── Program.cs                       # DI + endpoint mapping (89 satır)
├── appsettings.json                 # AI (OpenAI / AzureOpenAI / Anthropic) + WorkflowGuards + HumanInTheLoop
│
├── Agents/                          # Ajan orkestrasyonu
│   ├── CustomerSupportTeam.cs       # 6 agent + workflow builder + streaming pump
│   └── CustomerSupportChatManager.cs# GroupChatManager türevi — seçim + terminasyon
│
├── Endpoints/                       # HTTP yüzeyi
│   ├── ChatEndpoints.cs             # POST /chat + /chat/stream (SSE)
│   ├── SessionEndpoints.cs          # GET /sessions/... (debug + sidebar)
│   ├── TraceEndpoints.cs            # GET /traces/... (dashboard + replay)
│   ├── EvaluationEndpoints.cs       # POST /evaluation/run
│   ├── MemoryEndpoints.cs           # /memory/stats|search|ingest (admin)
│   ├── ImprovementsEndpoints.cs     # /improvements/* (admin self-improve loop)
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
│   ├── agents/                      # 6 ajan instruction'ı
│   │   ├── planning-agent.md
│   │   ├── product-inquiry-agent.md
│   │   ├── order-placement-agent.md
│   │   ├── order-inquiry-agent.md
│   │   ├── complaint-agent.md
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
│   │   ├── CustomerContextProvider.cs   # FakeDatabase'den müşteri geçmişi
│   │   ├── ConversationSummaryProvider.cs# 8+ mesaj → LLM özet
│   │   └── SemanticMemoryContextProvider.cs # Qdrant RAG (KB + lessons)
│   ├── Memory/                          # Semantic memory (Qdrant + embedding)
│   │   ├── IEmbeddingService.cs / OpenAiEmbeddingService.cs
│   │   ├── IVectorMemoryStore.cs / QdrantVectorMemoryStore.cs
│   │   ├── SemanticMemoryService.cs        # facade (Episodic/Lessons/Knowledge)
│   │   └── KnowledgeBaseIngestor.cs        # IHostedService (KB → chunks → Qdrant)
│   ├── Improvement/                     # Self-improving loop
│   │   ├── ILessonStore.cs / InMemoryLessonStore.cs
│   │   └── LessonMiner.cs                  # mine + approve + reject
│   ├── ISessionManager.cs           # oturum arayüzü
│   ├── IConversationStore.cs        # geçmiş arayüzü (aynı sınıf implement eder)
│   ├── InMemorySessionManager.cs    # in-memory session + history
│   ├── ConversationStore.cs         # geçmiş yardımcıları
│   ├── IReasoningTraceStore.cs      # trace arayüzü
│   ├── InMemoryReasoningTraceStore.cs# ring-buffer (max 500)
│   ├── PlanningResultParser.cs      # PlanningAgent JSON parser
│   └── SpecialistReasoningParser.cs # specialist JSON parser
│
├── Tools/
│   └── CustomerSupportTools.cs      # 6 static tool fonksiyonu
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

`@Program.cs:24-67` kayıtları:

```
AiOptions             (IOptions)   ─┐  ← GetSection("AI") (Provider + alt blok)
                                    │
IChatClient           (singleton)  ─┤  → AiClientFactory.CreateStandardChatClient(opts)
ReasoningChatClient   (singleton)  ─┤  → AiClientFactory.CreateReasoningChatClient(opts)
                                    │     (Provider'a göre OpenAI / AzureOpenAI / Anthropic)
PromptService         (singleton)  ─┤  startup'ta Prompts/**/*.md yükler
EntityVerifier        (singleton)  ─┤  no dependencies, deterministic
ReasoningSanityChecker(singleton)  ─┤  ILogger bağımlılığı var
ReasoningService      (singleton)  ─┤  depends: ReasoningChatClient, PromptService,
                                    │             EntityVerifier, ReasoningSanityChecker
CustomerSupportTeam   (singleton)  ─┤  depends: IChatClient, ContextPipeline, IConfiguration,
                                    │             IReasoningTraceStore, PromptService
EvaluationRunner      (singleton)  ─┤  depends: CustomerSupportTeam, ReasoningService, ...
                                    │
IReasoningTraceStore  (singleton)  ─│→ InMemoryReasoningTraceStore (ring buffer, max 500)
                                    │
InMemorySessionManager(singleton)  ─┤─ ISessionManager + IConversationStore (aynı instance iki
                                    │   farklı interface üzerinden resolve edilir)
                                    │
IContextProvider      (singleton)  ─├─ CustomerContextProvider (Order=10)
                                    ├─ ConversationSummaryProvider (Order=5)
ContextPipeline       (singleton)  ─┘  IEnumerable<IContextProvider> enjekte eder,
                                       Order'a göre sıralı çalıştırır.
```

Kritik nokta: `InMemorySessionManager` **tek bir singleton** olarak oluşturulup iki farklı interface'e (`ISessionManager`, `IConversationStore`) aynı instance üzerinden mapping yapılır. Aksi halde oturum verileri ikiye bölünürdü:

```csharp
@Program.cs:59-61
builder.Services.AddSingleton<InMemorySessionManager>();
builder.Services.AddSingleton<ISessionManager>(sp => sp.GetRequiredService<InMemorySessionManager>());
builder.Services.AddSingleton<IConversationStore>(sp => sp.GetRequiredService<InMemorySessionManager>());
```

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
  │                        │                        │── o4-mini ─▶ OpenAI     │  ← Katman 1
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
  │                        │                        │                         │   PlanningAgent ▶ gpt-4o  ← Katman 2
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

`@Endpoints/ChatEndpoints.cs:25-58` bu akışı **dört adım** olarak kodda yorumlar. **Katman 0 + 1.5** deterministic (LLM'siz) — reasoning service içinde şeffaf.

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

Bkz. `@Models/StreamEvent.cs` — tüm tip sabitleri.

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

1. **`IChatClient`** (default: OpenAI → `gpt-4o`, Azure → `gpt-5.1` deployment, Anthropic → `claude-haiku-4-5`) — tüm ajanlar, ConversationSummaryProvider, RewriteRoutingMessage.
2. **`ReasoningChatClient`** (default: OpenAI → `o4-mini`, Azure → reasoning deployment, Anthropic → reasoning modeli) — **sadece** `ReasoningService` kullanır; OpenAI/Azure'da `reasoning_effort` parametresi gönderilir.

Bu ayrım sayesinde:
- Ön-analiz (niyet tespiti, requiredInfo) **daha uzun iç düşünme** zamanı olan o-series modelde yapılır.
- Esas müşteri yanıtı, araç çağrısı ve ton duyarlı çıktılar **daha hızlı ve daha ucuz** gpt-4o'da üretilir.
- İki modeli bağımsız upgrade/downgrade edebilirsiniz (ayrı config anahtarları).

## Uygulama başlangıç sırası

`Program.cs` incelenirse:

1. **Config okunur** — `AI:Provider` ile sağlayıcı seçilir; ilgili sağlayıcının alt bloğundaki zorunlu alanlar (`ApiKey`, `Endpoint` vb.) eksikse `AiClientFactory` `InvalidOperationException` fırlatır.
2. **İki chat client** `AiClientFactory.CreateStandardChatClient` / `CreateReasoningChatClient` ile oluşturulur.
3. **`PromptService` singleton** constructor'ında `Prompts/**/*.md` dosyalarını belleğe yükler — **bu lazy değildir**; `README.md` yoksa veya dizin yoksa immediately fırlatılır.
4. Domain servisleri, trace store, session manager, context provider'lar kaydedilir.
5. **`CustomerSupportTeam` construct edildiğinde** 6 agent yaratılır ve MAF `AgentWorkflowBuilder` ile workflow derlenir — **bu lazy'dir**, ilk `/chat/` isteğinde tetiklenir çünkü DI singleton'ı ilk resolve anında oluşturulur.
6. Endpoint'ler map edilir (chat, sessions, traces, evaluation).
7. `app.Run()` ile Kestrel dinlemeye başlar.

## Veri yaşam döngüsü (session + trace)

- **Session** (`InMemorySessionManager`): `sessionId` → `AgentSession` (state + history). Reset yok, uygulama restart olunca sıfırlanır.
- **History**: Kullanıcı + asistan mesaj çiftleri `List<ChatMessage>` olarak biriktirilir. `ConversationSummaryProvider` 8+ mesaj olunca eski mesajları LLM ile özetleyip `SessionState.ConversationSummary` alanına yazar — token tasarrufu.
- **Trace** (`InMemoryReasoningTraceStore`): Her workflow koşusu için ayrı bir `ReasoningTrace` üretilir. Ring buffer; en fazla 500 trace tutulur, fazlası en eskiden itibaren atılır.

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

- **Her class/interface ne iş yapar?** → [reference.md](reference.md) — tek-paragraflık rol tanımı + alan/metod listesi
- **HTTP endpoint şemaları + SSE event payload'ları** → [api.md](api.md)
- **Agent davranışı + iç sub-component anatomisi** → [agents.md](agents.md)
- **Workflow akışı + Compound query orkestrasyon** → [workflow.md](workflow.md)
- **Reasoning pipeline katmanları** → [reasoning.md](reasoning.md)
- **Tasarım pattern'leri** → [patterns.md](patterns.md)
