# Port Arayüzleri

Hexagonal mimaride **port**, bir katmanın dışarıya sunduğu veya dışarıdan beklediği sözleşmedir. Bu dosya Application katmanındaki tüm portları listeler.

## Driving Ports (Gelen Portlar)

API katmanı bu arayüzleri çağırır. Implementasyonlar `Services/` altındadır.

| Arayüz | Implementasyon | Açıklama |
|--------|---------------|---------|
| `IChatPort` | `ChatPortService` | Ana chat use-case (HandleAsync / HandleStreamAsync) |
| `IReasoningPort` | `ReasoningService` | Reasoning pipeline (ReasonAsync / ReasonStreamingAsync) |
| `ISessionPort` | `SessionPortService` | Session listeleme, silme, geçmiş okuma |
| `IChatSessionPort` | `ChatSessionPortService` | Admin chat panel — mesaj gönderme, geçmiş |
| `IApprovalPort` | `ApprovalPortService` | HITL approval queue — listele, onayla, reddet |
| `IEscalationPort` | `EscalationPortService` | Eskalasyon listesi ve yönetimi |
| `IHitlEventPort` | `HitlEventPortService` | HITL olay akışı (SSE) |
| `IHumanAgentPort` | `HumanAgentPortService` | İnsan temsilci kayıt ve mod yönetimi |
| `IAnalyticsPort` | `AnalyticsPortService` | Oturum puanlama ve analitik |
| `ITracePort` | `TracePortService` | Trace listeleme ve detay |
| `ITelemetryPort` | `TelemetryPortService` | LLM maliyet ve kullanım istatistikleri |
| `IEvaluationPort` | `EvaluationRunner` | Otomatik senaryo değerlendirme |
| `IMemoryPort` | `MemoryPortService` | Semantik bellek CRUD (API üzerinden) |
| `IPersonalizationPort` | `PersonalizationPortService` | Müşteri profil güncelleme |
| `IImprovementsPort` | `ImprovementsPortService` | Lesson mining ve iyileştirme önerileri |
| `ISlaPort` | `SlaPortService` | SLA olay listesi ve özet |
| `IInputGuard` | `InputGuard` | Gelen mesaj güvenlik filtresi |
| `IRealtimeBridge` | `RealtimeBridgeService` | Realtime voice (Scoped) |
| `IRealtimeNativeBridge` | `RealtimeNativeService` | Native realtime transport (Scoped) |
| `ITokenService` | `TokenPortService` | JWT token işlemleri |
| `IUserService` | `UserService` | Kullanıcı kimlik doğrulama |

---

## Driven Ports (Giden Portlar)

Application katmanı bu arayüzleri kullanır. Implementasyonlar Adapter projelerindedir.

### Genel

| Arayüz | Beklenen Adapter | Açıklama |
|--------|-----------------|---------|
| `IAgentTeamPort` | `CustomerSupportTeam` (Adapters.Agents) | Ajan workflow çalıştırma (`RunAsync`/`RunStreamingAsync`) + `GetWorkflowDiagram()` (admin panelindeki Mermaid diyagramı) |
| `IContextPipeline` | `ContextPipeline` | Context provider zinciri |
| `IPromptRepository` | `FileSystemPromptRepository` (Adapters.Persistence) | Prompt dosyaları okuyucu |
| `ICustomerSupportToolsService` | `CustomerSupportToolsService` | Genel tool orchestrator |
| `IComplaintToolsService` | `ComplaintToolsService` | Şikayet tool implementasyonları |
| `IOrderToolsService` | `OrderToolsService` | Sipariş tool implementasyonları |
| `IProductToolsService` | `ProductToolsService` | Ürün tool implementasyonları |
| `IApprovalContextAccessor` | `ApprovalContextAccessor` | AsyncLocal HITL context |
| `ICustomerProfileService` | `CustomerProfileService` | Müşteri profil güncelleme |
| `ICustomerUnderstandingService` | `CustomerUnderstandingService` | `CustomerProfile`'ı tek, sentezlenmiş `CustomerUnderstanding` görünümüne çevirir (LLM çağırmaz) |
| `IRecommendationService` | `RecommendationService` | Kural tabanlı ürün önerisi (LLM çağırmaz) — susma kuralları için bkz. [RecommendationService.md](Personalization/RecommendationService.md) |
| `ISemanticMemoryWriter` | `SemanticMemoryService` | Episodik bellek yazma |
| `ISkillsBasedRouter` | `SkillsBasedRouter` | Eskalasyon routing kararı |
| `IUiHintEmitter` | `UiHintEmitter` | Tool → streaming pipeline UI ipuçları — `Emit` **`bool` döner**, aşağıya bakın |
| `IBrowserChannel` | `WebSocketBrowserChannel` (Api) | WebSocket kanal abstraction |

