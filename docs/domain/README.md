# CustomerSupportBot.Domain

Bu klasör **Domain katmanı**nın dokümantasyonunu içerir. Domain, projedeki **iş kavramları**nı temsil eder — saf, framework'siz, altyapısız C# sınıflarıdır.

---

## Neden Domain ayrı bir katman?

Hexagonal mimaride Domain **bağımlılık piramidinin tepesi**dir. Hiçbir altyapıya (EF Core, ASP.NET, AI SDK, Redis) bağımlı değildir; tam tersine, **diğer katmanlar Domain'e bağımlıdır**.

```
Adapters (Persistence, AI, Agents)  ──┐
Application (PortServices)           ──┼──→ Domain (saf iş kavramları)
Api (Controllers)                    ──┘
```

**Sonuç:** Domain'i bozmadan EF Core'u kaldırabilir, AI sağlayıcısını değiştirebilir, web framework'ünü değiştirebilirsin.

---

## Klasör yapısı

```
CustomerSupportBot.Domain/
├── Exceptions/
│   └── DomainException.cs
├── Model/
│   ├── Auth/          (UserInfo, RefreshTokenInfo)
│   ├── Improvement/   (Lesson)
│   ├── Memory/        (CustomerProfile, MemoryDocument)
│   ├── Workflow/      (WorkflowDefinition)
│   └── *.cs           (34 model dosyası)
└── Services/
    ├── EscalationStates.cs
    ├── IdExtractor.cs
    ├── PlanningResultParser.cs
    ├── ReasoningResultParser.cs
    ├── SessionStateExtractor.cs
    └── SpecialistReasoningParser.cs
```

---

## Dokümantasyon haritası

| Doküman | Kapsam |
|---|---|
| [Exceptions.md](Exceptions.md) | DomainException ve alt sınıfları |
| [Services-EscalationStates.md](Services-EscalationStates.md) | Escalation yaşam döngüsü State Pattern |
| [Services-IdExtractor.md](Services-IdExtractor.md) | Regex tabanlı ID çıkarımı (LLM'siz) |
| [Services-Parsers.md](Services-Parsers.md) | PlanningResultParser, ReasoningResultParser, SpecialistReasoningParser |
| [Services-SessionStateExtractor.md](Services-SessionStateExtractor.md) | Session state türetimi |
| [Model-Session.md](Model-Session.md) | AgentSession, ChatSessionState, ChatMode, ChatBridgeMessage, ConversationMessage |
| [Model-Auth.md](Model-Auth.md) | UserInfo, RefreshTokenInfo |
| [Model-Reasoning.md](Model-Reasoning.md) | ReasoningResult, ReasoningStep, ReasoningIssue, ConfidenceLevel, PlanningResult, SubTask |
| [Model-Specialist.md](Model-Specialist.md) | SpecialistReasoning, PreToolCheck, PostToolReflection, TaskCompletionStatus |
| [Model-Hitl.md](Model-Hitl.md) | ApprovalRequest, EscalationRequest, HumanAgent, RoutingDecision |
| [Model-Tools.md](Model-Tools.md) | ToolResult, OrderInfo, ProductInfo, ComplaintInfo, ExtractedIds, VerifiedEntities |
| [Model-Memory.md](Model-Memory.md) | CustomerProfile, MemoryDocument, Lesson |
| [Model-Workflow.md](Model-Workflow.md) | WorkflowDefinition + Step + Execution |
| [Model-Trace.md](Model-Trace.md) | ReasoningTrace, AgentVisit, ToolInvocation, SessionAnalytics, ConversationRating, SlaEvent |
| [WellKnown.md](WellKnown.md) | Magic string constant registry |

---

## Domain'in temel kuralları

1. **Bağımlılık yok**: Hiçbir `Microsoft.*` veya 3rd-party paket import edilmez (sadece BCL: `System.*`).
2. **Davranış + veri**: Sadece DTO değildir; state machine'ler ve domain servisleri (parser'lar, extractor'lar) burada yaşar.
3. **Magic string yok**: `WellKnown.cs` tek doğruluk kaynağı (intent, agent name, tool name, status, vb.).
4. **Türkçe iş dili**: Yorum ve fallback mesajları Türkçe; ChatBot kullanıcı odaklı bir ürün.
5. **Immutable tercih edilir**: `record` veya init-only property'ler yaygın.

---

## Üç ana kategori

### 1. Models (34 dosya)
Saf veri tipleri. Sınıflar (mutable state için: `AgentSession`), record'lar (immutable DTO için: `OrderInfo`), enum'lar (`ChatMode`, `ConversationPhase`).

### 2. Services (6 dosya)
Domain logic'i — altyapıya bağlı olmayan algoritmalar:
- **EscalationStates**: State Pattern (Open → Acknowledged → Resolved/Dismissed)
- **IdExtractor**: Regex ile ORD-N, CMP-N, CUST-N çıkar
- **3 Parser**: LLM JSON çıktısını domain modeline çevir
- **SessionStateExtractor**: User+Bot mesajından session state türet (intent, phase, sentiment)

### 3. Exceptions (1 dosya)
`DomainException` hiyerarşisi — altyapı hataları (`PersistenceException`, `ExternalServiceException`) ve iş kuralı hataları (`EntityNotFoundException`, `ConcurrencyConflictException`).

---

## Hızlı referans: Adapters bu Domain'i nasıl kullanır?

- **Adapters.Persistence**: EF Core entity'leri (`SessionEntity`) ↔ Domain modeli (`AgentSession`) arasında manuel mapping yapar
- **Adapters.AI**: LLM JSON yanıtını `PlanningResultParser`/`ReasoningResultParser` ile Domain modeline çevirir
- **Adapters.Agents**: `ToolResult` factory metodlarını (`ToolResult.Ok()`, `ToolResult.NotFound()`) tool çıktıları için kullanır
- **Application**: Port arayüzleri Domain tiplerini imzalarda kullanır
