# Evaluation (Otomatik Senaryo Değerlendirme)

**Dosyalar:**  
- `Services/Evaluation/EvaluationRunner.cs`  
- `Services/Evaluation/CriteriaEvaluator.cs`

## Ne yapar?

`EvaluationRunner`, hazır senaryo dosyalarındaki test durumlarını gerçek sistem üzerinde otomatik çalıştırır. Her senaryo için:
1. Reasoning çalıştırır
2. Workflow çalıştırır
3. Trace'i inceler
4. Başarı kriterlerini değerlendirir

Bu; regresyon testi, yeni ajan/tool doğrulaması ve prompt değişikliği etkisini ölçmek için kullanılır.

---

## `EvaluationRunner`

**Implements:** `IEvaluationPort`  
**Yaşam döngüsü:** Singleton

### `RunAsync`

```csharp
public async Task<EvaluationRunResult> RunAsync(
    List<EvaluationScenario> scenarios,
    CancellationToken ct = default)
```

Senaryoları **sırayla** çalıştırır (paralel değil — LLM rate limit koruması).

**`EvaluationRunResult` alanları:**

| Alan | Açıklama |
|------|---------|
| `TotalScenarios` | Toplam senaryo sayısı |
| `PassedScenarios` | Tüm kriterleri geçen sayısı |
| `FailedScenarios` | Hiç kriteri geçemeyen sayısı |
| `PartialScenarios` | Bazı kriterleri geçen sayısı |
| `Results` | Her senaryo için `ScenarioResult` |
| `CompletedAt` | Bitiş zamanı |

### `RunScenarioAsync`

```csharp
public async Task<ScenarioResult> RunScenarioAsync(
    EvaluationScenario scenario,
    CancellationToken ct = default)
```

**Tek senaryo akışı:**

```
1. İzole session oluştur (her senaryo kendi session'ında çalışır)

2. ReasoningService.ReasonAsync(query, session, history=null)
   → ReasoningResult elde et

3. IAgentTeamPort.RunAsync(query, history=null, session, reasoning)
   → Bot yanıtı al

4. IReasoningTraceStore.GetBySession(sessionId).LastOrDefault()
   → Trace'i al

5. Trace'ten AgentsVisited ve ToolsCalled bilgilerini derle

6. Her SuccessCriteria için CriteriaEvaluator.Evaluate(criterion, ctx) çağır

7. ExpectedIntent kontrolü (scenario.ExpectedIntent varsa)

8. ScenarioResult döndür
```

**`ScenarioResult` alanları:**

| Alan | Açıklama |
|------|---------|
| `ScenarioId` | Senaryo kimliği |
| `Query` | Test sorusu |
| `Response` | Bot yanıtı |
| `DetectedIntent` | Reasoning'in tespit ettiği intent |
| `AgentsVisited` | Ziyaret edilen agent adları (sadeleştirilmiş) |
| `ToolsCalled` | Çağrılan tool'lar |
| `TerminationReason` | Workflow neden bitti |
| `CriteriaResults` | Her kriter sonucu |
| `PassedCriteria` | Geçilen kriter sayısı |
| `TotalCriteria` | Toplam kriter sayısı |
| `DurationMs` | Milisaniye cinsinden süre |
| `Error` | Varsa hata mesajı |

---

## `CriteriaEvaluator`

**Tür:** `public static class`

`SuccessCriteria` alanı `List<string>` tipindedir — her kriter sade İngilizce metin cümlesidir. `CriteriaEvaluator.Evaluate(criterion, ctx)` regex + pattern matching ile bu metinleri yorumlar.

### Desteklenen kriter metinleri

