# Agentic Patterns

Bu dokümanda sistemde uygulanan **agentic design pattern'leri** haritalanır. Her pattern için:

- **Adı ve kısa tanımı**
- **Sistemdeki somut gerçeklemesi**
- **İlgili dosya + satırlar**
- **Pattern'in çözdüğü problem**

## Pattern haritası (özet)

| # | Pattern | Ana uygulayıcı | Kritiklik |
|---|---|---|---|
| 1 | **Planner-Executor (Hierarchical Planning)** | PlanningAgent → Specialist | ⭐⭐⭐ |
| 2 | **Router Agent** | PlanningAgent + ChatManager | ⭐⭐⭐ |
| 3 | **ReAct (Reason + Act)** | Specialist ajanlar | ⭐⭐⭐ |
| 4 | **Structured Output (JSON Schema)** | 5+ ayrı reasoning modeli | ⭐⭐⭐ |
| 5 | **Self-Reflection** | Specialist pre/post-tool reasoning | ⭐⭐ |
| 6 | **Tool Use + Pre-Tool Validation** | Specialist `preToolCheck` | ⭐⭐⭐ |
| 7 | **Group Chat (Multi-Agent Orchestration)** | MAF `GroupChatManager` türevi | ⭐⭐⭐ |
| 8 | **Dynamic Handoff (Agent-to-Agent)** | `postToolReflection.handoffSuggestion` | ⭐⭐ |
| 9 | **Guardrails / Circuit Breaker** | `WorkflowGuardOptions` + `DetectRepeatedToolCall` | ⭐⭐⭐ |
| 10 | **Context Pipeline / Dynamic RAG** | `ContextPipeline` + `IContextProvider`'lar | ⭐⭐ |
| 11 | **Conversation Summarization** | `ConversationSummaryProvider` | ⭐⭐ |
| 12 | **Deterministic Preprocessing (Regex Entity Extraction)** | `IdExtractor` | ⭐⭐ |
| 13 | **Reasoning Trace / Observability** | `IReasoningTraceStore` | ⭐⭐ |
| 14 | **Prompt Externalization (Markdown Templates)** | `PromptService` + `Prompts/*.md` | ⭐⭐ |
| 15 | **Scenario-Based Evaluation** | `EvaluationRunner` + `docs/evaluation-scenarios.yaml` | ⭐ |
| 16 | **Grounded Reasoning (ReAct-lite Entity Verification)** | `EntityVerifier` + `VerifiedEntities` | ⭐⭐⭐ |
| 17 | **Deterministic Sanity Checking (Rule-Based Post-Validation)** | `ReasoningSanityChecker` + `IReasoningSanityRule` (8 sınıf) | ⭐⭐⭐ |
| 18 | **Task Decomposition (Compound Query)** | `ReasoningService.SubTasks` | ⭐⭐ |
| 19 | **Task Orchestration (Sequential Sub-Workflow Runs)** | `CustomerSupportTeam.RunDecomposedAsync` | ⭐⭐ |
| 20 | **Human-in-the-Loop (Approval Gate + Escalation Sink + Live Takeover + Admin Replan)** | `IApprovalQueue` + `IEscalationSink` + `IChatModeRegistry` + `IChatBridge` + `SessionState.ForceReplanNextTurn` + admin panel | ⭐⭐⭐ |

---

## 1. Planner-Executor (Hierarchical Planning)

**Tanım**: Bir "planner" ajan görev analizi yapar ve uygun "executor" ajan(lar)a delege eder. Planner tool kullanmaz, executors kullanır.

**Gerçekleme**:

- **Planner**: `PlanningAgent` — tool yok, sadece routing JSON üretir
- **Executors**: 4 specialist ajan (ProductInquiry, Order, Complaint, HumanHandoff) — hepsi tool kullanır

**Dosya**: `CustomerSupportBot.Adapters.Agents/CustomerSupportTeam.cs:60-101`

**Akış**:

```
User query → PlanningAgent (plan üret) → Specialist (tool çağır) → ResponseAgent (format)
```

**Neden?** Monolitik bir "her şeyi yapan" ajan sürdürülebilir değil. Rol ayrımı:

- Planner prompt'u kısa + karar odaklı (intent + route)
- Executor prompt'u tool şeması + parametre disiplini
- İki farklı modeli asimetrik optimize edebilirsiniz (örn. Planner için daha ucuz model)

---

## 2. Router Agent

**Tanım**: Gelen isteği N uzman arasında en uygun olana yönlendiren ajan. Intent classification ile confidence üretir.

**Gerçekleme**: Intent tespiti/confidence `ReasoningService`'in sorumluluğu (`ReasoningResult.Intent`/`ConfidenceScore`, workflow'dan önce çalışır); PlanningAgent bu nihai intent'i devralıp yalnızca `selectedAgent` + `needsClarification` kararını üretir:

```json
{
  "selectedAgent": "OrderAgent",
  "needsClarification": false,
  "alternativesRejected": [
    { "agent": "ComplaintAgent", "reason": "şikayet iması yok" }
  ]
}
```

**Dosya**: `CustomerSupportBot.Api/Prompts/agents/planning-agent.md`

**Confidence-aware routing**: Artık sayısal bir eşik yok — PlanningAgent `needsClarification=true` derse ResponseAgent'a düşer (clarification). Detay → [reasoning.md §4](reasoning.md).

**Neden?** Belirsiz bir niyeti yanlış specialist'e yönlendirmek, kullanıcının sorusunu atlamaktan daha zararlı. Düşük confidence = "emin değilim, sor".

---

## 3. ReAct (Reason + Act)

**Tanım**: LLM bir araç çağırmadan önce **bir muhakeme adımı** üretir, tool sonrası **tekrar muhakeme eder**. "Reason → Act → Observe → Reason → Act …" döngüsü.

**Gerçekleme**: Her specialist ajan, tool çağrısı çevresinde **pre-tool check** ve **post-tool reflection** üretir:

```
Reason   → preToolCheck JSON  {canProceed: true, reasoning: "...", confidence: 0.9}
Act      → order_placement_tool(001, "Dell XPS 15", 2)
Observe  → ToolResult {success: true, data: {orderId: "3"}}
Reason   → postToolReflection {status: "done", handoffSuggestion: "ResponseAgent"}
```

**Dosya**:

- Prompt'lar: `Prompts/agents/{product,order-placement,order-inquiry,complaint}-agent.md`
- Model: `CustomerSupportBot.Domain/Model/SpecialistReasoning.cs`
- Parser: `CustomerSupportBot.Domain/Services/SpecialistReasoningParser.cs`

**Neden?** Tool'un doğru/yanlış kullanıldığını sonradan kanıtlamak/eğitmek için explicit trace gerek. Ayrıca `canProceed=false` erken çıkışı, eksik parametreyle yan etkili tool çağırmayı önler.

---

## 4. Structured Output (JSON Schema)

**Tanım**: LLM serbest metin yerine **belirli bir JSON şemasına uygun** çıktı üretir; downstream kod deterministik olarak parse eder.

**Gerçekleme**: 4 ayrı seviyede structured output:

| Seviye | Şema | Üretici | Parser |
|---|---|---|---|
| Global reasoning | `ReasoningResult` | `ReasoningService` (gpt-5.4-nano) | `ReasoningService.ParseReasoning` |
| Planning | `PlanningResult` | PlanningAgent | `PlanningResultParser` |
| Specialist | `SpecialistReasoning` | 4 specialist | `SpecialistReasoningParser` |

**Robust parsing stratejisi**: Her parser önce ` ```json …``` ` fence'ini arar, sonra serbest ` ``` …``` `, sonra düz JSON ( `{...}` ) — üç fallback seviyesi. Parse başarısızsa `null` döner, downstream graceful fallback kullanır.

