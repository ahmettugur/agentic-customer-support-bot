# CustomerSupportBot.Application — Genel Bakış

`CustomerSupportBot.Application` projesi hexagonal mimarinin **çekirdeğidir**. Domain Model dışında hiçbir framework'e (ASP.NET, EF Core, MAF) bağımlı değildir. Tüm iş kuralları, use-case mantığı ve port tanımları buradadır.

## Bu projeyi ne zaman açarsınız?

| Görev | İlgili dosya |
|-------|-------------|
| Kullanıcı mesajının baştan sona nasıl işlendiğini anlamak | [ChatPortService](ChatPortService.md) |
| Reasoning pipeline'ını değiştirmek (intent, entity, güven skoru) | [ReasoningPipeline](ReasoningPipeline.md) |
| Yeni bir AI tool eklemek veya mevcut tool davranışını değiştirmek | [CustomerSupportToolsService](CustomerSupportToolsService.md) |
| Workflow bağlamına yeni bilgi enjekte etmek | [ContextPipeline](ContextPipeline.md) |
| Compound query / paralel görev mantığını değiştirmek | [SubTaskOrchestrator](SubTaskOrchestrator.md) |
| Eskalasyon politikasını düzenlemek | [EscalationPolicyService](EscalationPolicyService.md) |
| Semantik bellek veya knowledge base ile çalışmak | [Memory](Memory.md) |
| Otomatik test / evaluation senaryosu çalıştırmak | [Evaluation](Evaluation.md) |
| DI kayıtlarını anlamak | [DependencyInjection](DependencyInjection.md) |
| Tüm port arayüzlerinin listesi | [Ports](Ports.md) |
| HITL onay kuyruğunu yönetmek | [ApprovalPortService](ApprovalPortService.md) |
| Admin panel oturum yönetimi (TakeOver/Release/Replan) | [ChatSessionPortService](ChatSessionPortService.md) |
| Eskalasyon oluşturma ve event bridge | [EscalationPortService](EscalationPortService.md) |
| HITL SSE event akışları | [HitlEventPortService](HitlEventPortService.md) |
| Human agent kayıt ve yük takibi | [HumanAgentPortService](HumanAgentPortService.md) |
| Session CRUD API işlemleri | [SessionPortService](SessionPortService.md) |
| Trace sorgulama ve istatistik | [TracePortService](TracePortService.md) |
| Analitik ve dashboard | [AnalyticsPortService](AnalyticsPortService.md) |
| Bot'u yeniden planlama (admin tetikli) | [ReplanService](ReplanService.md) |
| Self-improvement / lesson mining | [ImprovementsPortService](ImprovementsPortService.md) + [LessonMiner](LessonMiner.md) |
| Semantik bellek admin API | [MemoryPortService](MemoryPortService.md) |
| Müşteri profil kişiselleştirmesi | [PersonalizationPortService](PersonalizationPortService.md) + [CustomerProfileService](CustomerProfileService.md) |
| SLA izleme ve ihlal aksiyonları | [SlaGuardian](SlaGuardian.md) |
| LLM maliyet takibi | [TelemetryPortService](TelemetryPortService.md) |
| Low-code workflow CRUD ve test | [WorkflowPortService](WorkflowPortService.md) + [WorkflowExecutor](WorkflowExecutor.md) |
| Eskalasyon için skill-based agent yönlendirmesi | [SkillsBasedRouter](SkillsBasedRouter.md) |
| JWT + refresh token yönetimi | [Auth](Auth.md) |
| Gelen mesaj güvenlik filtresi | [InputGuard](InputGuard.md) |
| AsyncLocal HITL context taşıyıcı | [ApprovalContextAccessor](ApprovalContextAccessor.md) |
| Sesli konuşma (Bridge ve Native mod) | [RealtimeServices](RealtimeServices.md) |

## Klasör yapısı