> ⚠️ **`IUiHintEmitter.Emit` teslimi garanti etmez.** İpucu ancak ambient bağlamda bir
> session varsa kuyruğa girer; yoksa düşer ve metot `false` döner. Native sesli kanalda bu
> bağlam hiç kurulmadığı için bu yol gerçekten yürünüyor.
>
> Dolayısıyla bu port'u çağıran her tool, LLM'e döndürdüğü mesajı **dönüş değerine
> koşullamak zorundadır** — aksi halde "kullanıcıya gösterildi" gibi doğrulanmamış bir iddia
> üretir ve model kullanıcıyı olmayan bir arayüze yönlendirir. `Emit` eskiden `void`'di ve
> `ProductListTool` tam olarak bu hatayı yapıyordu; bkz.
> [`Tools/ProductToolsService.md`](Tools/ProductToolsService.md#31-picker-gerçekten-gösterildi-mi-showcategorypicker).
>
> Genel kural: bu bir **yan kanaldır**, garanti değil. Bir ipucu hiç çizilmezse konuşmanın
> yine de yürümesi gerekir — her ipucunun bir metin muadili olmalıdır.

### Persistence

| Arayüz | Implementasyon | Açıklama |
|--------|---------------|---------|
| `ISessionManager` | Postgres/InMemory | Session CRUD + geçmiş |
| `ICustomerRepository` | Postgres | Müşteri kayıt sorgulama |
| `IOrderRepository` | Postgres | Sipariş CRUD |
| `IComplaintRepository` | Postgres | Şikayet CRUD |
| `IProductCatalogRepository` | Postgres | Ürün kataloğu |
| `IApprovalQueue` | Postgres/InMemory | HITL approval kuyruğu |
| `IChatBridge` | Redis/InMemory | HITL live-chat köprüsü |
| `IChatModeRegistry` | Postgres/InMemory | Bot/Human mod kaydı |
| `IEscalationSink` | Postgres/InMemory | Eskalasyon kaydı |
| `IHumanAgentRegistry` | Postgres/InMemory | İnsan temsilci kaydı |
| `ICustomerProfileStore` | Postgres/InMemory | Müşteri profil deposu |
| `IRatingStore` | Postgres/InMemory | Oturum puanlaması |
| `ILessonStore` | Postgres/InMemory | Çıkarılan dersler |
| `ISlaEventSink` | Postgres/InMemory | SLA olay kaydı |

### AI

| Arayüz | Beklenen Adapter | Açıklama |
|--------|-----------------|---------|
| `IReasoningChatClient` | `ReasoningChatClient` (Adapters.AI) | o-series LLM (reasoning) |
| `IGeneralChatClient` | Adapters.AI | Genel amaçlı LLM (özet gibi) |
| `IEmbeddingPort` | Adapters.AI | Text → embedding vektörü |
| `IVectorMemoryPort` | Qdrant/InMemory | Vektör deposu |
| `IKnowledgeBaseSource` | Adapters.AI | KB kaynak okuyucu |
| `IRealtimeVoiceTransport` | Adapters.AI | Realtime ses protokolü |

### Observability

| Arayüz | Implementasyon | Açıklama |
|--------|---------------|---------|
| `IReasoningTraceStore` | Postgres/InMemory | Trace kayıt ve sorgulama |
| `ICostUsageStorePort` | `CostUsageStore` (Adapters.Telemetry) | LLM maliyet in-memory özeti |
| `ILlmCallPersistencePort` | `PostgresLlmCallUsageSink` (Adapters.Persistence) | LLM çağrı persist |
| `ICostCalculatorPort` | `CostCalculator` (Adapters.AI) | Model bazlı maliyet hesaplama |

### Mesajlaşma / Locking / Auth

| Arayüz | Implementasyon | Açıklama |
|--------|---------------|---------|
| `IMessageBusPort` | Redis/InMemory | Pub/sub mesaj kanalı |
| `IAppDistributedLock` | `RedisDistributedLockAdapter` | Dağıtık kilit (SLA guardian) |
| `IJwtAccessTokenProvider` | Adapters (Auth) | JWT token üretme |
| `IPasswordHasher` | Adapters (Auth) | Parola hash |
| `IRefreshTokenRepository` | Postgres/InMemory | Refresh token deposu |
| `IUserAuthRepository` | Postgres/InMemory | Kullanıcı kimlik doğrulama |

---

## Yeni driven port eklemek

1. `Ports/Outbound/` altında arayüz dosyası oluşturun.
2. İlgili adapter projesinde implementasyonu yazın.
3. Adapter'ın DI extension metoduna kaydı ekleyin.
4. `PortAliases.cs` dosyasına global using ekleyin (gerekiyorsa).

## Yeni driving port eklemek

1. `Ports/Inbound/` altında arayüz dosyası oluşturun.
2. `Services/` altında implementasyonu yazın.
3. `ApplicationServiceCollectionExtensions` içine kaydedin.
4. `Api` projesinde endpoint oluşturun.
