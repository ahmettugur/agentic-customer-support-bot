# Ports/Outbound

Application katmanının dış dünyaya (veritabanı, önbellek, LLM sağlayıcıları, dosya sistemi,
WebSocket) çıkmak için tanımladığı **secondary / driven port**'ların tamamı. Her arayüzü kim
implement ediyorsa (bkz. her dosyadaki "İmplementasyon" satırı) o adaptör `Adapters.*`
katmanlarından birinde yaşar — Application katmanının kendisi hiçbir somut altyapı
kütüphanesine (Npgsql, StackExchange.Redis, Qdrant.Client, OpenAI SDK vb.) doğrudan bağımlı
değildir. Bu, hexagonal (ports & adapters) mimarinin temel taşıdır: Dependency Inversion
Principle (DIP) — iş mantığı somut teknolojiye değil, kendi tanımladığı soyutlamaya bağımlıdır.

## A2A/

- [IA2ASubjectAuthorizer](A2A/IA2ASubjectAuthorizer.md) — Partner→müşteri A2A yetki kontrolü.

## AI/

- [IEmbeddingPort](AI/IEmbeddingPort.md) — Metin → vektör.
- [IGeneralChatClient](AI/IGeneralChatClient.md) — Genel amaçlı LLM tamamlama.
- [IKnowledgeBaseSource](AI/IKnowledgeBaseSource.md) — KB dosya kaynağı.
- [IRealtimeVoiceTransport](AI/IRealtimeVoiceTransport.md) — Sesli chat WebSocket transport'u.
- [IReasoningChatClient](AI/IReasoningChatClient.md) — Reasoning modeli erişimi.
- [IVectorMemoryPort](AI/IVectorMemoryPort.md) — Qdrant vektör deposu.
- [RealtimeModels](AI/RealtimeModels.md) — Realtime port'unun vendor-nötr veri tipleri.
- [SemanticMemoryOptions](AI/SemanticMemoryOptions.md) — Semantic memory konfigürasyonu.
- [SelfImprovementOptions](AI/SelfImprovementOptions.md) — Self-improving loop konfigürasyonu.

## Auth/

- [IJwtAccessTokenProvider](Auth/IJwtAccessTokenProvider.md) — JWT access token üretimi.
- [IPasswordHasher](Auth/IPasswordHasher.md) — Şifre hash/doğrulama.
- [IRefreshTokenRepository](Auth/IRefreshTokenRepository.md) — Refresh token kalıcılığı (koşullu iptal `TryRevokeAsync` dahil).
- [IUserAuthRepository](Auth/IUserAuthRepository.md) — Kullanıcı hesabı kalıcılığı.
- [JwtOptions](Auth/JwtOptions.md) — JWT altyapı ayarları.

## Locking/

- [IAppDistributedLock](Locking/IAppDistributedLock.md) — Pod'lar arası dağıtık kilit.

## Messaging/

- [IMessageBusPort](Messaging/IMessageBusPort.md) — Pod'lar arası pub/sub mesaj yolu.

## Observability/

- [ICostCalculatorPort](Observability/ICostCalculatorPort.md) — LLM maliyet hesaplama.
- [ICostUsageStorePort](Observability/ICostUsageStorePort.md) — Anlık toplam kullanım sayaçları.
- [ILlmCallPersistencePort](Observability/ILlmCallPersistencePort.md) — LLM çağrılarının kalıcı kaydı.
- [IReasoningTraceStore](Observability/IReasoningTraceStore.md) — Ajan reasoning trace kaydı.
- [TelemetryConstants](Observability/TelemetryConstants.md) — OTel kaynak adı sabitleri.

## Persistence/