```
CustomerSupportBot.Application/
│
├── Ports/
│   ├── Driving/          # Gelen porlar — API katmanının çağırdığı arayüzler
│   │   ├── IChatPort.cs
│   │   ├── IReasoningPort.cs
│   │   ├── IApprovalPort.cs
│   │   ├── ISessionPort.cs
│   │   ├── ITracePort.cs
│   │   ├── IEvaluationPort.cs
│   │   └── ... (12 arayüz)
│   │
│   └── Driven/           # Giden porlar — Adapter'ların implement ettiği arayüzler
│       ├── IAgentTeamPort.cs
│       ├── IContextPipeline.cs
│       ├── IPromptRepository.cs
│       ├── Persistence/   → ISessionManager, IOrderRepository, IApprovalQueue ...
│       ├── AI/            → IReasoningChatClient, IEmbeddingPort, IVectorMemoryPort ...
│       ├── Observability/ → IReasoningTraceStore, ICostUsageStorePort, ILlmCallPersistencePort ...
│       ├── Auth/          → IJwtAccessTokenProvider, IPasswordHasher ...
│       ├── Messaging/     → IMessageBusPort
│       └── Locking/       → IAppDistributedLock
│
└── Services/
    ├── ChatPortService.cs          # IChatPort — Ana use-case orkestrasyonu
    ├── ReasoningService.cs         # IReasoningPort — LLM reasoning pipeline
    ├── ReasoningMessageBuilder.cs  # Reasoning mesaj listesi inşaatı
    ├── ReasoningSanityChecker.cs   # Deterministic doğruluk kontrolü
    ├── EntityVerifier.cs           # DB'ye dayalı entity doğrulama
    ├── ContextPipeline.cs          # IContextPipeline — paralel context sağlayıcı zinciri
    ├── CustomerSupportToolsService.cs # Tool implementasyonları + idempotency
    ├── SubTaskOrchestrator.cs      # Compound query decomposition
    ├── SessionStateService.cs      # Sentiment + intent session yönetimi
    ├── ApprovalContextAccessor.cs  # AsyncLocal HITL context
    ├── EscalationPolicyService.cs  # Eskalasyon politikası + routing
    ├── InputGuard.cs               # Güvenlik kapısı (6 kural, LLM'den önce çalışır)
    │
    ├── Providers/                  # IContextProvider implementasyonları
    │   ├── ConversationSummaryProvider.cs
    │   ├── CustomerContextProvider.cs
    │   ├── CustomerProfileContextProvider.cs
    │   └── SemanticMemoryContextProvider.cs
    │
    ├── Memory/                     # Semantik bellek servisleri
    │   ├── SemanticMemoryService.cs
    │   └── KnowledgeBaseIngestionService.cs
    │
    ├── Personalization/            # Müşteri profil servisi
    │   └── CustomerProfileService.cs
    │
    ├── Evaluation/                 # Otomatik senaryo değerlendirici
    │   ├── EvaluationRunner.cs
    │   └── CriteriaEvaluator.cs
    │
    ├── Routing/                    # Skills-based routing
    │   └── SkillsBasedRouter.cs
    │
    ├── Sla/                        # SLA politika değerlendiricisi
    │   └── SlaPolicyEvaluator.cs
    │
    └── Improvement/                # Ders madenciliği (LessonMiner)
        └── LessonMiner.cs
```

## Bir isteğin uçtan uca akışı

```
[Kullanıcı]
    │ HTTP POST /chat/stream
    ▼
[ChatEndpoints]  ← Api katmanı
    │ IInputGuard.Inspect(query) — 6 güvenlik kontrolü
    │    → Reject ise 400 döner
    ▼
[ChatPortService.HandleStreamAsync]
    │
    ├─► [ReasoningService.ReasonStreamingAsync]
    │       ├─ EntityVerifier.Verify()       → DB'de entity doğrula
    │       ├─ ReasoningMessageBuilder.Build() → Prompt mesaj listesi kur
    │       ├─ IReasoningChatClient.StreamAsync() → LLM çağrısı (o-series)
    │       ├─ ReasoningResultParser.Parse()  → JSON sonuç ayrıştır
    │       └─ ReasoningSanityChecker.Check() → 8 deterministik kural kontrol
    │
    │    StreamEvent: ReasoningStart → ReasoningDelta* → ReasoningComplete
    │
    ├─► [IAgentTeamPort.RunStreamingAsync]   ← CustomerSupportTeam (Adapters.Agents)
    │       ├─ ContextPipeline.BuildContextAsync()  → 4 provider paralel
    │       ├─ Workflow: PlanningAgent → SpecialistAgent → ResponseAgent
    │       └─ WorkflowResponseExtractor: temizle + streaming
    │
    │    StreamEvent: Agent* → ResponseStart → ResponseDelta* → ResponseComplete
    │
    └─► SessionStateService: sentiment + intent güncelle + persist
         StreamEvent: SentimentUpdate [+ SentimentAlert?]
```

## Mimari prensipler

- **Hiçbir framework import'u yoktur** — `System.*` ve kendi port/domain tipleri dışında bağımlılık yok
- **Portlar arayüzdür, implementasyon adapter'dadır** — `IOrderRepository` arayüzü burada, Postgres/InMemory impl Persistence'da
- **Servisler Singleton'dır** — state yok; her istek için yeni nesne yaratılmaz
- **Opsiyonel bağımlılıklar nullable gelir** — `ISemanticMemoryWriter?`, `ICustomerProfileService?` gibi; null ise özellik sessizce atlanır
