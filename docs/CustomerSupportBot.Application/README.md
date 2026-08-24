# CustomerSupportBot.Application

Bu klasör, Onion / Hexagonal Mimari'de **Use Cases & Application Core (İş Akışları ve Uygulama Çekirdeği)** katmanıdır. Dış sürücü adaptörlerin (Driving Adapters: API, Web) çağırdığı **Inbound Port** sözleşmelerini ve bu sözleşmeleri yerine getiren domain servislerini barındırır. Aynı zamanda dış altyapıların (Driven Adapters: DB, LLM, Redis, Telemetry) uygulayacağı **Outbound Port** arayüzlerini tanımlar.

## Dizin Yapısı

- [Ports/](Ports.md) — Hexagonal Port Arayüzleri:
  - `Inbound/` — Giriş Portları ([IChatOrchestratorPort](Ports.md#inbound-portlar), `IReasoningPort`, `IAgentTeamPort`, `IEvaluationPort`, `IApprovalPort`, `IAuthPort`, `ITelemetryPort`, `ITracePort`, `IMemoryPort`, `IPersonalizationPort`, `ISlaPort`, `IRealtimeSessionPort`, `StreamEvent`).
  - `Outbound/` — Çıkış Portları (`IReasoningChatClient`, `IGeneralChatClient`, `IEmbeddingPort`, `IVectorMemoryPort`, `IApprovalQueue`, `ICustomerRepository`, `IOrderRepository`, `IProductCatalogRepository`, `IComplaintRepository`, `IAppDistributedLock`, `IMessageBusPort`, `IPromptRepository`, `ICustomerSupportToolsService`, `IApprovalContextAccessor`).
- [Reasoning/](Services/Reasoning/ReasoningService.md) — 2 Aşamalı ReAct Muhakeme Hattı:
  - [ReasoningService](Services/Reasoning/ReasoningService.md) — [IReasoningPort](Ports.md) uygulayıcısı; niyet tespiti, varlık doğrulama ve alt görev bölme motoru.
  - [EntityVerifier](Services/Reasoning/EntityVerifier.md) — Query/history ID'lerini çözümler,
    authenticated customer kimliğini korur; factual doğrulamayı sahiplik kontrollü tool'lara bırakır.
  - [ReasoningSanityChecker](Services/Reasoning/ReasoningSanityChecker.md) — Model çıktısını mantıksal kurallarla denetleyen güvenlik katmanı.
  - [ReasoningMessageBuilder](Services/Reasoning/ReasoningMessageBuilder.md) — Muhakeme istemi oluşturucu.
  - [ReplanService](Services/Reasoning/ReplanService.md) — Yönetici müdahaleli zorunlu yeniden planlama servisi.
  - [SubTaskOrchestrator](Services/Reasoning/SubTaskOrchestrator.md) — Compound sorguları alt görevlere bölen orkestratör.
- [Chat/](Services/Chat/ChatPortService.md) — Sohbet Hattı ve Oturum Yönetimi:
  - [ChatPortService](Services/Chat/ChatPortService.md) — [IChatOrchestratorPort](Ports.md) uygulayıcısı; uçtan uca senkron ve SSE canlı akış orkestrasyonu.
  - [ContextPipeline](Services/Chat/ContextPipeline.md) — RAG, müşteri profili, ürün önerisi ve konuşma özeti bağlamlarını birleştiren boru hattı.
  - [InputGuard](Services/Chat/InputGuard.md) — Prompt Injection ve aşırı uzunluk denetimi.
  - [ChatSessionPortService](Services/Chat/ChatSessionPortService.md) — Canlı devralma (human takeover) ve yeniden planlama orkestrasyonu.
  - [SessionPortService](Services/Chat/SessionPortService.md) — Genel amaçlı oturum CRUD'unu `ISessionPort`'a bağlayan servis.
  - [SessionStateService](Services/Chat/SessionStateService.md) & [SessionIdentityBinder](Services/Chat/SessionIdentityBinder.md) — Oturum durumu ve müşteri kimliği bağlayıcısı.
- [Auth/](Services/Auth/TokenPortService.md) — Kimlik Doğrulama ve JWT/Refresh Token Yaşam Döngüsü:
  - [TokenPortService](Services/Auth/TokenPortService.md) — JWT+refresh token üretimi/rotasyonu (koşullu atomik iptal ile).
  - [UserService](Services/Auth/UserService.md) — Staff (Admin/Agent) login.
  - [CustomerAuthService](Services/Auth/CustomerAuthService.md) — Müşteri self-servis kayıt/login (kimlik sahipliği kontrolü dahil).
- [Tools/](Services/Tools/CustomerSupportToolsService.md) — Uzman Ajanların Kullandığı Domain Araçları:
  - [CustomerSupportToolsService](Services/Tools/CustomerSupportToolsService.md) — Tüm araçların merkezi cephesi (`ICustomerSupportToolsService`).
  - [OrderToolsService](Services/Tools/OrderToolsService.md) — Sipariş durumu, oluşturma, iptal araçları.
  - [ProductToolsService](Services/Tools/ProductToolsService.md) — Ürün sorgulama ve listeleme araçları.
  - [ComplaintToolsService](Services/Tools/ComplaintToolsService.md) — Şikayet durumu ve kayıt araçları.
  - [SideEffectIdempotencyCache](Services/Tools/SideEffectIdempotencyCache.md) — Mükerrer sipariş ve şikayet kaydını önleyen idempotency önbelleği.
- [Approval/](Services/Approval/ApprovalPortService.md) — HITL (Human-in-the-Loop) Onay Sistemi:
  - [ApprovalPortService](Services/Approval/ApprovalPortService.md) — [IApprovalPort](Ports.md) uygulayıcısı.
  - [ApprovalExecutionRouter](Services/Approval/ApprovalExecutionRouter.md) — Onaylanan talepleri (sipariş/şikayet/iptal/iade) otomatik işleten yönlendirici (bloklamayan onay modelinin yürütme ayağı).
  - [ApprovalContextAccessor](Services/Approval/ApprovalContextAccessor.md) — Ambient `CurrentCustomerId` ve `TraceId` güvenliği (`AsyncLocal` tabanlı).
- [Providers/](Services/Providers/IContextProvider.md) — Dinamik Bağlam Sağlayıcılar (`IContextProvider` sözleşmesi + `ConversationSummaryProvider`, `CustomerIdentityHintBuilder`, `SemanticMemoryContextProvider`, `ProductRecommendationContextProvider`, `CustomerProfileContextProvider`, `NoopContextProvider`).
- [Memory/](Services/Memory/SemanticMemoryService.md) — RAG ve Anlamsal Bellek Servisleri:
  - [SemanticMemoryService](Services/Memory/SemanticMemoryService.md) — Üst seviye memory facade'ı (embed + upsert + arama, üç koleksiyon).
  - [KnowledgeArticleService](Services/Memory/KnowledgeArticleService.md) — Panelden yönetilen KB makalelerinin CRUD + anlık indeksleme.
  - [KnowledgeBaseIngestionService](Services/Memory/KnowledgeBaseIngestionService.md) — Dosya + makale kaynaklarının toplu/periyodik yeniden indekslenmesi (değişiklik tespiti, chunk'lama, stale temizlik).
  - [ContextSanitizer](Services/Memory/ContextSanitizer.md) — Retrieval içeriğini prompt-injection'a karşı sertleştirir.
  - [ISemanticMemoryIngestor](Services/Memory/ISemanticMemoryIngestor.md) / [DisabledSemanticMemoryIngestor](Services/Memory/DisabledSemanticMemoryIngestor.md) — Vektör yazım sözleşmesi ve bellek-kapalı Null Object'i.
  - [IKnowledgeBaseIngestor](Services/Memory/IKnowledgeBaseIngestor.md) — KB ingest use case arayüzü.
  - [MemoryPortService](Services/Memory/MemoryPortService.md) — [IMemoryPort](Ports.md) uygulayıcısı (+ `DisabledMemoryPort` Null Object).
- [Personalization/](Services/Personalization/PersonalizationPortService.md) — Müşteri Profili ve Öneri Servisleri (`CustomerProfileService`, `CustomerUnderstandingService`, `RecommendationService`).
- [Realtime/](Services/Realtime/RealtimeNativeService.md) — Sesli Görüşme Servisleri (`RealtimeNativeService`, `RealtimeBridgeService`).
- [Escalation/](Services/Escalation/EscalationPortService.md) — Canlı Temsilci Eskalasyon Politikaları:
  - [EscalationPolicyService](Services/Escalation/EscalationPolicyService.md) — Eskalasyon adaylığı, dedup, skills-based routing kararı.
  - [EscalationPortService](Services/Escalation/EscalationPortService.md) — [IEscalationPort](Ports.md) uygulayıcısı (admin panel CRUD).
  - [HitlEventPortService](Services/Escalation/HitlEventPortService.md) — Oturuma özel HITL olaylarının (onay/eskalasyon/insan devri) canlı yayını.
  - [HumanAgentPortService](Services/Escalation/HumanAgentPortService.md) — Temsilci CRUD'u, otomatik yük takibi, manuel yeniden atama.
- [A2A/](Services/A2A/A2ATokenExchangeService.md) — Agent-to-Agent Protokolü ve Güvenlik (`A2ATokenExchangeService`, `A2ASubjectIdentity`, `ConfiguredA2ASubjectAuthorizer`).
- [Sla/](Services/Sla/SlaPortService.md) — SLA Politikası ve Performans Değerlendirmesi (`SlaPolicyEvaluator`).
- [Improvement/](Services/Improvement/ImprovementsPortService.md) — Kendi Kendini İyileştirme ve Ders Çıkarma:
  - [LessonMiner](Services/Improvement/LessonMiner.md) — Düşük puanlı/hatalı trace'lerden LLM ile "ders" önerisi üretir.
  - [ImprovementsPortService](Services/Improvement/ImprovementsPortService.md) — [IImprovementsPort](Ports.md) uygulayıcısı.
- [Telemetry/](Services/Telemetry/TelemetryPortService.md) — İzleme, Trace ve Analitik Port Servisleri.
- [Routing/](Services/Routing/SkillsBasedRouter.md) — Yetenek Bazlı Ajan Yönlendiricisi.
- [UiHint/](Services/UiHint/UiHintEmitter.md) — İstemciye (Blazor) UI kart ipuçları yayınlayan servis.

## Mimari Rolü ve Yetenekleri

- **Bağımsızlık:** UI framework'lerinden (Blazor, ASP.NET Core) veya veritabanı altyapısından (Npgsql, Redis) tamamen bağımsızdır.
- **2 Aşamalı ReAct Muhakemesi:** MAF iş akışı başlamadan önce `ReasoningService` ile niyet
  tespiti ve güvenli entity resolution (`EntityVerifier`) yapılır; factual doğrulama specialist
  tool'da kalır ve MAF bu bağlamla başlatılır.
- **Tam Idempotency ve Ambient Güvenlik:** Sipariş ve şikayetlerde `SideEffectIdempotencyCache` ile mükerrer işlem engellenir; `CurrentCustomerId` değeri LLM parametresinden değil, doğrulanmış ambient context'ten alınır.