**Dosya**: `Services/PlanningResultParser.cs:59-88` — `ExtractJsonBlock` metodu.

**Neden?** Serbest metin LLM çıktıları işlemek "regex ile niyet çıkar" gibi kırılgan yollara iter. Structured output + tolerant parser = kontrolsüz varyasyonu yönetilebilir yapar.

---

## 5. Self-Reflection

**Tanım**: LLM'in kendi çıktısını değerlendirmesi. Tool çağrısı öncesi ve sonrası structured reasoning üreterek karar kalitesini artırır.

**Gerçekleme**: Specialist ajanlar tool çağrısı etrafında iki aşamalı reflection yapar:

**a) Pre-tool check**: Tool çağrılmadan önce `preToolCheck` JSON'u üretilir — eksik parametre varsa tool çağrılmaz.

**b) Post-tool reflection**: Tool sonrası `postToolReflection` JSON'u üretilir — `status`, `handoffSuggestion`, `summary` alanları ile sonraki adım belirlenir.

**Dosya**: `CustomerSupportBot.Api/Prompts/agents/{specialist}-agent.md`, `CustomerSupportBot.Domain/Model/SpecialistReasoning.cs`

**Neden?** Tool çağrıları yan etkili olabilir (sipariş oluşturma, şikayet kaydetme). Pre-check eksik parametreyle yan etkili çağrıyı engeller; post-reflection sonuç değerlendirmesi yapar.

---

## 6. Tool Use + Pre-Tool Validation

**Tanım**: Ajanlar function calling ile tool çağırır; çağrı öncesi parametreleri **explicit olarak** doğrular.

**Gerçekleme**:

- **Tool tanımı**: `CustomerSupportBot.Application/Services/CustomerSupportToolsService.cs` — 6 `[Description]`-attributed method.
- **Tool factory**: `AIFunctionFactory.Create(CustomerSupportTools.OrderPlacementTool)` → LLM function schema.
- **Pre-validation**: Specialist `preToolCheck.canProceed=false` ise tool hiç çağrılmaz.
- **Post-validation**: Tool `ToolResult` zarfı ile success/error ve categorized error code (validation/not_found/conflict/system) döner.

**ToolResult taksonomisi**:

```csharp
CustomerSupportBot.Domain/Model/ToolResult.cs:137-144
public static class ToolErrorCategories
{
    public const string Validation = "validation";
    public const string NotFound = "not_found";
    public const string Conflict = "conflict";
    public const string BusinessRule = "business_rule";
    public const string System = "system";
}
```

**Neden?** Yan etkili tool'lar (OrderPlacement, ComplaintRegistration) eksik parametreyle çalıştırılırsa veri bozulması olur. İki katman vardır ama **ağırlıkları eşit değildir**:

| Katman | Ne | Güvenilirlik |
|---|---|---|
| `preToolCheck` | Modelin kendi ön kontrol raporu | **Kalite sinyali** — model kendi hakkında rapor verir; atlayabilir veya yanlış doldurabilir |
| `ValidationError` / tool gövdesi | Deterministik kod | **Uygulanan sınır** — atlanamaz |

`preToolCheck` bir *executable güvenlik kapısı değildir*: LLM çıktısıdır ve LLM çıktısı bir
kontrol düzlemi olamaz. Gerçek sınırlar JWT kimliği, tool seviyesindeki sahiplik kontrolleri,
HITL onayı ve idempotency'dir. Aynı ayrım `postToolReflection` ve reasoning sanity sonuçları
için de geçerlidir — hepsi model kaynaklı kalite sinyalleridir, güvenlik kontrolü değil.

---

## 7. Group Chat (Multi-Agent Orchestration)

**Tanım**: Birden fazla ajanın paylaşılan bir konuşma üzerinde sırayla mesaj üretmesi. Bir manager bir sonraki konuşmacıyı seçer.

**Gerçekleme**: MAF `AgentWorkflowBuilder.CreateGroupChatBuilderWith(…)` + custom `CustomerSupportChatManager`:

```csharp
CustomerSupportBot.Adapters.Agents/CustomerSupportTeam.cs:121-140
```

**Dosya**: `CustomerSupportBot.Adapters.Agents/CustomerSupportChatManager.cs`

**Detay** → [CustomerSupportBot.Adapters.Agents/CustomerSupportChatManager.md](CustomerSupportBot.Adapters.Agents/CustomerSupportChatManager.md).

**Neden?** Tek bir ajan prompt'u tüm rolleri (router + 4 specialist + presenter) karıştırır → karmaşık + kırılgan. Group chat = rol ayrımı.

---

## 8. Dynamic Handoff (Agent-to-Agent)

**Tanım**: Bir specialist, kendi görevi dışında bir şey gerektiğinde başka bir specialist'e yönlendirme önerisi üretir.

**Gerçekleme**: `postToolReflection.handoffSuggestion`:

```json
{
  "postToolReflection": {
    "status": "done",
    "handoffSuggestion": "ComplaintAgent",
    "handoffReason": "Kullanıcı sipariş sonrası şikayet bildirmek istedi"
  }
}
```

`CustomerSupportChatManager` bunu görüp ilgili ajana yönlendirir. **Ping-pong guard** aktif: aynı ajana max 2 handoff:

```csharp
CustomerSupportBot.Adapters.Agents/CustomerSupportChatManager.cs:134-144
if (count < MaxHandoffsPerAgent)   // = 2
{
    _handoffCounts[targetName] = count + 1;
    return target;
}
```

**Neden?** Konuşma gerçek hayatta düz değildir: "Sipariş verdim, ama geciktiyse şikayet nasıl yapabilirim?" — OrderPlacement → Complaint handoff. Ping-pong guard iki ajanın birbirini çağırıp takılmasını önler.

---

## 9. Guardrails / Circuit Breaker

**Tanım**: Modelin sonsuz döngüye girmesini, aşırı maliyet üretmesini, duplicate tool çağırmasını veya timeout olmasını engelleyen mekanizmalar.

**Gerçekleme**: `WorkflowGuardOptions` + `CustomerSupportChatManager.ShouldTerminateAsync`:

| Guard | Değer | Kontrol | Sonuç |
|---|---|---|---|
| `TimeoutSeconds` | 60 | Linked CTS | `terminationReason=timeout` |
| `MaxIterations` | 20 | Mesaj sayısı | `max_messages_reached` |
| `MaxDuplicateToolCalls` | 3 | `DetectRepeatedToolCall` | `repeated_tool_call_guard` |
| Dinamik handoff | 2 | `_handoffCounts` | ResponseAgent'a zorla düş |

**Dosya**:

- `CustomerSupportBot.Application/Ports/Driven/WorkflowGuardOptions.cs`
- `CustomerSupportBot.Adapters.Agents/CustomerSupportChatManager.cs:204-287`

**Neden?** LLM ajansız bırakılırsa "düşünüyorum... düşünüyorum..." sonsuza kadar dönebilir. Production'da her guard = bir bütçe hattı.

---

## 10. Context Pipeline / Dynamic RAG

**Tanım**: Prompt'a dahil edilecek bağlamı **çoklu provider**'dan birleştirerek dinamik olarak inşa etmek. Klasik RAG'ın aktör tabanlı versiyonu.

**Gerçekleme**:

```csharp
CustomerSupportBot.Application/Services/ContextPipeline.cs
public class ContextPipeline
{
    // IEnumerable<IContextProvider> Order'a göre sıralı koşturulur
    public async Task<string> BuildContextAsync(AgentSession session) { … }
}
```

İki provider var:

1. **`CustomerContextProvider`** (Order=10): Repository port'ları (`IOrderRepository`, `IComplaintRepository`) üzerinden müşterinin sipariş + şikayet geçmişini çeker (ilk 5 sipariş, ilk 3 şikayet).
2. **`ConversationSummaryProvider`** (Order=5): Konuşma 8+ mesaja ulaştığında eskileri LLM ile özetler.