- [IApprovalQueue](Persistence/IApprovalQueue.md) — HITL onay kuyruğu (bloklamayan model).
- [IChatBridge](Persistence/IChatBridge.md) — Live Takeover mesaj köprüsü.
- [IChatModeRegistry](Persistence/IChatModeRegistry.md) — Session bazlı chat modu (bot/insan).
- [IComplaintRepository](Persistence/IComplaintRepository.md) — Şikayet kalıcılığı.
- [ICustomerProfileStore](Persistence/ICustomerProfileStore.md) — Müşteri profili kalıcılığı.
- [ICustomerRepository](Persistence/ICustomerRepository.md) — Müşteri varlığı/kimlik sahipliği.
- [IEscalationSink](Persistence/IEscalationSink.md) — Eskalasyon kaydı.
- [IHumanAgentRegistry](Persistence/IHumanAgentRegistry.md) — Temsilci kaydı ve yük yönetimi.
- [IKnowledgeArticleStore](Persistence/IKnowledgeArticleStore.md) — KB makale kalıcılığı.
- [ILessonStore](Persistence/ILessonStore.md) — Self-improving loop ders kaydı.
- [IOrderRepository](Persistence/IOrderRepository.md) — Sipariş kalıcılığı (atomik `PlaceOrder`).
- [IProductCatalogRepository](Persistence/IProductCatalogRepository.md) — Ürün kataloğu ve stok.
- [IRatingStore](Persistence/IRatingStore.md) — Müşteri değerlendirmesi.
- [ISessionManager](Persistence/ISessionManager.md) — Oturum ve konuşma geçmişi (en merkezi port).
- [ISlaEventSink](Persistence/ISlaEventSink.md) — SLA uyarı/ihlal kaydı.

## Kök seviye (Ports/Outbound/*.cs)

- [ApprovalOptions](ApprovalOptions.md) — HITL konfigürasyonu.
- [ContextPipelineOptions](ContextPipelineOptions.md) — Bağlam pipeline sınırları.
- [ContextResult](ContextResult.md) — Bağlam kurulumu sonucu (+ `ContextPart`).
- [EvaluationQualityOptions](EvaluationQualityOptions.md) — LLM-judge kalite kontrolü anahtarı.
- [IAgentTeamPort](IAgentTeamPort.md) — Ajan takımı (MAF workflow) çalıştırma.
- [IApprovalContextAccessor](IApprovalContextAccessor.md) — Ambient tur bağlamı (+ `ApprovalContext`).
- [IApprovalExecutionRouter](IApprovalExecutionRouter.md) — Onaylanan işin gerçek yürütmesi.
- [IBrowserChannel](IBrowserChannel.md) — Tarayıcı WebSocket kanalı.
- [IComplaintToolsService](IComplaintToolsService.md) — Şikayet tool'ları.
- [IContextPipeline](IContextPipeline.md) — Bağlam kurulumu port'u.
- [IContextSanitizer](IContextSanitizer.md) — Retrieval içeriği temizleme/sarmalama.
- [ICustomerProfileService](ICustomerProfileService.md) — Etkileşim → profil güncelleme.
- [ICustomerSupportToolsService](ICustomerSupportToolsService.md) — Tüm tool port'larının facade'i.
- [ICustomerUnderstandingService](ICustomerUnderstandingService.md) — Memory sentezi.
- [IOrderToolsService](IOrderToolsService.md) — Sipariş tool'ları.
- [IProductToolsService](IProductToolsService.md) — Ürün tool'ları.
- [IPromptRepository](IPromptRepository.md) — Prompt şablonu erişimi.
- [IRecommendationService](IRecommendationService.md) — Kural tabanlı ürün önerisi.
- [ISemanticMemoryWriter](ISemanticMemoryWriter.md) — Episodik bellek yazımı.
- [ISkillsBasedRouter](ISkillsBasedRouter.md) — Eskalasyon → temsilci eşleştirme.
- [IUiHintEmitter](IUiHintEmitter.md) — Tool'dan UI ipucu yayınlama.
- [ParallelExecutionOptions](ParallelExecutionOptions.md) — Compound sorgu paralellik ayarları.
- [WorkflowGuardOptions](WorkflowGuardOptions.md) — Workflow koruma parametreleri.

## Bağlantılar

- [../../README.md](../../README.md) — Application katmanı genel bakışı.
- [../Inbound/](../Inbound) — Primary/driving port'lar (Application'ın dışarıya sunduğu arayüzler).
