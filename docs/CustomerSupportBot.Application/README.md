# CustomerSupportBot.Application

Bu klasör **Application katmanı**nın dokümantasyonunu içerir. Application, Domain ile Adapters arasında **port servisleri** barındırır — iş akışlarını orkestre eder.

---

## Hexagonal mimarideki yeri

```
Web/Api ──→ Application (Port Services) ──→ Domain
                  │                            ↑
                  └── Ports (interfaces) ←── Adapters
```

Application **dışarıya port arayüzleri** tanımlar. Adapters bunları implement eder.

---

## Klasör yapısı

```
docs/CustomerSupportBot.Application/
├── README.md (bu dosya)
├── Ports.md (Inbound/Outbound port arayüzleri)
├── DependencyInjection.md
├── Chat/
│   ├── ChatPortService.md (ana orkestratör)
│   ├── SessionPortService.md
│   ├── ChatSessionPortService.md
│   ├── InputGuard.md
│   ├── ContextPipeline.md
│   └── SessionStateService.md
├── Reasoning/
│   ├── ReasoningService.md (pipeline orkestratör)
│   ├── EntityVerifier.md (L0 entity doğrulama)
│   ├── ReasoningMessageBuilder.md (L1 prompt hazırlığı)
│   ├── ReasoningSanityChecker.md (L3 sanity kuralları)
│   ├── SubTaskOrchestrator.md
│   └── ReplanService.md
├── Tools/
│   ├── OrderToolsService.md
│   ├── ProductToolsService.md
│   ├── ComplaintToolsService.md
│   └── SideEffectIdempotencyCache.md
├── Approval/
│   ├── ApprovalPortService.md
│   ├── ApprovalExecutionRouter.md
│   └── ApprovalContextAccessor.md
├── Escalation/
│   ├── EscalationPortService.md
│   ├── EscalationPolicyService.md
│   ├── HitlEventPortService.md
│   └── HumanAgentPortService.md
├── Routing/
│   └── SkillsBasedRouter.md
├── Memory/
│   ├── MemoryPortService.md
│   ├── SemanticMemoryService.md
│   ├── KnowledgeArticleService.md
│   ├── KnowledgeBaseIngestionService.md
│   └── ContextSanitizer.md
├── Providers/
│   └── ContextProviders.md (6 provider)
├── Realtime/
│   ├── RealtimeBridgeService.md
│   └── RealtimeNativeService.md
├── Auth/
│   ├── CustomerAuthService.md
│   ├── TokenPortService.md
│   └── UserService.md
├── Telemetry/
│   ├── AnalyticsPortService.md
│   ├── TracePortService.md
│   └── TelemetryPortService.md
├── Sla/
│   ├── SlaPortService.md
│   └── SlaPolicyEvaluator.md
├── Improvement/
│   ├── ImprovementsPortService.md
│   └── LessonMiner.md
├── Personalization/
│   ├── PersonalizationPortService.md
│   └── CustomerProfileService.md
└── UiHintEmitter.md
```

---

## Ana akış: Bir mesajın yolculuğu

```
Kullanıcı mesajı
    ↓
[ChatPortService] ── InputGuard → ContextPipeline → ReasoningService
    ↓                                                      ↓
[WorkflowRunner]                              EntityVerifier → MessageBuilder
    ↓                                              → LLM → SanityChecker
PlanningAgent → SpecialistAgent → ResponseAgent
    ↓               ↓
ToolsService    ApprovalGate (HITL)
    ↓
[Yanıt → SignalR → Kullanıcı]
```