**Dosya**: `Services/Providers/*.cs`

Sonuç tek bir system message olarak workflow'un başına eklenir:

```
[Müşteri Bağlamı — 001]
Toplam sipariş: 3
  - 1: Dell XPS 15 x1, Durum: Kargolandı, Tarih: ...
[Konuşma Özeti]
Kullanıcı 1 hakkında daha önce...
```

**Neden?** Tüm history her prompt'a koyulursa token maliyeti patlar. Provider tabanlı yaklaşımla sadece ilgili bağlam seçilir, Order ile önceliklendirilir, yeni context türleri (ör. CRM, user preferences) kolay eklenir.

---

## 11. Conversation Summarization

**Tanım**: Uzun konuşmaları LLM ile özetleyip bağlam olarak kullanmak. Sliding window yerine recursive summarization.

**Gerçekleme**: `ConversationSummaryProvider`:

```csharp
CustomerSupportBot.Application/Services/Providers/ConversationSummaryProvider.cs:40-75
if (history.Count < SummaryThreshold) return null;        // 8+ mesaj gerek

// Son 4 mesajı atlayıp geri kalanı özetle
var oldMessages = history.Take(history.Count - RecentMessageCount);
var summary = await SummarizeAsync(oldMessages);
session.State.ConversationSummary = summary;
```

Özetleme prompt'u talimat: "Müşteri kimliği, sipariş numaraları, yapılan işlemler, çözülmemiş sorunlar — bunları koru. Max 150 kelime."

**Neden?** 30+ mesajlık bir konuşmanın hepsini her turda prompt'a vermek hem token hem dikkat dağılımı açısından verimsiz. "Özet + son 4 mesaj" formülü kritik bilgileri korur.

---

## 12. Deterministic Preprocessing (Regex Entity Extraction)

**Tanım**: LLM'e bırakmak riskli olan structured veri çıkarımını (ID'ler, tarihler, miktarlar) kodda deterministik olarak yapmak.

**Gerçekleme**: `IdExtractor`:

```csharp
CustomerSupportBot.Domain/Services/IdExtractor.cs
OrderIdPattern = \bORD[-_\s]?(\d+)\b               // 1, ORD_1
ComplaintIdPattern = \bCMP[-_\s]?(\d+)\b           // 1
CustomerIdPrefixPattern = \bCUST[-_\s]?(\d+)\b     // 001
NumericOnlyPattern = (?<![\w-])(\d{3,5})(?![\w-])  // 1990" (fallback)
```

Çıkan ID'ler hint olarak prompt'a enjekte edilir + sipariş sorgusu için tool seçim önceliği de ipucunda yer alır:

```
[ENTITY EXTRACTION — deterministik regex ile çıkarıldı]
- order_id = "1"
- customer_id = "1990"

SİPARİŞ SORGUSU ÖNCELİK KURALI:
  - order_id MEVCUT → 'order_status_tool' kullan
  - customer_id TEKRAR SORMA; order_id tek başına yeterlidir.
```

**Neden?** "1" ile "1990" yan yana geldiğinde LLM bazen customer_id'yi order_id sanabiliyor. Regex ile eşleştirme deterministik; sonra LLM'e dikte edilir.

---

## 13. Reasoning Trace / Observability

**Tanım**: Her LLM ajanlı koşumu tam izlenebilir kılmak — hangi ajan çağrıldı, hangi tool, hangi parametrelerle, sonuç ne, hangi reason ile sonlandı.

**Gerçekleme**:

- **Model**: `CustomerSupportBot.Domain/Model/ReasoningTrace.cs`
- **Store**: `InMemoryReasoningTraceStore` (ring buffer, max 500)
- **Endpoint'ler**: `/traces/recent`, `/traces/{id}`, `/traces/by-session/{sid}`, `/traces/stats`

Bir trace içeriği:

```
TraceId, SessionId, UserQuery, StartedAt, CompletedAt, DurationMs
Reasoning          (Katman 1)
Planning           (Katman 2)
SpecialistReasonings[] (Katman 3)
AgentVisits[], ToolCalls[]
TerminationReason, FinalResponse, IterationCount, Error
```

**Neden?** Production bot "neden bu yanıtı verdi?" sorusuna cevap vermek zorundadır — trace bu sorunun audit log'u. OpenTelemetry overhead'i olmadan hafif bir in-process alternatif.

---

## 14. Prompt Externalization (Markdown Templates)

**Tanım**: Prompt'ları kod içi string'lerden ayırıp harici dosyalara taşımak. Hot-swap edilebilirlik + yetkilendirme (content writer ≠ developer) sağlar.

**Gerçekleme**: `PromptService`:

```csharp
CustomerSupportBot.Adapters.Persistence/FileSystem/FileSystemPromptRepository.cs
// Prompts/**/*.md → ConcurrentDictionary<string, string>
public string Get(string key);
public string Render(string key, IDictionary<string, string?>? vars);
```

`Prompts/agents/planning-agent.md` → `_prompts.Get("agents/planning-agent")`
`Prompts/services/reasoning-system.md` → `_prompts.Render("services/reasoning-system", vars)`

`{{PLACEHOLDER}}` şablon syntax'ı regex ile ikame edilir. `README.md`/`NOTES.md` loader tarafından atlanır.

**Dosya**: Tüm prompt'lar `Prompts/` dizini altında; detay → [developer-guide.md#yeni-prompt-ekleme](developer-guide.md#yeni-prompt-ekleme).

**Neden?** Prompt iteration'ı kod iteration'ından hızlıdır. Markdown'da yapılan değişiklik için rebuild + yeniden deploy yeterli — kod değişikliği + review döngüsü gerekmez.

---

## 15. Scenario-Based Evaluation

**Tanım**: Bot'un davranışını manuel teste değil, YAML'da tanımlı senaryolara göre otomatik kriterlerle ölçmek.

**Gerçekleme**:

- **YAML**: `@docs/evaluation-scenarios.yaml` — 20+ senaryo; her birinde `query`, `expected_intent`, `expected_agents`, `expected_tools`, `success_criteria`.
- **Runner**: `@Evaluation/EvaluationRunner.cs` — her senaryoyu izole session'da koşturur, trace ile karşılaştırır.
- **Evaluator**: `CriteriaEvaluator` — criterion tipi başına pass/fail üretir.
- **Endpoint**: `POST /eval/run` (tüm senaryolar) / `POST /eval/run/{id}` (tekil)

**Senaryo örneği**:

```yaml
- id: "order-inquiry-with-id"
  category: "sipariş_sorgulama"
  query: "1 siparişim nerede?"
  expected_intent: "sipariş_sorgulama"
  expected_agents: ["PlanningAgent", "OrderAgent", "ResponseAgent"]
  expected_tools: ["order_status_tool"]
  success_criteria:
    - "response contains 'Kargolandı'"
```

**Neden?** Prompt değişikliği yaptığınızda bot 5 regression gösterebilir — manuel test bulmaz. YAML senaryoları + CI entegrasyonu = "prompt update önce eval koş".

---

## 16. Grounded Reasoning (ReAct-lite Entity Verification)

**Tanım**: Reasoning modeli karar vermeden **önce** kritik entity'ler deterministik kodla extract edilir ve external kaynak (DB) ile doğrulanır. Model "bu ID var mı var olmadı mı?" tahmininde bulunmaz — gerçekleği dayatırız.

**Gerçekleme**: `EntityVerifier`:

```csharp
CustomerSupportBot.Application/Services/EntityVerifier.cs
public VerifiedEntities Verify(string query, AgentSession? session, IList<ChatMessage>? history)
{
    var ids = IdExtractor.Extract(query);        // regex
    // history + session state'ten eksikleri doldur
    // Her entity için repository port'ları (IOrderRepository.Get / IProductCatalogRepository.FindProduct / IComplaintRepository.Get)
    //   → Verified / NotFoundInDb / FormatOnly
    // customer_id Verified ise DerivedLastOrderId hesaplanır
    return verified;
}
```

**Reasoning prompt'una enjeksiyon** (`Prompts/services/reasoning-system.md` üzerinden):

```
[VERIFIED ENTITIES — session/DB ile doğrulandı]
Aşağıdaki bilgiler ZATEN elinizde. requiredInfo'ya EKLEMEYİN.
- order_id = "1" [VERIFIED, status=Kargolandı, product=Dell XPS 15]
- customer_id = "1990" [VERIFIED, has_orders=true]
- last_order_id = "1" [derived]
```

**Dosya**:

- `CustomerSupportBot.Domain/Model/VerifiedEntities.cs`
- `CustomerSupportBot.Application/Services/EntityVerifier.cs`

**Literatürdeki yeri**: Klasik [ReAct](https://arxiv.org/abs/2210.03629)'ın "Observe" adımı normalde model tarafından tool çağrısıyla yapılır. Biz bu adımı **LLM'den önce, kodda deterministik** yapıyoruz — sıfır latency + sıfır LLM maliyeti. Bu yaklaşım *grounded prompting* veya *entity grounding* olarak da anılır.

**Neden?** Model *"9999 bulunabilir"* diye halusine ederse kullanıcı *"siparişiniz kargoda"* gibi yanlış yanıt alabilir. Verified bir entity → güvenli karar zemini.

---

## 17. Deterministic Sanity Checking (Rule-Based Post-Validation)

**Tanım**: LLM çıktısını **ikinci bir LLM ile** değil, **kodda kural tabanlı** tarayarak mantık tutarsızlıklarını yakalama. Sıfır ekstra API maliyeti, mikrosaniye latency.

**Gerçekleme**: `ReasoningSanityChecker` — **Strategy pattern** + 8 kural sınıfı (her biri `IReasoningSanityRule` implement eder):

| # | Kural sınıfı | `Code` | Severity | Tetiklenme |
|---|---|---|---|---|
| 1 | `OverconfidentClarificationRule` | `overconfident_clarification` | warn | `confidenceScore >= 0.7` AMA nextAction clarification istiyor |
| 2 | `RedundantRequiredInfoRule` | `redundant_required_info` | **error** | `requiredInfo`'da VERIFIED entity var (ping-pong riski) |
| 3 | `IntentActionMismatchRule` | `intent_action_mismatch` | warn | Intent ile seçilen agent çelişiyor |
| 4 | `LowConfidenceNoMissingRule` | `low_confidence_no_missing` | info | `confidenceScore < 0.5` ama requiredInfo boş |
| 5 | `AssumptionHeavyStepsRule` | `assumption_based_step` | info | Bir step'te `grounding=assumption` |
| 6 | `OverconfidentAssumptionsRule` | `overconfident_assumptions` | warn | `confidenceScore >= 0.8` ama 3+ assumption |
| 7 | `NotFoundIgnoredRule` | `not_found_ignored` | **error** | NOT_FOUND_IN_DB entity var ama nextAction doğrulatmıyor |
| 8 | `SubTasksIgnoredRule` | `subtasks_ignored` | warn | 2+ subTask var ama nextAction hepsinden bahsetmiyor |

`ReasoningSanityChecker.Check` constructor'da kayıtlı `_rules` listesini sırayla iterate eder. Bir kural exception fırlatırsa loglanır ve sonraki kural çalışır — fail-soft. Yeni kural eklemek için sadece yeni `IReasoningSanityRule` sınıfı yaz ve `_rules` listesine ekle (bkz. [developer-guide.md](developer-guide.md#yeni-sanity-checker-kural%C4%B1-ekleme)).

**Dosya**:

- `CustomerSupportBot.Application/Services/ReasoningSanityChecker.cs`
- `CustomerSupportBot.Domain/Model/ReasoningIssue.cs`

Tespit edilen her issue `result.SanityIssues` listesine eklenir, trace'e yazılır ve UI'da gösterilir. Şu an **bilgilendirme modunda** — workflow akışını değiştirmez; ileride error severity'de LLM re-prompt tetikleyebilir.

**Neden?** Self-consistency (N kez LLM çağırıp oyla) 5-40x maliyet getirir. Çoğu tutarsızlık *basit mantık hataları*dır (VERIFIED entity'yi requiredInfo'ya ekleme vb.). Bunlar için LLM'ye gerek yok — kurallar yeterli.

---

## 18. Task Decomposition (Compound Query)

**Tanım**: Birden fazla bağımsız işlem isteyen kullanıcı sorgusunu, reasoning model tarafından **alt görevler listesine** ayırtırma. Her alt görev kendi intent'i, hedef agent'ı ve entity'leri ile anotlu.

**Gerçekleme**: `ReasoningService` çıktısında `subTasks[]` alanı:

```json
"subTasks": [
  { "order": 1, "intent": "sipariş_sorgulama", "description": "1 için durum sorgula",
    "targetAgent": "OrderAgent", "entities": { "order_id": "1" }, "dependencies": [] },
  { "order": 2, "intent": "şikayet", "description": "2 için şikayet aç",
    "targetAgent": "ComplaintAgent", "entities": { "order_id": "2" }, "dependencies": [] }
]
```

**Decomposition kuralları** (`Prompts/services/reasoning-system.md`):

- Query tek niyetliyse `subTasks: []` (boş).
- *" ve "*, *"sonra"*, *"ayrıca"*, iki farklı ID → decompose et.
- Aynı niyet çoklu parametre (*"1 ve 2'nin durumu"*) → decompose **etme**, tek görev.
- `targetAgent` mutlaka 4 specialist'ten biri.

**Dosya**:

- `CustomerSupportBot.Domain/Model/SubTask.cs`
- `CustomerSupportBot.Api/Prompts/services/reasoning-system.md` (decomposition bölümü)

**Neden?** Tek intent'li routing compound query'lerde ikinci istek kaybına sebep olur. Decomposition ile her istek isimlendirilir, hedeflenir ve **Pattern 19 (Task Orchestration)** tarafından paralel/sıralı yürütülür.

---

## 19. Task Orchestration (Parallel/Sequential Sub-Workflow Runs)

**Tanım**: Alt görevlerin her birini ayrı bir alt-workflow run'ı olarak yürütme ve sonuçları birleştirme. Kod katmanında "partition → parallel/sequential group runs → aggregate" deseni.

**Gerçekleme**: `DecomposedRunner.RunDecomposedAsync` / `RunDecomposedStreamingAsync`:

```csharp
// CustomerSupportTeam.cs — delegasyon
if (ShouldDecompose(reasoning))
    return await _decomposed.RunDecomposedAsync(query, history, session, reasoning!, ct);

// DecomposedRunner.cs — orkestrasyon
public async Task<string> RunDecomposedAsync(...)
{
    var groups = SubTaskOrchestrator.Partition(reasoning.SubTasks, _parallelOptions);
    foreach (var group in groups)  // paralel veya sıralı
    {
        if (group.IsParallel)
            await Task.WhenAll(group.Tasks.Select(t => RunSubTask(t, ...)));
        else
            foreach (var task in group.Tasks)
                await RunSubTask(task, ...);
    }
    return SubTaskOrchestrator.AggregateSubTaskResults(collected);
}
```

**Dosyalar**:
- `CustomerSupportBot.Adapters.Agents/DecomposedRunner.cs` — compound query orkestratörü
- `CustomerSupportBot.Adapters.Agents/WorkflowRunner.cs` — tekil workflow yürütücüsü
- `CustomerSupportBot.Application/Services/Reasoning/SubTaskOrchestrator.cs` — gruplama + sonuç birleştirme
- `CustomerSupportBot.Application/Ports/Outbound/ParallelExecutionOptions.cs` — yapılandırma

**Karakteristikleri**:

- **Paralel/sıralı gruplama** — yalnızca tüm tool'ları salt-okunur ajanlar paralel çalışır. Karma tool setli `OrderAgent`, LLM intent'i read-only olsa da sıralıdır.
- **Paralel eşzamanlılık sınırı** — `SemaphoreSlim` ile `ParallelExecutionOptions.MaxDegreeOfParallelism` kadar task aynı anda çalışır.
- **Fan-out ve süre sınırı** — `MaxSubTasks` plan genişliğini, `TimeoutSeconds` bütün compound koşusunun ortak zaman bütçesini sınırlar.
- **İzolasyon/continuity** — ana compound sorgu kardeş workflow'lara taşınmaz; yalnızca açıkça bildirilen `Dependencies` sonuçları ilgili alt görevin history'sine eklenir.
- **Hedef kısıtı** — her türetilen reasoning `ConstrainedTargetAgent` taşır; planning başka specialist seçerse chat manager doğrulanmış hedefi uygular, reflection ile kardeş specialist'e handoff engellenir.
- **Recursion-safe** — derived reasoning'in `SubTasks=[]` olması sonsuz döngüyü engeller.
- **Streaming uyumlu** — sıralı grupta gerçek token akışı iletilir; paralel batch sonuçları batch tamamlandıktan sonra deterministik `sub.Order` sırasıyla yayınlanır.

**Neden?** MAF `GroupChatManager.SelectNextAgentAsync` bir turda tek next speaker seçer. Compound query için bu yetersiz. Kod katmanında orkestrasyon = *"N ayrı ama ilişkili konuşma = N workflow run"* modeli. Detay → [CustomerSupportBot.Adapters.Agents/CustomerSupportTeam.md](CustomerSupportBot.Adapters.Agents/CustomerSupportTeam.md).

---

## 20. Human-in-the-Loop (Approval Gate + Escalation Sink)

**Tanım**: Yüksek riskli veya belirsizlik içeren otonom kararları **insan onayına** veya **insan çözümüne** yönlendirmek. Tam otomatik akışı kısmen durdurup bir kişinin müdahalesini bekleme.

Bu sistemde **iki farklı HITL mekanizması** vardır ve birbirlerini tamamlar:

### 20.1 Synchronous Approval Gate

**Nerede**: Yan etkili tool çağrıları öncesi (`order_placement_tool`, `complaint_registration_tool`).

**Akış**:

```
┌──────────────────────────────────────────────────────────────┐
│ 1. Specialist agent preToolCheck.canProceed=true üretti      │
│    → MAF tool invocation başlatıyor                          │
├──────────────────────────────────────────────────────────────┤
│ 2. ApprovalGateService.BuildOrderPlacementTool() lambda'sı   │
│    aracın gerçek çalışmasından ÖNCE approval request yazar   │
├──────────────────────────────────────────────────────────────┤
│ 3. IApprovalQueue.CreateAsync(req)                            │
│    → event: RequestCreated                                    │
│    → SSE: approval_required payload'ı client'a                │
├──────────────────────────────────────────────────────────────┤
│ 4. Tool lambda HEMEN döner: ToolResult.Pending(...)           │
│    → "Talebiniz onaya gönderildi"                             │
│    → KULLANICININ TURU BURADA BİTER, beklemez                 │
└──────────────────────────────────────────────────────────────┘

            ⋯ saniyeler, saatler — kullanıcı sohbete devam eder ⋯

┌──────────────────────────────────────────────────────────────┐
│ 5. Admin /approvals/pending'i çeker, onaylar veya reddeder    │
├──────────────────────────────────────────────────────────────┤
│ 6. IApprovalQueue.DecideAsync(id, approved, by, reason)       │
│    → ClaimDecisionAsync: UPDATE ... WHERE status='Pending'    │
│      (0 satır ⇒ kararı başkası verdi, yürütme ATLANIR)        │
│    → status=Approved, executionStatus=Running                 │
├──────────────────────────────────────────────────────────────┤
│ 7. IApprovalExecutionRouter GERÇEK işi burada çalıştırır      │
│    - approved → _tools.OrderPlacementTool(...)                │
│    - rejected → yürütme yok                                   │
│    → executionStatus = Succeeded | Failed                     │
├──────────────────────────────────────────────────────────────┤
│ 8. → event: RequestDecided                                    │
│    → SSE: approval_resolved payload'ı client'a                │
│    → client bağlı değilse: unseen listesinde birikir,         │
│      kullanıcı döndüğünde badge olarak görür                  │
└──────────────────────────────────────────────────────────────┘
```

> **Neden bloklamıyor:** Eskiden 4. adımda tool lambda'sı `AwaitDecisionAsync` ile admin kararını
> aynı HTTP isteği içinde bekliyordu (varsayılan 60 sn). 50-100 eşzamanlı onay biriktiğinde bir
> admin bu süreye yetişemediği için istekler sessizce otomatik reddedilip kullanıcıya yanlış
> sonuç dönüyordu. Şimdi bekleyen yok; talep admin karar verene ya da `StalePendingHours`
> (72 saat) dolana kadar kalıcı olarak kuyrukta durur.

**Gerçekleme**:

- Queue: `Adapters.Persistence/Postgres/PostgresApprovalQueue.cs` (üretim; `InMemoryApprovalQueue` tek-süreç/test için)
- Tool wrapper: `Adapters.Agents/ApprovalGateService.cs` (`BuildOrderPlacementTool`, `ExecuteWithApprovalGateAsync`)
- Ajana bağlama: `Adapters.Agents/Team/OrderAgent.cs`, `ComplaintAgent.cs`
- Karar sonrası yürütme: `Application/Services/Approval/ApprovalExecutionRouter.cs`
- Süresi geçenler: `Adapters.Persistence/EfCore/StaleApprovalSweepService.cs`
- Config: `Application/Ports/Outbound/ApprovalOptions.cs` (`appsettings.json > "HumanInTheLoop"`)
- Endpoints: `/approvals/pending`, `/approvals/{id}/approve`, `/approvals/{id}/reject`,
  `/chat-sessions/{sessionId}/approvals/unseen`, `/customer/approvals/history`
- UI: `Web/Pages/Admin.razor` (admin), `Web/Pages/Chat.razor` (müşteri bildirim/badge)

**Context propagation** — tool lambda'sı session/trace/query/customerId bağlamını
`AsyncLocal<ApprovalContext>` üzerinden alır (`IApprovalContextAccessor`). `customerId` LLM'e
parametre olarak açılmaz; JWT claim'inden gelir.

**Zaman aşımı davranışı**: `StalePendingHours` (varsayılan 72 saat) aşılan `Pending` kayıtlar
`StaleApprovalSweepService` tarafından otomatik **reddedilir**. `TimeoutSeconds` ve
`AutoApproveOnTimeout` bu tool'lar için **uygulanmaz** — yalnızca eski bloklayan yolun ayarlarıydı.

**Karar ≠ yürütme**: `Status` admin'in kararı, `ExecutionStatus` o kararın hayata geçip geçmediği.
`Approved + Running`'de kalmış bir kayıt yürütme sırasında sürecin kapandığını gösterir; sistem
tekrar denemez (tool'lar idempotent değil), admin panelinde uyarıyla işaretlenir.

### 20.2 Asynchronous Escalation Sink

**Nerede**: Specialist'in `postToolReflection.status = "needs_escalation"` döndürdüğü senaryolar (ödeme/iade/sistem sorunu vb.).

**Akış**:

```
workflow sonu
    │
    ▼
EmitEscalationsIfAny(trace, userQuery, finalResponse)
    │
    ▼
trace.SpecialistReasonings
  .Where(sr => sr.PostToolReflection?.Status == "needs_escalation")
    │
    ▼
IEscalationSink.Create(new EscalationRequest { ... })
    │
    ├── event: RequestCreated → SSE: escalation_created
    │
    └── Admin UI /escalations/open listesinde görünür
        │
        ▼
    Admin: acknowledge → resolve → (opsiyonel: dismiss)
```

**Gerçekleme**:

- Sink: `CustomerSupportBot.Adapters.Persistence/InMemory/InMemoryEscalationSink.cs` — ring buffer (max 500)
- Hook: `CustomerSupportBot.Adapters.Agents/CustomerSupportTeam.cs` → `EmitEscalationsIfAny(...)` trace completion öncesi çağrılır
- Endpoints: `/escalations/open`, `/escalations/{id}/acknowledge`, `/escalations/{id}/resolve`, `/escalations/{id}/dismiss`

**Senkron vs asenkron ayrımı** — critical:

| Mekanizma | Mod | Kullanıcı bekletilir mi? | Çözüm zamanı |
|---|---|---|---|
| **Approval Gate** | Senkron | ✅ Evet — tool execute olmaz | Saniyeler (SLA önemli) |
| **Escalation Sink** | Asenkron | ❌ Hayır — yanıt kullanıcıya döner | Dakikalar/saatler |

**Karakteristikleri**:

- **Feature flag** — `ApprovalOptions.Enabled=false` olursa bypass edilir; eski davranış korunur.
- **Per-tool granularity** — `ToolsRequiringApproval` listesinde değilse tool bypass.
- **Session-scoped SSE filtresi** — event'ler global yayılır ama ChatEndpoints sadece kendi session'ına ait olanları client'a iletir.
- **Unit-test edilebilir** — Queue/Sink interface'leri fake/mock ile değiştirilebilir.
- **Production-ready değişim noktası** — `InMemory*` impl'ler Redis/PostgreSQL/Zendesk adapter ile değiştirilebilir (ring buffer + TCS semantiği korunmalı).

**Neden iki ayrı mekanizma?**

- Approval gate **yanlış/zararlı tool çağrılarını** engeller (lock + control). Örnek: LLM'in halüsinasyon yüzünden yanlış customer_id ile sipariş yaratmasını durdurur.
- Escalation sink **bot'un gerçekten çözemeyeceği durumları** bir insana aktarır. Örnek: LLM ödeme sistemi arızası tespit etti ama fix yetkisi yok.

Birlikte kullanıldıklarında **production-grade HITL** elde edilir: proaktif koruma (approval) + reaktif çözüm (escalation).

### 20.3 Live Human Takeover (real-time agent handover)

**Nerede**: Admin bir eskalasyon kartında **"Devral & Sohbet"** tıkladığında ya da `POST /chat-sessions/{sid}/takeover` çağırdığında.

**Mantık**: Eskalasyon "bilet açma" idi (offline), bu ise **canlı transfer**. Session bir kip sahibi olur:

```
ChatMode.Bot   ──takeover──▶  ChatMode.Human  ──release──▶  ChatMode.Bot
```

`CustomerSupportBot.Api/Endpoints/ChatEndpoints.cs:90-96` → her `/chat/stream` isteğinde önce `IChatModeRegistry.GetMode(sessionId)` okunur:

- **Bot** → mevcut workflow (reasoning + agent + response)
- **Human** → workflow **tamamen atlanır**, mesaj `IChatBridge`'e push edilir, admin yanıtı stream'lenir

**Bileşenler**:

| Tip | Sorumluluk |
|---|---|
| `IChatModeRegistry` | Session başına `(Mode, HumanAgent, EnteredAt, MessageCount)` snapshot. `TakeOver()` / `Release()` + `ModeChanged` event. |
| `IChatBridge` | Per-session `Channel<ChatBridgeMessage>` pub/sub (toAdmin + toUser) + ring-buffer history (200 msg). |
| `ChatBridgeMessage` | `{Sender: User\|Bot\|Admin\|System, Text, HumanAgent?, Timestamp}` |
| Admin endpoint'leri | `/chat-sessions/{active,/{sid}/state,/history,/takeover,/release,/messages,/subscribe}` |
| SSE event'leri | `human_joined`, `human_message`, `human_left` (user-side); `bridge_message` (admin-side) |

**Çağrı akışı**:

```
                  ┌─── ChatEndpoints (/chat/stream) ───┐
   USER ──text──▶ │ if mode == Human:                  │ ──► bridge.PublishUserMessage
                  │   bridge.SubscribeToUser stream    │ ◀── bridge.PublishAdminMessage
                  └────────────────────────────────────┘            ▲
                                                                    │
                  ┌─── AdminEndpoints (/chat-sessions/{sid}) ───┐   │
  ADMIN ──takeover──▶ registry.TakeOver(sid, agent)             │   │
        ──text──▶     bridge.PublishAdminMessage(sid, agent, t) │───┘
        ◀──bridge_message── bridge.SubscribeToAdmin stream      │
        ──release──▶  registry.Release(sid)                     │
                  └─────────────────────────────────────────────┘
```

**Bot history → admin context**: Bot moddayken her tur sonunda `chatBridge.RecordBotExchange()` çağrılır (`CustomerSupportBot.Api/Endpoints/ChatEndpoints.cs:202-204`). Admin "Devral" deyince son 200 mesajlık tam bağlam (bot + user) panele yüklenir — temsilci sıfırdan başlamaz.

**Mod değişim kanalı**: `ModeChanged` event'i, açık duran user `/chat/stream` SSE bağlantısını da koparır → `human_left` + `done` gönderilir, akış normal Bot moduna düşer (bir sonraki user mesajı tekrar workflow tetikler).

**Admin mesajının session geçmişine yazılması**: `POST /chat-sessions/{sid}/messages` artık `ISessionManager.AppendAssistantMessage()` ile admin mesajını LLM-facing session history'sine **assistant turu** olarak yazıyor. Bot moduna dönüldüğünde (release veya replan) `PlanningAgent` admin'in vaatlerini/yönlendirmelerini görür ve onları göz ardı etmez. `ISessionManager` ve `InMemorySessionManager` her ikisi de "son boş assistant turunu doldur, yoksa yeni tur ekle" mantığını uygular.

**Karakteristikleri**:

- **In-memory** — restart kaybı kaçınılmaz; production için Redis Streams + persistent state
- **Tek admin desteği** — bridge channel'ları çoklu abonenin tümüne broadcast eder ama admin UI'sı tek panel açıyor (multi-admin için coordination eksik)
- **Auth yok** — `/chat-sessions/*` route'larında da admin auth gerekli
- **Reconnect yok (UI)** — admin sayfası refresh edilirse panel sıfırlanır, açıkça yeniden takeover yapmaya gerek yok ama sohbet panelini elle açmak gerekir
- **Tek yönlü kapatma** — sadece admin "Bitir"e tıklayabilir; user "ben artık konuşmak istiyorum bitir" diyemez

**Uygulanmayan yönler** (gelecek geliştirmeler):

- **Multi-admin coordination** — birden fazla admin aynı session'a eş zamanlı bakıyor mu? "X yazıyor…" presence belirteci.
- **User-initiated release** — chat'te "Bitir" butonu (user kendi söylesin "tamam, teşekkürler").
- **Admin mesajlarının session history'ye yazılması** — ✅ uygulandı (`AppendAssistantMessage`).
- **Push notification** — admin sayfası açık değilken yeni eskalasyon → email/Slack/desktop bildirim.
- **SLA + öncelik** — high-priority kuyruk + max bekleme süresi.
- **Çoklu instance** — `Channel<T>` aynı process içinde; çoklu pod için Redis pub/sub veya SignalR backplane gerekir.

**Uygulanmayan yönler** (eski Approval + Escalation parçası için):

- **Supervisor review kuyruğu** — düşük confidence yanıtları kullanıcıya gösterilmeden önce insan moderator'e gösterme. Gerektirir: yanıt ertelemesi + ayrı kuyruk. Faydası: ciddi hallucination kontrolü.
- **User feedback (thumbs up/down)** — post-hoc HITL, RLHF datası toplamak için. Trivial eklenebilir (`POST /feedback`).
- **Admin authentication** — şu an `/approvals/*`, `/escalations/*` ve `/chat-sessions/*` endpoint'lerinde **hiçbir auth yok**. Production için JWT/role-based auth middleware eklenmeli.

**Referans dokümantasyon**: [CustomerSupportBot.Api/Endpoints-Admin.md](CustomerSupportBot.Api/Endpoints-Admin.md).

### 20.4 Admin Replan (one-shot planning override + auto bot turn)

**Nerede**: Admin eskalasyon kartında veya aktif sohbet panelinde **"🔄 Yeniden Planla"** butonuna basar; opsiyonel bot-içi not yazabilir.

**Endpoint'ler**:
- `POST /escalations/{id}/replan` — eskalasyon bağlamında
- `POST /chat-sessions/{sid}/replan` — aktif sohbet panelinde (eskalasyon şart değil)
- Body: `{ requestedBy?, note? }` (`ReplanInput`)

**Mantık**: Bot önceki turlarda yanlış agent'a takılmış olabilir veya admin canlı sohbet sonrasında yönü değiştirmek isteyebilir. Replan, **bir sonraki bot turunda** PlanningAgent'a "önceki TOOL ÇAĞRILARINI geçersiz say" hint'ini iletir; admin/müşteri diyalog bağlamı korunur. Note opsiyoneldir ve **yalnızca PlanningAgent'a** görünür; müşteri sadece nazik bir yönlendirme bildirimi alır.

**Bileşenler**:

| Tip | Sorumluluk |
|---|---|
| `SessionState.ForceReplanNextTurn` | One-shot bool flag — `BuildWorkflowMessagesAsync`'te görülür ve tüketildikten sonra `false`. |
| `SessionState.ReplanNote` | One-shot opsiyonel string — PlanningAgent system hint'inin sonuna eklenir, sonra `null`. |
| `WellKnown.FallbackMessages.ReplanPlanningHint` | "🔄 ADMIN OVERRIDE — önceki TOOL ÇAĞRILARINI ve specialist agent kararlarını geçersiz say…" sabit metin. |
| `WellKnown.FallbackMessages.ReplanCustomerNotice` | "ℹ️ Talebinizi tekrar değerlendiriyoruz…" müşteri bildirimi. |
| `IChatBridge.PublishBotTyping(sid, on)` | Transient typing-indicator sinyali (history'ye yazılmaz). |
| `IChatBridge.PublishBotMessage(sid, text)` | Otomatik bot yanıtını müşteriye `human_message` (`from="bot"`) olarak push'lar. |
| `AdminEndpoints.RunReplanBotTurnAsync` | Fire-and-forget arka plan turu — reasoning + workflow + bridge yayını. |

**Akış**:

```
  ADMIN ──replan──▶ /chat-sessions/{sid}/replan (note?)
                       │
                       ├── state.ForceReplanNextTurn = true
                       ├── state.ReplanNote = note
                       ├── escalations[sid].Decide(Resolve, resolution=note??default)
                       ├── if Mode==Human: registry.Release(sid)            ──▶ human_left
                       ├── bridge.PublishSystemMessage(ReplanCustomerNotice) ──▶ human_message(system)
                       └── _ = RunReplanBotTurnAsync(...)        ◀──── fire-and-forget
                                ├── lastUserQuery = history.LastOrDefault(role=user)
                                ├── bridge.PublishBotTyping(true)            ──▶ bot_typing(on)
                                ├── reasoningService.ReasonAsync(...)
                                ├── team.RunAsync(...)
                                │     └── BuildWorkflowMessagesAsync
                                │           if state.ForceReplanNextTurn:
                                │              messages.prepend(ReplanPlanningHint + "📌 Admin notu: ...")
                                │              state.ForceReplanNextTurn = false
                                │              state.ReplanNote = null
                                ├── sessions.AppendAssistantMessage(response)
                                ├── bridge.PublishBotMessage(response)       ──▶ human_message(bot)
                                └── bridge.PublishBotTyping(false)           ──▶ bot_typing(off)
```

**Müşteri UX**: yeşil temsilci bandı kalkar → sistem notu → typing indicator (input kilitli) → bot baloncuğu → (4+ mesaj eşiği sağlandıysa) puanlama widget'ı. Müşteri admin notunu hiçbir zaman görmez.

**Karakteristikleri**:
- **One-shot** — flag + note tek bir sonraki bot turunda kullanılıp temizlenir; replan etkisinin sızıntısı yok.
- **Audit koruması** — `SessionState.ReplanRequestedBy/At` gözüken her trace'te kalır; eskalasyon `resolution` alanına not yazılır.
- **Diyalog koruması** — `ReplanPlanningHint` sadece **tool çağrılarını** geçersiz sayar, admin/müşteri konuşma bağlamını korur (vaadleri unutmaz).
- **Idempotent değil** — admin peş peşe iki kere basarsa iki bot turu tetiklenir; UI confirm modal'ı ile kısıtlanıyor.

---

## Uygulanmayan pattern'ler ve nedenleri

Bazı pattern'leri **bilinçli olarak uygulamadık**. Bunları listelemek, hangi durumda uygulanabileceklerini de dokumanda bırakıyor.

### Self-Consistency (ensemble voting)

> *"Aynı soruyu N kez `temperature>0` ile sor, cevapları oyla"* — Wang et al. 2022.

- **Avantaj**: Matematik/mantık benchmark'larında %5-15 doğruluk artışı.
- **Dezavantaj**: 5-40x reasoning LLM çağrısı maliyeti.
- **Biz neden uygulamadık?** Bizim reasoning çıktımız *routing + requiredInfo* gibi düşük varyasyonlu ayrık çıkışlar. Intent tespit doğruluğu zaten %90+. 5x maliyet için marjinal iyileşme ekonomik değil. Ayrıca *Pattern 17 (Sanity Checker)* aynı hataların çoğunu sıfır maliyetle yakalıyor.
- **Ne zaman ekleriz?** Intent spaces genişlerse (50+ ajan) veya routing hatası maliyeti çok yüksek olursa (gerçek ticaret kararları vb.).

### Tree-of-Thoughts (ToT)

> *"Her adımda k farklı düşünce üret, evaluate, BFS/DFS ile en iyi yolu takip et"* — Yao et al. 2023.

- **Avantaj**: Game of 24, crosswords, plan arama gibi kombinatoryel problemlerde çok yüksek iyileşme.
- **Dezavantaj**: d=4, k=3, b=5 ile **60 LLM çağrısı** / query.
- **Biz neden uygulamadık?** Müşteri destek rotalaması dar (4 specialist), derinliği sığ (2-3 adım). Orada *Pattern 1 (Planner-Executor) + Pattern 2 (Router)* yeterli. ToT fayda-maliyet oranı ters.
- **Ne zaman ekleriz?** Otonom ajan planlama, kod üretimi, karışık data analiz gibi gerçek "arama" gereken görevlerde.

### Reflexion (memory-based iterative refinement)

> *"Hatalardan öğren ve sonraki attempt'te bunları hatırla"* — Shinn et al. 2023.

- **Avantaj**: Zamanla görev performansı artar.
- **Biz neden uygulamadık?** *Pattern 13 (Reasoning Trace)* zaten her koşunun kayıt alıyor; bu trace'leri sonraki run'larda **otomatik feedback** olarak kullanmıyoruz. Manuel olarak prompt'u güncellemek (eval tabanlı iterasyon) daha şeffaf + kontrol edilebilir bulduk.
- **Ne zaman ekleriz?** Prompt iteration hızı yetersiz kalırsa (günde 100+ hata paternı).

### Multi-Model Cascading

> *"Ucuz modelle dene, güvensizsen pahalı modele geç"*.

- **Avantaj**: Ortalama maliyet düşer.
- **Biz neden uygulamadık?** Zaten *iki model kullanıyoruz* (gpt-5.4-nano reasoning + gpt-5.4 chat) ama bu **rol bazlı** ayrım, güven bazlı cascading değil. Cascading için her LLM çağrısının **confidence output**'u olmalı ve routing güncellenmeli. Karmaşıklık/getiri oranı düşük.
- **Ne zaman ekleriz?** Çok düşük latency bir ticari satış noktası olursa.

### Parallel Sub-Task Execution

> *Compound query'nin paralel varyantı: bağımsız alt görevleri aynı anda çalıştır.*

- **Biz neden uygulamadık?** Mevcut helper'lar sıralı (`foreach`). `SubTask.Dependencies` alanı var ama kullanılmıyor. Paralele geçmek için:
  - Topolojik sıralama
  - `Task.WhenAll` ile bağımsız subtask'leri tetikleme
  - `InMemoryProductCatalogAdapter`, `InMemoryOrderAdapter`, `InMemoryComplaintAdapter` lock'larının race condition'a dayanıklı olduğundan emin olma
- **Ne zaman ekleriz?** Ortalama subtask başına latency yüksek olur (gerçek DB + external API çağrısı) ve paralel kazanç belirginleşirse.

---

## Pattern'lerin birbirleriyle ilişkisi

```
 ┌─────────────────────────────────────────┐
 │ Deterministic Preprocessing             │◀─── Entity extraction (regex)
 └────────────────────┬────────────────────┘
                      │ hint
                      ▼
 ┌─────────────────────────────────────────┐
 │ Grounded Reasoning (Entity Verify) │◄─── Repository port lookup
 │                              — Katman 0 │
 └────────────────────┬────────────────────┘
                      │ verified entities
                      ▼
 ┌─────────────────────────────────────────┐
 │ Context Pipeline + Summarization (RAG)  │◀─── Session state
 └────────────────────┬────────────────────┘
                      │ context
                      ▼
 ┌─────────────────────────────────────────┐
 │ Global Reasoning (Structured Output     │
 │ + Steps + SubTasks)          — Katman 1 │
 └────────────────────┬────────────────────┘
                      │ raw reasoning
                      ▼
 ┌─────────────────────────────────────────┐
 │ Sanity Checker (8 deterministic rules)  │
 │                            — Katman 1.5 │
 └────────────────────┬────────────────────┘
                      │ hint + issues
                      ▼
 ┌─────────────────────────────────────────┐
 │ subTasks≥2 → Task Decomposition         │
 │              + Orchestration             │
 │ (her alt görev → recursive workflow run)│
 └────────────────────┬────────────────────┘
                      ▼
 ┌─────────────────────────────────────────┐
 │ Router (Structured Output + Confidence) │
 │ PlanningAgent                — Katman 2 │
 └────────────────────┬────────────────────┘
                      │ selectedAgent
                      ▼
 ┌─────────────────────────────────────────┐
 │ Group Chat + Dynamic Handoff + Guards   │
 │ ┌─────────────────────────────────────┐ │
 │ │ ReAct + Pre-Tool Validation +       │ │
 │ │ Tool Use                 — Katman 3 │ │
 │ └─────────────────────────────────────┘ │
 └────────────────────┬────────────────────┘
                      ▼
              Final response to user
              (compound ise:
               JoinAggregatedParts ile birleştirildi)
                      │
                      ▼
 ┌─────────────────────────────────────────┐
 │ Observability (Reasoning Trace)         │
 │ Scenario-Based Evaluation               │
 └─────────────────────────────────────────┘

 + Cross-cutting:
   - Prompt Externalization (tüm prompt'lar MD'den)
   - Structured Output (her katmanda JSON şema)
```

## Hangi pattern nerede görülür? Hızlı lookup

| Pattern | Dosyalar |
|---|---|
| Planner-Executor, Router | `CustomerSupportBot.Api/Prompts/agents/planning-agent.md`, `CustomerSupportBot.Domain/Model/PlanningResult.cs`, `CustomerSupportBot.Domain/Services/PlanningResultParser.cs` |
| ReAct | `CustomerSupportBot.Api/Prompts/agents/{product,order-placement,order-inquiry,complaint}-agent.md`, `CustomerSupportBot.Domain/Model/SpecialistReasoning.cs`, `CustomerSupportBot.Domain/Services/SpecialistReasoningParser.cs` |
| Self-Reflection | `CustomerSupportBot.Api/Prompts/agents/{specialist}-agent.md`, `CustomerSupportBot.Domain/Model/SpecialistReasoning.cs`, `CustomerSupportBot.Domain/Services/SpecialistReasoningParser.cs` |
| Group Chat + Guardrails | `CustomerSupportBot.Adapters.Agents/CustomerSupportChatManager.cs`, `CustomerSupportBot.Application/Ports/Driven/WorkflowGuardOptions.cs` |
| Tool Use + Validation | `CustomerSupportBot.Application/Services/CustomerSupportToolsService.cs`, `CustomerSupportBot.Domain/Model/ToolResult.cs` |
| Dynamic Handoff | `CustomerSupportBot.Adapters.Agents/CustomerSupportChatManager.cs SelectNextAgentAsync` (L108-153) |
| Context Pipeline + Summarization | `CustomerSupportBot.Application/Services/ContextPipeline.cs`, `CustomerSupportBot.Application/Services/Providers/*.cs` |
| Deterministic Preprocessing | `CustomerSupportBot.Domain/Services/IdExtractor.cs` |
| Observability | `CustomerSupportBot.Domain/Model/ReasoningTrace.cs`, `CustomerSupportBot.Adapters.Persistence/InMemory/InMemoryReasoningTraceStore.cs`, `CustomerSupportBot.Api/Endpoints/TraceEndpoints.cs` |
| Prompt Externalization | `CustomerSupportBot.Adapters.Persistence/FileSystem/FileSystemPromptRepository.cs`, `CustomerSupportBot.Api/Prompts/**/*.md` |
| Evaluation | `CustomerSupportBot.Api.Tests/Evaluation/*.cs`, `docs/evaluation-scenarios.yaml` |
| **Grounded Reasoning** | `CustomerSupportBot.Application/Services/EntityVerifier.cs`, `CustomerSupportBot.Domain/Model/VerifiedEntities.cs`, `CustomerSupportBot.Api/Prompts/services/reasoning-system.md` |
| **Sanity Checking** | `CustomerSupportBot.Application/Services/ReasoningSanityChecker.cs`, `CustomerSupportBot.Domain/Model/ReasoningIssue.cs` |
| **Task Decomposition** | `CustomerSupportBot.Domain/Model/SubTask.cs`, `CustomerSupportBot.Application/Services/ReasoningService.cs` (ParseSubTasks), `CustomerSupportBot.Api/Prompts/services/reasoning-system.md` |
| **Task Orchestration** | `CustomerSupportBot.Adapters.Agents/DecomposedRunner.cs` (`RunDecomposedAsync`), `WorkflowRunner.cs`, `SubTaskOrchestrator.cs` |
