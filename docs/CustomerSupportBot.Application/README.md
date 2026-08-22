# CustomerSupportBot.Application

Bu klasör, Onion / Hexagonal Mimari'de **Use Cases & Application Core (İş Akışları ve Uygulama Çekirdeği)** katmanıdır. Dış sürücü adaptörlerin (Driving Adapters: API, Web) çağırdığı **Inbound Port** sözleşmelerini ve bu sözleşmeleri yerine getiren domain servislerini barındırır. Aynı zamanda dış altyapıların (Driven Adapters: DB, LLM, Redis, Telemetry) uygulayacağı **Outbound Port** arayüzlerini tanımlar.

## Dizin Yapısı

- [Ports/](Ports.md) — Hexagonal Port Arayüzleri:
  - `Inbound/` — Giriş Portları ([IChatOrchestratorPort](Ports.md#inbound-portlar), `IReasoningPort`, `IAgentTeamPort`, `IEvaluationPort`, `IApprovalPort`, `IAuthPort`, `ITelemetryPort`, `ITracePort`, `IMemoryPort`, `IPersonalizationPort`, `ISlaPort`, `IRealtimeSessionPort`, `StreamEvent`).
  - `Outbound/` — Çıkış Portları (`IReasoningChatClient`, `IGeneralChatClient`, `IEmbeddingPort`, `IVectorMemoryPort`, `IApprovalQueue`, `ICustomerRepository`, `IOrderRepository`, `IProductCatalogRepository`, `IComplaintRepository`, `IAppDistributedLock`, `IMessageBusPort`, `IPromptRepository`, `ICustomerSupportToolsService`, `IApprovalContextAccessor`).
- [Reasoning/](Services/Reasoning/ReasoningService.md) — 2 Aşamalı ReAct Muhakeme Hattı:
  - [ReasoningService](Services/Reasoning/ReasoningService.md) — [IReasoningPort](Ports.md) uygulayıcısı; niyet tespiti, varlık doğrulama ve alt görev bölme motoru.
  - [EntityVerifier](Services/Reasoning/EntityVerifier.md) — Kullanıcı mesajındaki ve oturumdaki `order_id`, `customer_id` varlıklarını DB üzerinden doğrulayan servis.
  - [ReasoningSanityChecker](Services/Reasoning/ReasoningSanityChecker.md) — Model çıktısını mantıksal kurallarla denetleyen güvenlik katmanı.
  - [ReasoningMessageBuilder](Services/Reasoning/ReasoningMessageBuilder.md) — Muhakeme istemi oluşturucu.
  - [ReplanService](Services/Reasoning/ReplanService.md) — Yönetici müdahaleli zorunlu yeniden planlama servisi.
  - [SubTaskOrchestrator](Services/Reasoning/SubTaskOrchestrator.md) — Compound sorguları alt görevlere bölen orkestratör.
- [Chat/](Services/Chat/ChatPortService.md) — Sohbet Hattı ve Oturum Yönetimi:
  - [ChatPortService](Services/Chat/ChatPortService.md) — [IChatOrchestratorPort](Ports.md) uygulayıcısı; uçtan uca senkron ve SSE canlı akış orkestrasyonu.
  - [ContextPipeline](Services/Chat/ContextPipeline.md) — RAG, müşteri profili, ürün önerisi ve konuşma özeti bağlamlarını birleştiren boru hattı.
  - [InputGuard](Services/Chat/InputGuard.md) — Prompt Injection ve aşırı uzunluk denetimi.
  - [SessionStateService](Services/Chat/SessionStateService.md) & [SessionIdentityBinder](Services/Chat/SessionIdentityBinder.md) — Oturum durumu ve müşteri kimliği bağlayıcısı.
- [Tools/](Services/Tools/CustomerSupportToolsService.md) — Uzman Ajanların Kullandığı Domain Araçları:
  - [CustomerSupportToolsService](Services/Tools/CustomerSupportToolsService.md) — Tüm araçların merkezi cephesi (`ICustomerSupportToolsService`).
  - [OrderToolsService](Services/Tools/OrderToolsService.md) — Sipariş durumu, oluşturma, iptal araçları.
  - [ProductToolsService](Services/Tools/ProductToolsService.md) — Ürün sorgulama ve listeleme araçları.
  - [ComplaintToolsService](Services/Tools/ComplaintToolsService.md) — Şikayet durumu ve kayıt araçları.
  - [SideEffectIdempotencyCache](Services/Tools/SideEffectIdempotencyCache.md) — Mükerrer sipariş ve şikayet kaydını önleyen idempotency önbelleği.
- [Approval/](Services/Approval/ApprovalPortService.md) — HITL (Human-in-the-Loop) Onay Sistemi:
  - [ApprovalPortService](Services/Approval/ApprovalPortService.md) — [IApprovalPort](Ports.md) uygulayıcısı.
  - [ApprovalExecutionRouter](Services/Approval/ApprovalExecutionRouter.md) — Onaylanan talepleri (sipariş/şikayet) otomatik işleten yönlendirici.
  - [ApprovalContextAccessor](Services/Approval/ApprovalContextAccessor.md) — Ambient `CurrentCustomerId` ve `TraceId` güvenliği.
- [Providers/](Services/Providers/IContextProvider.md) — Dinamik Bağlam Sağlayıcılar (`IContextProvider` sözleşmesi + `ConversationSummaryProvider`, `CustomerIdentityHintBuilder`, `SemanticMemoryContextProvider`, `ProductRecommendationContextProvider`, `CustomerProfileContextProvider`, `NoopContextProvider`).
- [Memory/](Services/Memory/SemanticMemoryService.md) — RAG ve Anlamsal Bellek Servisleri (`SemanticMemoryService`, `KnowledgeArticleService`, `KnowledgeBaseIngestionService`, `ContextSanitizer`).
- [Personalization/](Services/Personalization/PersonalizationPortService.md) — Müşteri Profili ve Öneri Servisleri (`CustomerProfileService`, `CustomerUnderstandingService`, `RecommendationService`).
- [Realtime/](Services/Realtime/RealtimeNativeService.md) — Sesli Görüşme Servisleri (`RealtimeNativeService`, `RealtimeBridgeService`).
- [Escalation/](Services/Escalation/EscalationPortService.md) — Canlı Temsilci Eskalasyon Politikaları (`EscalationPolicyService`, `HumanAgentPortService`).
- [A2A/](Services/A2A/A2ATokenExchangeService.md) — Agent-to-Agent Protokolü ve Güvenlik (`A2ATokenExchangeService`, `ConfiguredA2ASubjectAuthorizer`).
- [Sla/](Services/Sla/SlaPortService.md) — SLA Politikası ve Performans Değerlendirmesi (`SlaPolicyEvaluator`).
- [Improvement/](Services/Improvement/ImprovementsPortService.md) — Kendi Kendini İyileştirme ve Ders Çıkarma (`LessonMiner`).
- [Telemetry/](Services/Telemetry/TelemetryPortService.md) — İzleme, Trace ve Analitik Port Servisleri.
- [Routing/](Services/Routing/SkillsBasedRouter.md) — Yetenek Bazlı Ajan Yönlendiricisi.
- [UiHint/](Services/UiHint/UiHintEmitter.md) — İstemciye (Blazor) UI kart ipuçları yayınlayan servis.

## Mimari Rolü ve Yetenekleri

- **Bağımsızlık:** UI framework'lerinden (Blazor, ASP.NET Core) veya veritabanı altyapısından (Npgsql, Redis) tamamen bağımsızdır.
- **2 Aşamalı ReAct Muhakemesi:** MAF iş akışı başlamadan önce `ReasoningService` ile niyet tespiti, varlık doğrulaması (`EntityVerifier`) ve akıl yürütme yapılır; MAF bu bağlamla başlatılır.
- **Tam Idempotency ve Ambient Güvenlik:** Sipariş ve şikayetlerde `SideEffectIdempotencyCache` ile mükerrer işlem engellenir; `CurrentCustomerId` değeri LLM parametresinden değil, doğrulanmış ambient context'ten alınır.