| Kriter metni (örnek) | Kontrol |
|--------|---------|
| `response contains '<metin>'` | Bot yanıtı belirtilen metni içeriyor mu? (OR desteği: `A OR B`) |
| `turn_count <= N` / `turn_count == N` / `turn_count < N` | Workflow iterasyon sayısı koşulu |
| `no extra tool calls` | Çağrılan tool sayısı beklenen tool listesini aşmıyor mu? |
| `no missing_param_tool error` | Tool parametre validasyon hatası yok mu? |
| `<tool_name>_tool called` | Belirtilen tool çağrıldı mı? (örn. `order_status_tool called`) |
| `<tool_name>_tool NOT called` | Belirtilen tool çağrılmadı mı? |
| `agent requests customer_id` | Yanıt, ek bilgi (customer_id / order_id) talep ediyor mu? |
| `complaint id returned` / `order id returned` | Yanıtta yeni oluşturulan ID var mı? |
| `customer_id used correctly` | Specialist preToolCheck'te customer_id parametre olarak geçildi mi? |
| `response contains order status` | Yanıtta sipariş durumu bilgisi (durum/kargo/teslim) var mı? |

Tanımsız kriter metinleri `CriterionResult.Skipped = "manual_review_needed"` ile işaretlenir — otomatik değerlendirme atlanır.

### Kriter tanımı örneği (YAML)

```yaml
scenarios:
  - id: "order_inquiry_basic"
    category: "order"
    query: "4821 nerede?"
    expected_intent: "order_inquiry"
    expected_agents: ["PlanningAgent", "OrderAgent", "ResponseAgent"]
    expected_tools: ["order_status_tool"]
    success_criteria:
      - "response contains '4821'"
      - "order_status_tool called"
      - "response contains order status"
      - "turn_count <= 10"
      - "no extra tool calls"
```

Senaryo dosyaları `docs/evaluation-scenarios.yaml` altında tutulur.

---

## `ScenarioRunContext`

`CriteriaEvaluator`'a iletilen değerlendirme bağlamı:

```csharp
public class ScenarioRunContext
{
    public string? Response { get; set; }
    public string? TerminationReason { get; set; }
    public string? DetectedIntent { get; set; }
    public int IterationCount { get; set; }
    public List<string> ToolsCalled { get; set; } = new();
    public List<string> AgentsVisited { get; set; } = new();
    public List<string> ExpectedTools { get; set; } = new();
    public List<SpecialistReasoning> SpecialistReasonings { get; set; } = new();
    public ReasoningResult? Reasoning { get; set; }
    public PlanningResult? Planning { get; set; }
}
```

Tüm alanlar mutable set property'lere sahiptir — `init` yerine `set` kullanılır.

---

## `AgentToToolName` eşlemesi

`EvaluationRunner` trace'den tool'ların çağrılıp çağrılmadığını `SpecialistReasoning.PreToolCheck.CanProceed` alanından anlar. Hangi ajan hangi tool'u çağırdığını aşağıdaki mapping ile türetir:

```csharp
"ProductAgent" → "product_inquiry_tool"
"OrderAgent"          → "order_status_tool"  // default — birden fazla tool var
"ComplaintAgent"      → "complaint_registration_tool"
```

Yeni ajan eklendiğinde bu mapping'i güncellemeyi unutmayın.

---

## API üzerinden çalıştırma

```http
POST /eval/run
```

Senaryo dosyasının konumu otomatik çözülür (`ContentRoot/docs/evaluation-scenarios.yaml`). İsteğe bağlı sorgu parametresi: `?limit=N` (ilk N senaryoyu çalıştır). Tekil senaryo için: `POST /eval/run/{id}`

Yanıt:
```json
{
  "totalScenarios": 15,
  "passedScenarios": 13,
  "failedScenarios": 1,
  "partialScenarios": 1,
  "results": [ ... ]
}
```

---

## Yeni senaryo eklemek

`docs/evaluation-scenarios.yaml` dosyasına yeni bir entry ekleyin:

```yaml
- id: "complaint_registration_new"
  category: "complaint"
  query: "1001 için ürün hasarlı geldi şikayet açmak istiyorum"
  expected_intent: "complaint"
  expected_agents: ["PlanningAgent", "ComplaintAgent", "ResponseAgent"]
  expected_tools: ["complaint_registration_tool"]
  success_criteria:
    - "complaint_registration_tool called"
    - "response contains 'şikayet'"
    - "complaint id returned"
```

Kod değişikliği gerekmez — senaryo dosyası runtime'da okunur. `success_criteria` her zaman düz İngilizce metin satırı olmalıdır; `type:` / `value:` alanlarına sahip YAML nesneleri desteklenmez.
