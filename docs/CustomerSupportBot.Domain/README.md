# CustomerSupportBot.Domain

Bu klasör **Domain katmanı**nın dokümantasyonunu içerir. Domain, projedeki **iş kavramları**nı temsil eder — saf, framework'süz, altyapısız C# sınıflarıdır.

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
docs/CustomerSupportBot.Domain/
├── README.md (bu dosya)
├── WellKnown.md
├── Model/
│   ├── AgentSession.md
│   ├── SessionState.md (+ SentimentEntry)
│   ├── ChatSessionState.md
│   ├── ChatMode.md
│   ├── ChatBridgeMessage.md (+ ChatBridgeSender)
│   ├── ConversationMessage.md (+ ConversationRoles)
│   ├── ConversationPhase.md
│   ├── ConversationRating.md (+ AnalyticsDashboard)
│   ├── ReasoningResult.md
│   ├── ReasoningStep.md
│   ├── ReasoningIssue.md (+ IssueSeverity)
│   ├── ConfidenceLevel.md
│   ├── PlanningResult.md (+ RejectedAlternative)
│   ├── SubTask.md
│   ├── TurnSignals.md
│   ├── SelfCritique.md
│   ├── SpecialistReasoning.md (+ PreToolCheck, PostToolReflection, TaskCompletionStatus)
│   ├── ApprovalRequest.md (+ ApprovalStatus)
│   ├── EscalationRequest.md (+ EscalationStatus, EscalationDecisionInput)
│   ├── EscalationAction.md
│   ├── HumanAgent.md (+ HumanAgentInput, EscalationPriority, RoutingDecision)
│   ├── ToolResult.md (+ ToolError, ToolErrorCategories, ToolSuggestedActions)
│   ├── OrderInfo.md (+ OrderLine, OrderLineRequest, StockDeductionResult)
│   ├── ComplaintInfo.md
│   ├── ProductInfo.md
│   ├── CategoryProducts.md
│   ├── ExtractedIds.md
│   ├── VerifiedEntities.md (+ VerifiedEntity, EntitySource, EntityVerification)
│   ├── SessionAnalytics.md (+ ilgili DTO'lar)
│   └── SlaEvent.md
├── Services/
│   ├── EscalationStates.md
│   ├── IdExtractor.md
│   ├── SessionStateExtractor.md
│   ├── PlanningResultParser.md
│   ├── ReasoningResultParser.md
│   ├── SpecialistReasoningParser.md
│   └── SelfCritiqueParser.md
└── Exceptions/
    └── DomainException.md
```

---

## Domain'in temel kuralları

1. **Bağımlılık yok**: Hiçbir `Microsoft.*` veya 3rd-party paket import edilmez (sadece BCL: `System.*`).
2. **Davranış + veri**: Sadece DTO değildir; state machine'ler ve domain servisleri (parser'lar, extractor'lar) burada yaşar.
3. **Magic string yok**: `WellKnown.cs` tek doğruluk kaynağı (intent, agent name, tool name, status, vb.).
4. **Türkçe iş dili**: Yorum ve fallback mesajları Türkçe; ChatBot kullanıcı odaklı bir ürün.
5. **Immutable tercih edilir**: `record` veya init-only property'ler yaygın.

---

## Üç ana kategori

### 1. Models (30 dosya)

Saf veri tipleri. Sınıflar (mutable state için: `AgentSession`), record'lar (immutable DTO için: `ProductInfo`), enum'lar (`ChatMode`, `ConversationPhase`).

#### Oturum & Konuşma
| Doküman | Kapsam |
|---|---|
| [Model/AgentSession.md](Model/AgentSession.md) | Oturum nesnesi — SessionId + zaman + state |
| [Model/SessionState.md](Model/SessionState.md) | Oturum durumu — intent, phase, sentiment, collectedInfo |
| [Model/ChatSessionState.md](Model/ChatSessionState.md) | HITL mod snapshot'ı (Bot/Human) |
| [Model/ChatMode.md](Model/ChatMode.md) | Bot / Human enum |
| [Model/ChatBridgeMessage.md](Model/ChatBridgeMessage.md) | Canlı sohbet mesaj formatı |
| [Model/ConversationMessage.md](Model/ConversationMessage.md) | LLM konuşma geçmişi formatı |
| [Model/ConversationPhase.md](Model/ConversationPhase.md) | Konuşma fazları enum |

#### Reasoning & Planning
| Doküman | Kapsam |
|---|---|
| [Model/ReasoningResult.md](Model/ReasoningResult.md) | LLM reasoning çıktısı (intent, güven, adımlar) |
| [Model/ReasoningStep.md](Model/ReasoningStep.md) | Tek bir reasoning adımı |
| [Model/ReasoningIssue.md](Model/ReasoningIssue.md) | Sanity check tutarsızlıkları |
| [Model/ConfidenceLevel.md](Model/ConfidenceLevel.md) | Güven seviyesi enum |
| [Model/PlanningResult.md](Model/PlanningResult.md) | PlanningAgent routing kararı |
| [Model/SubTask.md](Model/SubTask.md) | Compound query alt görevleri |
| [Model/TurnSignals.md](Model/TurnSignals.md) | LLM → SessionState sinyal taşıyıcı |
| [Model/SelfCritique.md](Model/SelfCritique.md) | ResponseAgent kalite değerlendirmesi |

#### Specialist Agents
| Doküman | Kapsam |
|---|---|
| [Model/SpecialistReasoning.md](Model/SpecialistReasoning.md) | Pre/post tool reasoning |
| [Model/ToolResult.md](Model/ToolResult.md) | Tool çağrısı standart dönüş zarfı |
| [Model/OrderInfo.md](Model/OrderInfo.md) | Sipariş domain modeli — **çok satırlı** (`OrderLine`), satır talebi (`OrderLineRequest`), stok düşüm sonucu (`StockDeductionResult`) |
| [Model/ComplaintInfo.md](Model/ComplaintInfo.md) | Şikayet domain modeli |
| [Model/ProductInfo.md](Model/ProductInfo.md) | Ürün domain modeli |
| [Model/CategoryProducts.md](Model/CategoryProducts.md) | Kategori sorgu sonucu — "kategori yok" ile "kategori boş"u ayırır |
| [Model/ExtractedIds.md](Model/ExtractedIds.md) | Regex ile çıkarılan ham ID'ler |
| [Model/VerifiedEntities.md](Model/VerifiedEntities.md) | DB ile doğrulanmış entity'ler |

#### HITL & Escalation
| Doküman | Kapsam |
|---|---|
| [Model/ApprovalRequest.md](Model/ApprovalRequest.md) | HITL onay kaydı |
| [Model/EscalationRequest.md](Model/EscalationRequest.md) | Eskalasyon talebi |
| [Model/EscalationAction.md](Model/EscalationAction.md) | Eskalasyon karar aksiyonları enum |
| [Model/HumanAgent.md](Model/HumanAgent.md) | İnsan temsilci profili |

#### Analytics & Observability
| Doküman | Kapsam |
|---|---|
| [Model/ReasoningTrace.md](Model/ReasoningTrace.md) | Workflow trace kaydı |
| [Model/ConversationRating.md](Model/ConversationRating.md) | Müşteri değerlendirmesi |
| [Model/SessionAnalytics.md](Model/SessionAnalytics.md) | Oturum bazlı analytics |
| [Model/SlaEvent.md](Model/SlaEvent.md) | SLA uyarı/ihlal olayı |

### 2. Services (7 dosya)

Domain logic'i — altyapıya bağlı olmayan algoritmalar:

| Doküman | Kapsam |
|---|---|
| [Services/EscalationStates.md](Services/EscalationStates.md) | State Pattern (Open → Acknowledged → Resolved/Dismissed) |
| [Services/IdExtractor.md](Services/IdExtractor.md) | Regex ile Türkçe bağlam kelimesiyle 4+ haneli ID çıkar |
| [Services/SessionStateExtractor.md](Services/SessionStateExtractor.md) | User+Bot mesajından session state türet |
| [Services/PlanningResultParser.md](Services/PlanningResultParser.md) | PlanningAgent JSON → PlanningResult |
| [Services/ReasoningResultParser.md](Services/ReasoningResultParser.md) | Reasoning JSON → ReasoningResult |
| [Services/SpecialistReasoningParser.md](Services/SpecialistReasoningParser.md) | Specialist JSON → SpecialistReasoning |
| [Services/SelfCritiqueParser.md](Services/SelfCritiqueParser.md) | SelfCritique JSON → SelfCritique |

### 3. Exceptions (1 dosya)

| Doküman | Kapsam |
|---|---|
| [Exceptions/DomainException.md](Exceptions/DomainException.md) | Altyapı ve iş kuralı hataları |

### 4. Constants

| Doküman | Kapsam |
|---|---|
| [WellKnown.md](WellKnown.md) | Magic string constant registry |

---

## Hızlı referans: Adapters bu Domain'i nasıl kullanır?

- **Adapters.Persistence**: EF Core entity'leri (`SessionEntity`) ↔ Domain modeli (`AgentSession`) arasında manuel mapping yapar
- **Adapters.AI**: LLM JSON yanıtını `PlanningResultParser`/`ReasoningResultParser` ile Domain modeline çevirir
- **Adapters.Agents**: `ToolResult` factory metodlarını (`ToolResult.Ok()`, `ToolResult.NotFound()`) tool çıktıları için kullanır
- **Application**: Port arayüzleri Domain tiplerini imzalarda kullanır
