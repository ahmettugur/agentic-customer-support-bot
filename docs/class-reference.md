# Reference — Class / Interface Sözlüğü

Sistemde yer alan her sınıf, interface ve enum için **tek paragraf lık** rol tanımı + ana alan/metod listesi. Diğer dokümanlar (`adapters-agents/`, `domain/Model-Workflow.md`, `domain/Model-Reasoning.md`, `agentic-patterns.md`) bu referans üzerinden yüksek seviyeli anlatım yapar.

**Bölümler**:

- [1. Agents/](#1-agents)
- [2. Services/](#2-services)
- [3. Models/](#3-models)
- [4. Tools/](#4-tools)
- [5. Endpoints/](#5-endpoints)
- [6. Evaluation/](#6-evaluation)
- [7. Program & DI](#7-program--di)

---

## 1. Agents/

MAF (`Microsoft.Agents.AI` framework) üzerine kurulu **2 orkestrasyon sınıfı**. Her ikisi de singleton.

### `CustomerSupportTeam` — `Agents/CustomerSupportTeam.cs`

Sistemin merkezi orkestratörü. **6 MAF `ChatClientAgent`**'ı (Planning + 4 specialist + Response) ctor'da yaratır, tüm tool'ları kaydeder ve `AgentWorkflowBuilder.CreateGroupChatBuilderWith(...)` ile workflow derler. İki genel metod:

- **`RunAsync(query, history?, session?, reasoning?)`** — non-streaming. Compound query algılayıp `RunDecomposedAsync`'a ayrılabilir. Final string response döner, ResponseAgent TERMINATE marker'ı temizlenmiş.
- **`RunStreamingAsync(...)`** — SSE için event stream üretir (`agent`, `response_start`, `response_delta`, `response_complete`). Compound query'de `RunDecomposedStreamingAsync`'a düşer.

**Compound query helper'ları** (`CustomerSupportTeam.cs:912-1192`): `ShouldDecompose`, `RunDecomposedAsync`, `RunDecomposedStreamingAsync`, `DeriveSubReasoning`, `BuildSubQuery`, `FormatSubResult`, `JoinAggregatedParts`, `ExtractTextFromDelta` — her subtask ayrı workflow run'ı olarak koşturulur ve sonuçlar `JoinAggregatedParts` ile `\n\n---\n\n` ayırıcılı birleştirilir.

**Bağımlılıklar**: `IChatClient`, `ReasoningChatClient`, `PromptService`, `IContextPipeline`, `IOptions<WorkflowGuardOptions>`, `IOptions<ParallelExecutionOptions>`, `ICustomerSupportToolsService`, `ISemanticMemoryWriter?`, `ICustomerProfileService?`, `CustomerSupportChatManager`, `IReasoningTraceStore`.

---

### `CustomerSupportChatManager` — `Agents/CustomerSupportChatManager.cs`

MAF `GroupChatManager` türevi. Bir workflow iterasyonunda **"hangi agent konuşsun?"** kararını verir ve **"bitti mi?"** sorusunu cevaplar. Üç metod override eder:

- **`SelectNextAgentAsync`** — 3 katmanlı karar:
  1. PlanningAgent mesajı geldi → `selectedAgent` JSON alanına bak
  2. Specialist mesajı geldi → `postToolReflection.handoffSuggestion` var mı?
  3. Fallback → LLM routing veya pozisyon tabanlı varsayılan (ResponseAgent)
- **`ShouldTerminateAsync`** — 3 koşul:
  1. ResponseAgent TERMINATE marker üretti
  2. `WorkflowGuardOptions.MaxIterations` aşıldı
  3. `DetectRepeatedToolCall` — aynı tool+param imzası `MaxDuplicateToolCalls`'ı aştı
- **`ExtractResultAsync`** — workflow bitince final string'i çıkarır.

Ayrıca **ping-pong guard** mantığı ve `TerminationReason` etiketleme burada.

---

## 2. Services/

İş mantığı servisleri. Hepsi DI'da singleton.

### `ReasoningService` — `Services/ReasoningService.cs`

Reasoning pipeline'ın ana beyni. Workflow **öncesinde** çalışır. O-series model (`ReasoningChatClient`) ile tek LLM çağrısı yapar ve yapılandırılmış `ReasoningResult` üretir:

- **`ReasonAsync(query, session?, history?)`** — senkron, non-streaming.
- **`ReasonStreamingAsync(...)`** — SSE için `reasoning_start`, `reasoning_delta`, `reasoning_complete` event'leri.

İç akış (`ReasoningService.cs:287-396`):

1. `EntityVerifier.Verify(...)` → `VerifiedEntities` üretir (Katman 0).
2. `reasoning-system.md` + `reasoning-user.md` prompt'larını `VERIFIED_ENTITIES` placeholder ile render eder.
3. LLM'e gönderir, JSON çıktısını parse eder.
4. `ParseSteps` + `ParseSubTasks` ile structured step ve subtask'ları çıkarır.
5. `ReasoningSanityChecker.Check(...)` → `SanityIssues` doldurur (Katman 1.5).
6. Fallback: LLM hata verirse `CreateFallbackResult` minimal bir `ReasoningResult` döner.

---

### `EntityVerifier` — `Services/EntityVerifier.cs`

**Katman 0**. Deterministik (LLM'siz) entity grounding:

- **`Verify(query, session?, history?)` → `VerifiedEntities`** —
  1. `IdExtractor.Extract(query)` ile regex tabanlı çıkarım
  2. History ve session state'ten eksik ID'leri tamamla (5 turluk geriye tarama)
  3. Her entity için repository port'ları üzerinden lookup (`IOrderRepository`, `IProductCatalogRepository`, `IComplaintRepository`)
  4. Sonuç: `Verified` | `NotFoundInDb` | `FormatOnly`
  5. `customer_id Verified` ise `DerivedLastOrderId` ve `DerivedOrderCount` türetir
- **`BuildVerifiedBlock(verified)` → string** — reasoning prompt'una enjekte edilecek `[VERIFIED ENTITIES]` bloğunu oluşturur.

---

### `ReasoningSanityChecker` + `IReasoningSanityRule` — `Services/ReasoningSanityChecker.cs`

**Katman 1.5**. **Strategy pattern** ile 8 deterministik kuralı sırayla çalıştırır. Her kural ayrı bir `IReasoningSanityRule` implementasyonudur (aynı dosyada):

| # | Sınıf | `Code` |
|---|---|---|
| 1 | `OverconfidentClarificationRule` | `overconfident_clarification` |
| 2 | `RedundantRequiredInfoRule` | `redundant_required_info` |
| 3 | `IntentActionMismatchRule` | `intent_action_mismatch` |
| 4 | `LowConfidenceNoMissingRule` | `low_confidence_no_missing` |
| 5 | `AssumptionHeavyStepsRule` | `assumption_based_step` |
| 6 | `OverconfidentAssumptionsRule` | `overconfident_assumptions` |
| 7 | `NotFoundIgnoredRule` | `not_found_ignored` |
| 8 | `SubTasksIgnoredRule` | `subtasks_ignored` |

**`IReasoningSanityRule`** arayüzü:

```csharp
public interface IReasoningSanityRule
{
    string Code { get; }
    void Apply(ReasoningResult result, VerifiedEntities verified, List<ReasoningIssue> issues);
}
```

**`Check(result, verified)` → `List<ReasoningIssue>`**. Constructor'da `_rules` listesi sırayla iterate edilir; bir kural exception fırlatırsa loglanır ve diğerleri çalışmaya devam eder. Her issue `code`, `severity`, `message`, `field`, `suggestedFix` içerir. **Workflow'u durdurmaz** — sadece trace'e ve UI'a yazılır.

> Yeni kural ekleme adımları için bkz. [developer-guide.md#yeni-sanity-checker-kuralı-ekleme](developer-guide.md#yeni-sanity-checker-kural%C4%B1-ekleme).

---

### `IPromptRepository` + `FileSystemPromptRepository` — `Application/Ports/Driven/IPromptRepository.cs`, `Adapters.Persistence/FileSystem/FileSystemPromptRepository.cs`

`Prompts/**/*.md` dosyalarını uygulama başlangıcında belleğe yükler (her istekte disk I/O yok). Key format: `agents/planning-agent`, `services/reasoning-system` (uzantı atılır, path ayıracı `/`'e normalize edilir).

- **`Get(key)` → string** — saf şablonu döndürür; anahtar yoksa `KeyNotFoundException`.
- **`Render(key, vars?)` → string** — `{{PLACEHOLDER}}` sözdizimini dictionary'den doldurur. Bulunmayan placeholder'lar boş string ile temizlenir.
- **`Keys`** — kayıtlı tüm anahtarlar (debug için).

README/NOTES adlı .md dosyaları atlanır (insanlara yönelik dokümantasyon olarak görülür). `IPromptRepository` Application katmanında port olarak tanımlıdır, `FileSystemPromptRepository` ise Persistence adapter'ında implementasyondur.

---

### `IdExtractor` — `Services/IdExtractor.cs`

Statik sınıf. **Deterministik** (LLM'siz) regex tabanlı entity extraction:

- **Pattern'lar** — `ORD[-_\s]?N`, `CMP[-_\s]?N`, `CUST[-_\s]?N`, saf 3-5 haneli sayı.
- **`Extract(text)` → `ExtractedIds`** — `OrderId`, `CustomerId`, `ComplaintId`. "2025 yılında" gibi yanlış yakalamaları önlemek için numeric fallback **sadece** başka anchor ID varsa veya query ≤4 token ise aktive olur.
- **`BuildHintMessage(ids)` → string?** — planning prompt'una eklenen sipariş sorgu öncelik kuralı:
  - `order_id VAR` → `order_status_tool` (customer_id ISTEME)
  - `customer_id VAR` → `get_last_order_tool` (order_id ISTEME)

`EntityVerifier` bu servisi ilk adım olarak kullanır.

---

### `ContextPipeline` + `IContextProvider` — `Services/ContextPipeline.cs`, `Services/IContextProvider.cs`

**Interface `IContextProvider`** — her sağlayıcı bir `Name`, `Order` (öncelik), `GetContextAsync(session)` metodu uygular.

**`ContextPipeline.BuildContextAsync(session)` → string** — tüm kayıtlı provider'ları `Order`'a göre sıralı çalıştırır ve üretilen metinleri `\n\n` ile birleştirir. Bir provider exception fırlatırsa loglanır ve atlanır.

---

### `CustomerContextProvider` — `Services/Providers/CustomerContextProvider.cs`

`Order=10`. Oturumdaki `State.CustomerId` varsa `IOrderRepository.GetAllAsync` + `IComplaintRepository.QueryAsync` ile müşterinin son 5 siparişi + son 3 şikayet hakkında metin üretir. Format: `[Müşteri Bağlamı — {id}] Toplam sipariş: N ... Toplam şikayet: M ...`.

---

### `ConversationSummaryProvider` — `Services/Providers/ConversationSummaryProvider.cs`

`Order=5`. Konuşma 8 mesajı aştığında eski mesajları **LLM ile** özetler (max 150 kelime), `SessionState.ConversationSummary`'e yazar ve sonraki turda cache'den okur. Format: `[Konuşma Özeti]\n{summary}`.

---

### `ISessionManager` + `InMemorySessionManager` — `Application/Ports/Driven/Persistence/ISessionManager.cs`, `Adapters.Persistence/InMemory/InMemorySessionManager.cs`

**Interface `ISessionManager : IConversationStore`** — oturum yönetimi + mesaj geçmişi birleşik sözleşme:

- `GetOrCreateSession(sessionId?)` → `AgentSession`
- `GetSession(id)` → `AgentSession?`
- `UpdateSession(session)` → void
- `ExtractAndUpdateState(sessionId, userMsg, botResp)` — CustomerId/OrderId regex çıkarımı + intent detection + state update

**`InMemorySessionManager`** iki `ConcurrentDictionary` kullanır: `_sessions` (state) ve `_messageHistory` (mesajlar). Thread-safe. Postgres implementasyonu (`PostgresSessionManager`) da mevcuttur — `Persistence:Provider` ayarına göre seçilir.

---

### `IConversationStore` — `Application/Ports/Driven/Persistence/IConversationStore.cs`

**Interface `IConversationStore`** — sadece mesaj geçmişi:

- `GetHistory(sessionId)` → `List<ChatMessage>`
- `AddExchange(sessionId, userQuery, assistantResponse)` — mesaj çifti ekler
- `ClearSession(sessionId)` — geçmişi sil
- `GetAllSessions()` → `List<SessionInfo>` (sidebar için)

`IConversationStore` ayrı bir implementasyona sahip değildir — `InMemorySessionManager` (ve `PostgresSessionManager`) her iki interface'i (`ISessionManager` + `IConversationStore`) birden uygular. DI'da aynı instance'a bind edilir — bkz. [architecture.md](architecture.md#dependency-injection-haritası).

---

### `IReasoningTraceStore` + `InMemoryReasoningTraceStore` — `Application/Ports/Driven/Observability/IReasoningTraceStore.cs`, `Adapters.Persistence/InMemory/InMemoryReasoningTraceStore.cs`

**Interface `IReasoningTraceStore`** — trace kaydı:

- `StartTrace(sessionId, userQuery)` → `ReasoningTrace`
- `Update(trace)` — mutable ref update
- `Complete(traceId, terminationReason?, finalResponse?, error?)`
- `Get(traceId)` → `ReasoningTrace?`
- `GetRecent(count)` → `IReadOnlyList<ReasoningTrace>`
- `GetBySession(sessionId)` → `IReadOnlyList<ReasoningTrace>`

**`InMemoryReasoningTraceStore`** — **ring buffer** pattern: `_byId` (ConcurrentDictionary) + `_insertionOrder` (ConcurrentQueue). Default kapasite **500 trace**; kapasite aşılınca en eski trace düşer. `Complete` metodunda `FinalResponse` 2000 karakterle truncate edilir. Postgres implementasyonu (`PostgresReasoningTraceStore`) da mevcuttur.

---

### `InputGuard` — `Services/InputGuard.cs`

Kullanıcı girdi güvenlik filtresi. `Inspect(query)` → `Pass | Flagged | Reject`. Prompt injection, zararlı içerik ve aşırı uzun girdi kontrolü yapar. Chat endpoint'lerinde ilk adımda çalışır.

---

### `SemanticMemoryContextProvider` — `Services/Providers/SemanticMemoryContextProvider.cs`

`Order=20`. Qdrant'tan Knowledge Base + Lessons araması yapıp context pipeline'a RAG sonuçları enjekte eder. `SemanticMemory:Enabled = false` ise atlanır.

---

### `CustomerProfileContextProvider` — `Services/Providers/CustomerProfileContextProvider.cs`

`Order=15`. `state.CustomerId` set'liyse müşteri profilini context'e enjekte eder (tüm ajanlar görür).

---

### `ApprovalGateService` — `Adapters.Agents/ApprovalGateService.cs`

HITL approval gate. Yan etkili tool lambda'larını sararak `HumanInTheLoop.Enabled = true` ise admin onayı bekletir. Timeout ve auto-approve politikaları destekler. Eskalasyon routing mantığını `EscalationPolicyService`'e delege eder.

---

### `EscalationPolicyService` — `Application/Services/EscalationPolicyService.cs`

Eskalasyon routing iş politikası. `ApprovalGateService`'den extract edilerek Application katmanına taşınmıştır. Sorumlulukları:
- Pending eskalasyonları işleme (dedup, priority elevation)
- `ISkillsBasedRouter` üzerinden skills-based temsilci eşleştirme
- Load tracking (`IncrementLoad`/`DecrementLoad`)

---

### `SlaGuardianService` — `Application/Services/Sla/SlaGuardianService.cs`

`BackgroundService`. Periyodik olarak bekleyen onay ve açık eskalasyonları tarar. Breach durumunda onayları reddeder, eskalasyon önceliğini yükseltir.

---

### `CustomerProfileService` — `Services/Personalization/CustomerProfileService.cs`

Per-customer profil yönetimi. `RecordInteraction` (LLM-siz heuristik, her turda) + `ConsolidateAsync` (admin tetikli LLM özet + ton çıkarımı).

---

### `SkillsBasedRouter` — `Application/Services/Routing/SkillsBasedRouter.cs`

Deterministik skills-based eşleştirme. Reasoning trace + müşteri profili → required skill çıkarımı → aday seçimi (skill match + dil + load balance). LLM kullanmaz. `ISkillsBasedRouter` port'unu (`Application/Ports/Driven/`) implemente eder.

---

### `WorkflowExecutor` — `Services/Workflow/WorkflowExecutor.cs`

Low-code workflow designer motoru. Admin tanımlı JSON workflow'larını LLM-siz deterministik olarak çalıştırır. Step tipleri: Respond, Lookup, Branch, SetVariable.

---

### `TelemetryChatClient` — `Services/Telemetry/TelemetryChatClient.cs`

`IChatClient` `DelegatingChatClient` wrapper'ı. Her LLM çağrısında span açar, token + USD maliyet + latency kaydeder. Streaming dahil.

---

### `CostUsageStore` — `Services/Telemetry/CostUsageStore.cs`

In-memory model bazlı agregat maliyet deposu. Admin `/telemetry/cost` endpoint'i bu store'dan okur.

---

### `AnalyticsPortService` — `Application/Services/AnalyticsPortService.cs`

Oturum, intent dağılımı, ortalama puan ve son rating bilgilerini toplar. Admin analytics dashboard verisi sağlar. `IAnalyticsPort` driving port interface'ini implemente eder.

---

### `ReasoningChatClient` — `Adapters.AI/Chat/ReasoningChatClient.cs`

Reasoning modeli (varsayılan `gpt-5.4-nano`) için `IChatClient` wrapper. `ReasoningEffort` ("low", "medium", "high") + `ModelName` ile konfigüre edilir. `ReasoningService` tarafından DI'dan alınır; chat client'tan ayrışmak için sarmalayıcı sınıf kullanılır. AI adapter katmanında yaşar.

---

### `PlanningResultParser` — `Services/PlanningResultParser.cs`

Statik sınıf. PlanningAgent çıktısından `PlanningResult` çıkarır:

- **`TryParse(planningOutput)` → `PlanningResult?`** —
  1. `ExtractJsonBlock`: Önce ` ```json ... ``` ` fence, sonra ``` ... ```, en son düz `{ ... }` arama.
  2. `JsonDocument.Parse` + property-by-property okuma.
  3. `intentConfidence` sayı veya string olabilir (`ReasoningResult.StringToScore` fallback).
  4. `alternativesRejected` → `List<RejectedAlternative>`.

Parse başarısızsa null döner; ChatManager fallback mantığına düşer.

---

### `SpecialistReasoningParser` — `Services/SpecialistReasoningParser.cs`

Statik sınıf. Specialist ajan çıktısından `SpecialistReasoning` çıkarır:

- **`TryParse(agentOutput, agentName)` → `SpecialistReasoning?`** —
  1. JSON bloğu extract (aynı üç katmanlı fallback).
  2. `preToolCheck`, `resultConfidence` veya `postToolReflection` key'lerinden **en az biri** olmalı; aksi halde null.
  3. `ParsePreToolCheck` → `PreToolCheck` alt objesi.
  4. `ParsePostToolReflection` → `PostToolReflection`; `status` alanı `NormalizeStatus` ile kanonikleştirilir (`done | needs_followup | needs_escalation | failed | partial`).
  5. `handoffSuggestion` "null"/"none" string'leri → `null`.

---

## 3. Domain Modelleri

### Entity POCO'lar

Hexagonal mimaride **domain modelleri** `CustomerSupportBot.Domain/Model/` altında tanımlı, repository port'ları üzerinden erişilir:

| Model | Alan | Açıklama |
|---|---|---|
| `ProductInfo` | `Price`, `Stock` | Ürün bilgisi. Fiyat sabit, stok sipariş verildiğinde azalır. `Models/ProductInfo.cs` |
| `OrderInfo` | `Product`, `Quantity`, `CustomerId`, `Status`, `OrderDate` | Sipariş kaydı. `Status` değerleri `WellKnown.OrderStatuses` içinde (`Processing`, `Shipped`, `Delivered`, `Cancelled`). `Models/OrderInfo.cs` |
| `ComplaintInfo` | `OrderId`, `CustomerId`, `Complaint`, `Status` | Şikayet kaydı. `Status` değerleri `WellKnown.ComplaintStatuses` içinde (`Pending`, `InProgress`, `Resolved`). `Models/ComplaintInfo.cs` |
| `AgentSession` | `SessionId`, `CreatedAt`, `LastActivity`, `State` | Oturumun kendisi; `State` alt nesnesi state'i taşır. `Models/AgentSession.cs` |
| `SessionState` | `CustomerId`, `CurrentIntent`, `CollectedInfo`, `TurnCount`, `ConversationSummary`, `Phase`, `ForceReplanNextTurn`, `ReplanNote`, `ReplanRequestedBy`, `ReplanRequestedAt` | Oturum durumu — ajanlar arası paylaşılan bağlam. Replan alanları admin "Yeniden Planla" akışında one-shot olarak kullanılır (bkz. [agentic-patterns.md#204-admin-replan](agentic-patterns.md#204-admin-replan-one-shot-planning-override--auto-bot-turn)). `CustomerSupportBot.Domain/Model/AgentSession.cs` |

---

### Demo Veri Adapтörleri — `CustomerSupportBot.Adapters.Persistence/InMemory/`

`FakeDatabase` statik sınıfı kaldırılmıştır. Yerini hexagonal adapter implementasyonları almıştır:

| Adapter | Implements | Seed Veri |
|---|---|---|
| `InMemoryProductCatalogAdapter` | `IProductCatalogRepository` | 5 ürün (Dell XPS 15, iPhone 15 Pro, Sony WH-1000XM5, Galaxy Tab S9, MX Master 3S) |
| `InMemoryOrderAdapter` | `IOrderRepository` | 2 seed sipariş (`ORD-1` → CUST-1990, `ORD-2` → CUST-2026) |
| `InMemoryComplaintAdapter` | `IComplaintRepository` | 2 seed şikayet (`CMP-1`, `CMP-2`) |

Thread-safety: `ConcurrentDictionary` ile sağlanır. Stok kontrolü `IOrderRepository.PlaceAsync` içinde `lock` altında yapılır.

---

### Request/response modelleri

| Model | Alan | Açıklama |
|---|---|---|
| `ChatRequest` | `Query`, `SessionId?` | `/chat/` ve `/chat/stream` request body. `record` tipi. `Models/ChatRequest.cs` |
| `ChatResponse` | `Response`, `SessionId`, `Reasoning?` | `/chat/` response body. `record` tipi. `Models/ChatResponse.cs` |
| `StreamEvent` | `Type`, `Data` | SSE event yapısı. `StreamEventTypes` sabitleri: `session`, `reasoning_start/delta/complete`, `agent`, `response_start/delta/complete`, `error`, `done`, HITL: `approval_required/resolved`, `escalation_created`, `human_joined/message/left`, `handoff_pending/cleared`, `bridge_message`, `bot_typing`, ayrıca `sentiment_update/alert`. `Models/StreamEvent.cs` |

Event payload şemaları → [api/](api/README.md).

---

### `ReasoningResult` — `Models/ReasoningResult.cs`

Reasoning pipeline'ın ana çıktısı. 16 alan — legacy + yeni:

| Alan | Tip | Amaç |
|---|---|---|
| `Analysis` | string | Kısa analiz (1-2 cümle) |
| `Steps` | `List<ReasoningStep>` | Yapılandırılmış plan adımları |
| `Intent` | string | "sipariş_sorgulama" vb. kanonik intent |
| `RequiredInfo` | `List<string>` | Eksik alanlar |
| `Confidence` | string (legacy) | "yüksek/orta/düşük" |
| `ConfidenceScore` | double | 0.0-1.0 sayısal |
| `Rationale` | string | Planın gerekçesi |
| `Assumptions` | `List<string>` | Varsayımlar |
| `NextAction` | string | Bir sonraki somut adım |
| `DecisionReason` | string | Karar gerekçesi |
| `SanityIssues` | `List<ReasoningIssue>` | sanity check tespitleri |
| `SubTasks` | `List<SubTask>` | compound query decomposition |

Statik helper: `ScoreToString(score)` + `StringToScore(str)`.

---

### `ReasoningStep` — `Models/ReasoningStep.cs`

Bir reasoning adımının yapılandırılmış özeti:

- `Order` — 1-indexed sıra
- `Description` — insanlar için açıklama (UI'da bu gösterilir)
- `Action` — `extract | route | clarify | call_tool | verify | terminate`
- `Premise` — önkoşul
- `Grounding` — kanıt kaynağı (`regex | session_state | history | DB | derived | assumption`)
- `Confidence` — 0.0-1.0 adım bazlı güven
- `AlternativeRejected` — reddedilen alternatif

Sanity checker (`assumption_based_step`, `overconfident_assumptions`) bu alanları kullanır.

---

### `SubTask` — `Models/SubTask.cs`

Compound query decomposition parçası:

- `Order` — 1-indexed yürütme sırası
- `Intent` — kanonik intent (üst düzey intent'ten farklı olabilir)
- `Description` — açıklama
- `TargetAgent` — "OrderAgent" vb.
- `Entities` — `Dictionary<string, string>` (ör. `{"order_id": "ORD-1"}`)
- `Dependencies` — `List<int>` — önce bitmesi gereken subtask sıra numaraları (şu an kullanılmıyor)

`CustomerSupportTeam.ShouldDecompose` **2+ subtask ve 2+ farklı agent** varsa `RunDecomposedAsync` dalına geçer.

---

### `ReasoningIssue` + `IssueSeverity` — `Models/ReasoningIssue.cs`

Sanity checker çıktısı:

- `Code` — snake_case (`overconfident_clarification` vb.)
- `Severity` — enum `Info | Warn | Error`
- `Message` — Türkçe açıklama
- `Field` — hangi alanı işaret ettiği (ör. `requiredInfo[0]`)
- `SuggestedFix` — geliştiriciye öneri

Frontend bu yapıyı severity badge'i ile render eder.

---

### `VerifiedEntities` + `VerifiedEntity` + enums — `Models/VerifiedEntities.cs`

EntityVerifier çıktısı:

**`VerifiedEntities`** — `OrderId?`, `CustomerId?`, `ComplaintId?` + türetilmiş `DerivedLastOrderId`, `DerivedOrderCount`. `HasAny`/`HasAnyVerified` bayrakları.

**`VerifiedEntity`** — `Value`, `Source`, `Verification`, `Attributes?` (DB'den çekilmiş metadata).

**Enums**:

- `EntitySource`: `Query | History | SessionState | Derived`
- `EntityVerification`: `Verified | NotFoundInDb | FormatOnly`

---

### `PlanningResult` + `RejectedAlternative` — `Models/PlanningResult.cs`

PlanningAgent JSON çıktısı:

- `DetectedIntent`, `IntentConfidence`, `SupportingEvidence`
- `SelectedAgent`, `Rationale`, `TaskDescription`
- `AlternativesRejected` — `List<RejectedAlternative>` (agent + reason)
- `NeedsClarification`, `ClarificationQuestion`

---

### `SpecialistReasoning` + `PreToolCheck` + `PostToolReflection` — `Models/SpecialistReasoning.cs`

Specialist ajanların (ProductInquiry, Order, Complaint) tool çağrısı etrafındaki yapılandırılmış reasoning'i.

**`PreToolCheck`** (tool çağrısı ÖNCESİ) — `RequiredParams`, `CollectedParams`, `MissingParams`, `CanProceed`, `Reasoning`, `Confidence`.

**`PostToolReflection`** (tool çağrısı SONRASI) — `TaskComplete`, `Status` (`done | needs_followup | needs_escalation | failed | partial`), `HandoffSuggestion`, `HandoffReason`, `MissingContext`, `Summary`.

ChatManager `handoffSuggestion`'a bakarak dinamik handoff yapar.

---

### `ReasoningTrace` + `AgentVisit` + `ToolInvocation` — `Models/ReasoningTrace.cs`

Bir workflow koşusunun **tam kaydı**:

- Kimlik: `TraceId` (GUID), `SessionId`, `UserQuery`
- Zaman: `StartedAt`, `CompletedAt`, `DurationMs` (computed)
- Reasoning parçaları: `Reasoning` (`ReasoningResult`), `Planning` (`PlanningResult?`), `SpecialistReasonings`
- Agent/tool kayıtları: `AgentVisits`, `ToolCalls`
- Sonuç: `TerminationReason`, `FinalResponse` (max 2000 char), `IterationCount`, `Error?`, `EstimatedTokens`

**`AgentVisit`** — `AgentName`, `StartedAt`, `CompletedAt`, `Output` (max 500 char).

**`ToolInvocation`** — `ToolName`, `InvokedAt`, `AgentName`, `ParametersSummary`, `ResultSummary`, `Success`, `Signature` (tekrar tespiti için name+params hash).

---

### `ToolResult` + `ToolError` + `ToolErrorCategories` — `Models/ToolResult.cs`

Tool çağrılarının **standart dönüş zarfı**. Tüm 6 tool fonksiyonu bunu döner:

- `Success`, `Confidence` (0.0-1.0), `Message` (Türkçe), `Data` (anonim obje), `Error?`
- `SuggestedAction` — `proceed | ask_user | retry | escalate | abort`

**Factory helper'ları**:

- `Ok(message, data?, confidence=1.0)`
- `ValidationError(userMsg, ...missingFields)` → `WellKnown.ToolErrorCodes.MissingRequiredField`
- `NotFound(code, userMsg)` — `code` için `WellKnown.ToolErrorCodes` sabitlerini kullan
- `Conflict(code, userMsg, partialConfidence=0.3)`
- `SystemError(code, userMsg)` → `escalate`

**`ToolError`** — `Code`, `Category` (`validation | not_found | conflict | business_rule | system`), `Message`, `MissingFields`.

---

### `WorkflowGuardOptions` — `Application/Ports/Driven/WorkflowGuardOptions.cs`

`appsettings.json` "WorkflowGuards" bölümünden bind edilen guard ayarları:

- `TimeoutSeconds` = 60 — tüm workflow timeout
- `MaxDuplicateToolCalls` = 3 — aynı tool+param tekrar eşiği
- `MaxTokensPerRequest` = 30000 — tahmini token üst sınırı
- `MaxIterations` = 20 — MAF superstep üst sınırı

ChatManager `ShouldTerminateAsync` bu ayarları kullanır.

---

## 4. Tools/

### `CustomerSupportToolsService` — `Application/Services/CustomerSupportToolsService.cs`

**7 tool fonksiyonu**, hepsi `[Description]` attribute'u ile LLM'e açıklanır ve `AIFunctionFactory.Create()` ile MAF agent'larına bağlanır. Tümü `ToolResult` döner. `ICustomerSupportToolsService` interface'ini implemente eder ve DI ile kayıtlıdır.

| Tool | İmza | Hangi agent | Side effect |
|---|---|---|---|
| `ProductInquiryTool` | `(productName)` | ProductInquiryAgent | ❌ read-only |
| `OrderPlacementTool` | `(productName, quantity?, customerId)` | OrderAgent | ✅ `OrdersDb` + stok |
| `OrderStatusTool` | `(orderId)` | OrderAgent | ❌ |
| `ComplaintRegistrationTool` | `(orderId, complaintText, customerId?)` | ComplaintAgent | ✅ `ComplaintsDb` |
| `GetLastOrderTool` | `(customerId)` | OrderAgent | ❌ |
| `GetAllOrdersTool` | `(customerId)` | OrderAgent | ❌ |
| `HumanHandoffTool` | `(reason, sessionId)` | HumanHandoffAgent | ✅ eskalasyon |

**Özel davranışlar**:

- `OrderPlacementTool` — stok kontrolü **`lock (_stockLock)`** altında (race-safe). Eksik alan → `ValidationError`, ürün yok → `NotFound(WellKnown.ToolErrorCodes.ProductNotFound)`, stok yetersiz → `Conflict(WellKnown.ToolErrorCodes.StockInsufficient)`.
- `ComplaintRegistrationTool` — `customerId` opsiyonel; boşsa `OrdersDb[orderId].CustomerId`'den türetir. Verilen customerId order sahibiyle uyuşmuyorsa → `Conflict(WellKnown.ToolErrorCodes.CustomerIdMismatch)`.
- Tool çıktıları her zaman `ToolResult` → LLM düz metin değil, yapılandırılmış sinyal görür.

Tool → agent eşleşmesi ve handoff davranışları → [adapters-agents/](adapters-agents/README.md).

---

### `WellKnown` — `Models/WellKnown.cs`

Sistemdeki tüm magic string ve sabit değerlerin **tek merkezi kaynağı**. Yeni sabit eklerken hard-code yerine ilgili nested class'a ekle. Önemli alt sınıflar:

| Alt sınıf | İçerik | Tipik kullanım |
|---|---|---|
| `Defaults` | `Admin`, `System` | HITL takeover, SSE event payload |
| `Intents` | `OrderCreation`, `OrderInquiry`, `OrderListing`, `Complaint`, `ProductInfo`, `General`, `Unknown` | `SessionState.CurrentIntent`, planning routing |
| `IntentKeywords` | `(Intent, string[] keywords)` tuple listesi | `InMemorySessionManager.DetectUserIntent` tablo tabanlı niyet algılama |
| `Phases` | `Inquiry`, `Action`, `Resolution` | `SessionState.Phase` |
| `AgentNames` | `Planning`, `ProductInquiry`, `Order`, `Complaint`, `Response`, + `Specialists[]`, `All[]` | Agent referansları, ChatManager routing |
| `OrderStatuses` | `Processing`, `Shipped`, `Delivered`, `Cancelled` | `OrderInfo.Status`, InMemory seed veri |
| `ComplaintStatuses` | `Pending`, `InProgress`, `Resolved` | `ComplaintInfo.Status` |
| `ToolErrorCodes` | `MissingRequiredField`, `ProductNotFound`, `OrderNotFound`, `StockInsufficient`, `CustomerIdMismatch`, `NoOrdersForCustomer` | `ToolResult.NotFound/Conflict` |
| `ToolParameterNames` | `CustomerId`, `OrderId`, `ProductName`, `Quantity`, `Reason`, `ComplaintDescription` (snake_case) | Tool validation `MissingFields` |
| `TaskStatuses` | `Done`, `NeedsFollowUp`, `NeedsEscalation`, `Failed`, `Partial` | `PostToolReflection.Status` (string) — type-safe alternatif: `PostToolReflection.StatusEnum : TaskCompletionStatus` |
| `Confidence` | `High`, `Medium`, `Low` | string karşılık; type-safe alternatif: `ReasoningResult.ConfidenceLevel : ConfidenceLevel` |
| `EscalationActions` | `Acknowledge`, `Resolve`, `Dismiss` | Admin endpoint payload |
| `ChatModes` | `Bot`, `Human` | HITL takeover |
| `FallbackMessages` | LLM hata fallback metinleri | ResponseAgent, error path |
| `ResponseKeywords` | `SuccessMarker`, `MissingInfoMarker` | Phase tespiti |
| `Evaluation.ScenarioFileName` | `evaluation-scenarios.yaml` | EvaluationRunner |
| `ReasoningEffort.PropertyKey` | `reasoning_effort` | ChatOptions metadata |

> **Konvansiyon**: Yeni magic string yazmadan önce `WellKnown`'da uygun yer var mı kontrol et. Yoksa yeni bir nested class veya entry oluştur. Bu sayede DRY + tek nokta değişiklik garantilenir.

---

## 5. Endpoints/

ASP.NET Core Minimal API. Her dosya **extension method** olarak kayıt: `app.MapXxxEndpoints()`.

### `ChatEndpoints` — `Endpoints/ChatEndpoints.cs`

2 endpoint:

| Method | Route | Handler |
|---|---|---|
| POST | `/chat/` | `HandleChatAsync` — non-streaming; reasoning → workflow sıralı; `ChatResponse` JSON döner |
| POST | `/chat/stream` | `HandleChatStreamAsync` — SSE; reasoning stream + workflow stream + done event |

Detay şemaları + akış → [api/](api/README.md).

---

### `TraceEndpoints` — `Endpoints/TraceEndpoints.cs`

4 GET endpoint:

| Route | Döner |
|---|---|
| `/traces/recent?count=20` | Son N `ReasoningTrace` |
| `/traces/{traceId}` | Tek trace (yoksa 404) |
| `/traces/by-session/{sessionId}` | Oturumun tüm trace'leri |
| `/traces/stats` | Aggregate: `totalTraces`, `completedCount`, `errorCount`, `avgDurationMs`, `avgIterationCount`, `terminationReasons{}` |

---

### `SessionEndpoints` — `Endpoints/SessionEndpoints.cs`

3 GET endpoint:

| Route | Döner |
|---|---|
| `/sessions/` | Tüm oturum özetleri (`SessionInfo[]`) |
| `/sessions/{sessionId}/messages` | Mesaj geçmişi (`{role, text}[]`) |
| `/sessions/{sessionId}/state` | Oturum + state (`SessionId`, `CreatedAt`, `LastActivity`, `State`) |

---

### `EvaluationEndpoints` — `Endpoints/EvaluationEndpoints.cs`

3 endpoint:

| Method | Route | Handler |
|---|---|---|
| GET | `/eval/scenarios` | `ScenarioFile` özet (version, totalScenarios, scenarios[]) |
| POST | `/eval/run?limit=N` | Tümünü koştur → `EvaluationRunResult` |
| POST | `/eval/run/{id}` | Tek senaryo → `ScenarioResult` |

Scenario YAML yolu: önce `docs/evaluation-scenarios.yaml`, sonra `docs/evaluation-scenarios.yaml`.

---

### `SseWriter` — `Endpoints/SseWriter.cs`

Internal static helper. SSE formatı: `event: TYPE\ndata: JSON\n\n`. JSON CamelCase + `UnsafeRelaxedJsonEscaping` (Türkçe karakter için).

- **`WriteHeaders(response)`** — `Content-Type: text/event-stream`, `Cache-Control: no-cache`, `X-Accel-Buffering: no` (nginx), `Connection: keep-alive`
- **`WriteEventAsync(response, eventType, data, ct)`** — JSON serialize + flush
- **`GetTextFromAnon(data)`** — anonim `{text=...}` objesinden reflection ile text okur (stream delta toplama için)

---

## 6. Evaluation/

### `EvaluationRunner` — `Evaluation/EvaluationRunner.cs`

- **`static LoadScenarios(yamlPath)` → `ScenarioFile`** — YamlDotNet + `UnderscoredNamingConvention` + `IgnoreUnmatchedProperties`.
- **`RunAsync(scenarios, ct?)` → `EvaluationRunResult`** — her senaryoyu ayrı session'da koşturur; sonuçları `Passed/Failed/Partial` kategorize eder.
- **`RunScenarioAsync(scenario, ct?)` → `ScenarioResult`** — izole session + `ReasonAsync` + `RunAsync` + session'ın son trace'i + `CriteriaEvaluator.Evaluate` her kriter için.

---

### `CriteriaEvaluator` — `Evaluation/CriteriaEvaluator.cs`

Statik sınıf. YAML'daki `success_criteria` string listesini regex tabanlı parse ederek değerlendirir. Desteklenen patternler:

| Pattern | Örnek | Nasıl değerlendirilir |
|---|---|---|
| `response contains "X"` / `response contains X` | `response contains "Kargolandı"` | Response text'inde `X` var mı |
| `response contains X OR Y` | | `X` veya `Y` var mı |
| `turn_count <=/</=/>=/>/==/= N` | `turn_count <= 2` | `trace.IterationCount` karşılaştır |
| `<tool_name> called` | `order_status_tool called` | `ctx.ToolsCalled` içeriyor mu |
| `<tool_name> NOT called` | | İçermiyor mu |
| `no extra tool calls` | | `actual.Count <= expected.Count` |
| `no missing_param_tool error` | | SpecialistReasoning'de validation error yok |
| `agent requests <field>` | | Response belirtilen alanı soruyor mu |
| `complaint id returned` / `order id returned` | | Regex `CMP-\d` veya `ORD-\d` |

Tanımsız pattern → `manual_review` flag'i.

---

### `ScenarioModels` — `Evaluation/ScenarioModels.cs`

YAML için DTO + sonuç modelleri:

| Tip | Alanlar |
|---|---|
| `ScenarioFile` | `Version`, `Scenarios[]` |
| `EvaluationScenario` | `Id`, `Category`, `Query`, `ExpectedIntent`, `ExpectedBehavior`, `ExpectedAgents[]`, `ExpectedTools[]`, `SuccessCriteria[]`, `KnownFailureMode` |
| `ScenarioResult` | `ScenarioId`, `PassedCriteria`, `TotalCriteria`, `Passed` (computed), `CriteriaResults[]`, `Response`, `TerminationReason`, `DetectedIntent`, `AgentsVisited`, `ToolsCalled`, `DurationMs`, `Error?` |
| `CriterionResult` | `Criterion`, `Passed`, `Evaluation` (neden), `Skipped` (skip sebebi) |
| `EvaluationRunResult` | `TotalScenarios`, `PassedScenarios`, `FailedScenarios`, `PartialScenarios`, `PassRate` (computed), `Results[]` |

---

## 7. Program & DI

### `Program.cs` — `CustomerSupportBot.Api/Program.cs`

Hexagonal mimaride `Program.cs` **composition root** rolündedir — DI kayıtlarını doğrudan değil, her katmanın kendi extension method'u üzerinden yapar:

```csharp
// Program.cs — Api katmanı DI composition root
// Her metod Api/Extensions/ içindeki wrapper'ları çağırır,
// onlar da ilgili adapter DI extension'larını çalıştırır.

// 1. Telemetri (cross-cutting — ilk kayıt)
builder.Services.AddTelemetryServices(builder.Configuration);    // → Adapters.Telemetry

// 2. AI istemcileri (sağlayıcı: OpenAI / AzureOpenAI / Anthropic)
builder.Services.AddAiServices(builder.Configuration);           // → Api/Extensions/AiServicesExtensions

// 3. Redis adaptörleri (locking + IMessageBusPort)
builder.Services.AddRedisServices(builder.Configuration);        // → Adapters.Redis

// 4. Persistence adaptörleri (Persistence:Provider'a göre Postgres / InMemory)
builder.Services.AddPersistenceServices(builder.Configuration);  // → Adapters.Persistence

// 5. Uygulama + Ajan servisleri (Application driving ports + AddAgentsAdapter dahili)
builder.Services.AddApplicationServices(builder.Configuration);  // → Application + Adapters.Agents

// 6. Auth (JWT Bearer + Admin policy)
builder.Services.AddAuthenticationServices(builder.Configuration);

// 7. Health checks
builder.Services.AddAppHealthChecks(builder.Configuration);

// Development'ta DB migration
await app.MigrateIfDevelopmentAsync();
```

Her `Add*` metodu kendi katmanının sınıflarını kaydeder:

| Extension | Kayıt edilen başlıcalar |
|---|---|
| `AddTelemetryServices` | OTLP exporter, `ICostTracker`, `ChatTelemetryMiddleware` |
| `AddAiServices` | `IChatClient`, `ReasoningChatClient`, `AiClientFactory`, `IOptions<AiProviderOptions>` |
| `AddRedisServices` | `IConnectionMultiplexer`, `IDistributedLockProvider`, `IMessageBusPort → RedisMessageBusAdapter` |
| `AddPersistenceServices` | Postgres: `PostgresSessionManager`, `PostgresApprovalQueue`, `PostgresRatingStore`, `IMessageBusPort → InMemoryMessageBusAdapter` (InMemory mod); `IOptions<PromptOptions>`, `IPromptRepository → FileSystemPromptRepository` |
| `AddApplicationServices` | 13 driving port servisi (ISessionPort, IChatPort, IApprovalPort, IEscalationPort, ...), `EntityVerifier`, `ReasoningSanityChecker`, `ReasoningService`, `EvaluationRunner`, `ContextPipeline` (IContextPipeline), `IContextProvider` × 3-4, `InputGuard`, `CustomerProfileService` (ICustomerProfileService), `SkillsBasedRouter` (ISkillsBasedRouter), `EscalationPolicyService`, `WorkflowExecutor`, `LessonMiner`, `IOptions<WorkflowGuardOptions>`, `IOptions<ParallelExecutionOptions>` — dahili olarak `AddAgentsAdapter()` çağırır |
| `AddAgentsAdapter` | `ApprovalGateService`, `CustomerSupportTeam` → `IAgentTeamPort` (IChatClient fail-fast doğrulama) |

### `IMessageBusPort` — `CustomerSupportBot.Application/Ports/Driven/Messaging/IMessageBusPort.cs`

Secondary port. Postgres adaptörlerinin Redis'e doğrudan bağımlılığını kaldıran mesajlaşma soyutlaması:

| Üye | Açıklama |
|---|---|
| `NodeId` | Çalışan pod/instance kimliği (çoklu pod senaryolarında mesaj routing için) |
| `PublishAsync(channel, payload)` | Verilen kanala mesaj yayınlar |
| `SubscribeAsync(channel, handler)` | Kanala abone olur; her mesajda `handler` çağrılır |

**Implementasyonlar**:
- `RedisMessageBusAdapter` (`CustomerSupportBot.Adapters.Redis/Messaging/`) — production Redis pub/sub
- `InMemoryMessageBusAdapter` (`CustomerSupportBot.Adapters.Persistence/InMemory/`) — geliştirme/test in-process fallback

### `PromptOptions` — `CustomerSupportBot.Adapters.Persistence/FileSystem/PromptOptions.cs`

`FileSystemPromptRepository`'nin prompt kök dizinini yapılandırma üzerinden almasını sağlar. Config section: `"Prompts"`, key: `RootPath`. Varsayılan: `AppContext.BaseDirectory/Prompts`.

---

## Çapraz referanslar

- **Agent davranışı detayı** → [adapters-agents/](adapters-agents/README.md)
- **Workflow akışı / compound query** → [domain/Model-Workflow.md](domain/Model-Workflow.md)
- **Reasoning pipeline katmanları** → [domain/Model-Reasoning.md](domain/Model-Reasoning.md)
- **Tasarım örüntüleri** → [agentic-patterns.md](agentic-patterns.md)
- **Mimari + DI + sequence diagram** → [architecture.md](architecture.md)
- **Endpoint + event şemaları** → [api/](api/README.md)
- **Güvenlik ve kimlik doğrulama** → [security.md](security.md)
- **Veritabanı ve kalıcılık** → [adapters-persistence/](adapters-persistence/README.md)
- **Telemetri ve maliyet takibi** → [adapters-telemetry/](adapters-telemetry/README.md)
- **Routing ve eskalasyon** → [application/SkillsBasedRouter.md](application/SkillsBasedRouter.md)
- **Yeni sınıf/tool/agent nasıl eklenir** → [developer-guide.md](developer-guide.md)
