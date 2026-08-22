# CustomerSupportBot.Domain

Bu klasör, Onion / Hexagonal Mimarinin en iç çekirdeğini (**Core Domain**) oluşturan; hiçbir dış kütüphaneye, veritabanına, framework'e (MAF, EF Core, ASP.NET Core) bağımlı olmayan saf C# iş modellerini, alan kurallarını, ayrıştırıcı servisleri (Parsers) ve domain istisnalarını barındırır.

## Dizin Yapısı

### Model/ — Temel domain varlıkları ve veri transfer modelleri

- [AgentSession](Model/AgentSession.md) / [SessionState](Model/AgentSession.md) / [SentimentEntry](Model/SentimentEntry.md) — Oturum durumu, doğrulanmış kimlik, konuşma geçmişi ve tur bazlı duygu kaydı (üçü de aynı `AgentSession.cs` dosyasında).
- [ApprovalRequest](Model/ApprovalRequest.md) — HITL (human-in-the-loop) onay kaydı.
- [CategoryProducts](Model/CategoryProducts.md), [ProductInfo](Model/ProductInfo.md) — Ürün/kategori modelleri.
- [ChatBridgeMessage](Model/ChatBridgeMessage.md), [ChatMode](Model/ChatMode.md), [ChatSessionState](Model/ChatSessionState.md) — Canlı sohbet köprüsü ve mod (bot/insan) modelleri.
- [ComplaintInfo](Model/ComplaintInfo.md) — Şikayet kaydı modeli.
- [ConfidenceLevel](Model/ConfidenceLevel.md), [ConversationPhase](Model/ConversationPhase.md), [ConversationRating](Model/ConversationRating.md), [ConversationMessage](Model/ConversationMessage.md) — Konuşma akışı modelleri.
- [EscalationAction](Model/EscalationAction.md), [EscalationRequest](Model/EscalationRequest.md) — İnsan temsilciye devir modelleri.
- [ExtractedIds](Model/ExtractedIds.md), [VerifiedEntities](Model/VerifiedEntities.md) — Metinden çıkarılan/doğrulanan kimlikler.
- [HumanAgent](Model/HumanAgent.md) — İnsan temsilci modeli.
- [OrderInfo](Model/OrderInfo.md), [OrderLineRequest](Model/OrderLineRequest.md), [OrderPlacementResult](Model/OrderPlacementResult.md), [StockDeductionResult](Model/StockDeductionResult.md) — Sipariş oluşturma zinciri: LLM talebi → stok düşümü → nihai sonuç.
- [PlanningResult](Model/PlanningResult.md), [SubTask](Model/SubTask.md) — Orkestrasyon planı modelleri.
- [ReasoningResult](Model/ReasoningResult.md), [ReasoningIssue](Model/ReasoningIssue.md), [ReasoningStep](Model/ReasoningStep.md), [ReasoningTrace](Model/ReasoningTrace.md), [SpecialistReasoning](Model/SpecialistReasoning.md), [SelfCritique](Model/SelfCritique.md), [TaskCompletionStatus](Model/TaskCompletionStatus.md) — 2 aşamalı niyet/akıl yürütme ve trace ambarı modelleri.
- [SessionAnalytics](Model/SessionAnalytics.md), [SlaEvent](Model/SlaEvent.md), [TurnSignals](Model/TurnSignals.md) — Analitik ve SLA modelleri.
- [ToolResult](Model/ToolResult.md) — Tüm tool'ların standart dönüş zarfı.
- [WellKnown](WellKnown.md) — Sistem genelinde paylaşılan sabitler (intent, ajan adı, tool adı, rol, eşik değerleri).

#### Model/Auth/

- [UserInfo](Model/Auth/UserInfo.md) — Kimliği doğrulanmış kullanıcı (admin/agent/customer) snapshot'ı.
- [RefreshTokenInfo](Model/Auth/RefreshTokenInfo.md) — JWT refresh token kaydı.

#### Model/Memory/

- [CustomerProfile](Model/Memory/CustomerProfile.md), [CustomerUnderstanding](Model/Memory/CustomerUnderstanding.md), [InferredTrait](Model/Memory/InferredTrait.md) — Uzun ömürlü müşteri profili ve LLM-türetilmiş çıkarımlar.
- [MemoryDocument](Model/Memory/MemoryDocument.md) — Qdrant'a yazılan vektör bellek belgesi.
- [ProductRecommendation](Model/Memory/ProductRecommendation.md), [KnowledgeArticle](Model/Memory/KnowledgeArticle.md) — Öneri ve bilgi bankası modelleri.

#### Model/Improvement/

- [Lesson](Model/Improvement/Lesson.md) — Self-improving loop'un ürettiği, admin onaylı "öğrenilmiş ders".

### Services/ — Saf C# domain servisleri ve deterministik ayrıştırıcılar

- [IdExtractor](Services/IdExtractor.md) — Türkçe bağlam kelimeleriyle metinden deterministik olarak `order_id`, `customer_id`, `complaint_id` çıkaran servis.
- [TokenEstimator](Services/TokenEstimator.md) — Metin uzunluğu ve kelime bazlı yaklaşık token hesaplayıcı.
- [ReasoningResultParser](Services/ReasoningResultParser.md), [PlanningResultParser](Services/PlanningResultParser.md), [SpecialistReasoningParser](Services/SpecialistReasoningParser.md), [SelfCritiqueParser](Services/SelfCritiqueParser.md) — LLM çıktısı JSON bloklarını hata toleranslı ayrıştıran servisler.
- [SessionStateExtractor](Services/SessionStateExtractor.md) — Oturum durumu güncelleyici (duygu tespiti dahil).
- [EscalationStates](Services/EscalationStates.md) — Eskalasyon durum makinesi.

### Exceptions/

- [DomainException](Exceptions/DomainException.md) — Temel sınıf.
- [EntityNotFoundException](Exceptions/EntityNotFoundException.md), [PersistenceException](Exceptions/PersistenceException.md), [ExternalServiceException](Exceptions/ExternalServiceException.md), [UnauthorizedSessionAccessException](Exceptions/UnauthorizedSessionAccessException.md), [ConcurrencyConflictException](Exceptions/ConcurrencyConflictException.md) — Türetilmiş, `DomainExceptionHandler` (Api katmanı) tarafından HTTP durum koduna çevrilen istisnalar.

## Mimari Kurallar ve Kısıtlar

1. **Sıfır Dış Bağımlılık:** Domain katmanı yalnızca standart .NET BCL kütüphanelerini (`System.*`, `System.Text.Json`, `System.Text.RegularExpressions`) kullanır.
2. **Deterministik ve Test Edilebilir:** Domain servisleri hiçbir I/O (veritabanı, ağ, LLM) çağrısı yapmaz; aynı girdi için her zaman aynı çıktıyı üretir.
3. **Tek Doğruluk Kaynağı ilkesi:** Magic string'ler (intent, tool adı, ajan adı, hata kodu) `WellKnown` sınıfında toplanır; prompt dosyaları ve appsettings ile senkronu `PromptContractTests` doğrular.
