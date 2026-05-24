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

### Desteklenen kriter türleri

| Kriter | Kontrol |
|--------|---------|
| `response_contains` | Bot yanıtı belirtilen metni içeriyor mu? |
| `response_not_contains` | Bot yanıtı belirtilen metni **içermiyor** mu? |
| `intent_is` | Tespit edilen intent beklenenle eşleşiyor mu? |
| `agent_visited` | Beklenen agent ziyaret edildi mi? |
| `agent_not_visited` | Belirtilen agent ziyaret **edilmedi** mi? |
| `tool_called` | Beklenen tool çağrıldı mı? |
| `termination_reason` | Workflow beklenen nedenle sonlandı mı? |
| `max_iterations` | İterasyon sayısı limiti aşılmadı mı? |
| `confidence_above` | Reasoning confidence skoru eşiğin üstünde mi? |
| `no_sanity_errors` | Sanity check'te Error seviyesinde sorun yok mu? |
| `preToolCheck_passed` | Specialist'in `preToolCheck.canProceed=true` oldu mu? |
| `response_language` | Yanıt belirtilen dilde mi? |

### Kriter tanımı örneği (YAML)

```yaml
scenarios:
  - id: "order_inquiry_basic"
    category: "order"
    query: "ORD-4821 nerede?"
    expected_intent: "order_inquiry"
    expected_agents: ["PlanningAgent", "OrderAgent", "ResponseAgent"]
    success_criteria:
      - type: response_contains
        value: "ORD-4821"
      - type: agent_visited
        value: "OrderAgent"
      - type: tool_called
        value: "order_status_tool"
      - type: termination_reason
        value: "completed"
      - type: max_iterations
        value: 10
      - type: no_sanity_errors
```

Senaryo dosyaları `docs/evaluation-scenarios.yaml` altında tutulur.

---

## `ScenarioRunContext`

`CriteriaEvaluator`'a iletilen değerlendirme bağlamı:

```csharp
public class ScenarioRunContext
{
    public string Response { get; init; }
    public string? TerminationReason { get; init; }
    public string? DetectedIntent { get; init; }
    public int IterationCount { get; init; }
    public List<string> ToolsCalled { get; init; }
    public List<string> AgentsVisited { get; init; }
    public List<string> ExpectedTools { get; init; }
    public List<SpecialistReasoning> SpecialistReasonings { get; init; }
    public ReasoningResult? Reasoning { get; init; }
    public PlanningResult? Planning { get; init; }
}
```

---

## `AgentToToolName` eşlemesi

`EvaluationRunner` trace'den tool'ların çağrılıp çağrılmadığını `SpecialistReasoning.PreToolCheck.CanProceed` alanından anlar. Hangi ajan hangi tool'u çağırdığını aşağıdaki mapping ile türetir:

```csharp
"ProductInquiryAgent" → "product_inquiry_tool"
"OrderAgent"          → "order_status_tool"  // default — birden fazla tool var
"ComplaintAgent"      → "complaint_registration_tool"
```

Yeni ajan eklendiğinde bu mapping'i güncellemeyi unutmayın.

---

## API üzerinden çalıştırma

```http
POST /evaluation/run
Content-Type: application/json

{ "scenarioFile": "docs/evaluation-scenarios.yaml" }
```

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
  query: "ORD-1001 için ürün hasarlı geldi şikayet açmak istiyorum"
  expected_intent: "complaint"
  success_criteria:
    - type: agent_visited
      value: "ComplaintAgent"
    - type: response_contains
      value: "şikayet"
    - type: no_sanity_errors
```

Kod değişikliği gerekmez — senaryo dosyası runtime'da okunur.
