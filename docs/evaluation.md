# Evaluation — Senaryo Tabanlı Değerlendirme

Bu doküman bot'un senaryo tabanlı test/değerlendirme sistemi olan `EvaluationRunner`'ı, senaryo YAML formatını ve metrik toplama mekanizmasını anlatır.

---

## 1. Genel Bakış

Değerlendirme sistemi YAML dosyasında tanımlı senaryoları sırayla çalıştırır, her birini beklenen sonuçlarla karşılaştırır ve detaylı rapor üretir.

```
evaluation-scenarios.yaml
    │
    ▼
EvaluationRunner.RunAsync(scenarios)
    ├─ Senaryo #1 → İzole session → ReasoningService.ReasonAsync → CustomerSupportTeam.RunAsync → CriteriaEvaluator
    ├─ Senaryo #2 → ...
    └─ ...
    │
    ▼
EvaluationRunResult (JSON)
    ├─ TotalScenarios, PassedScenarios, FailedScenarios, PartialScenarios
    ├─ PassRate (0.0 – 1.0)
    └─ Results: [ScenarioResult { criteriaResults[], response, agentsVisited, toolsCalled }]
```

---

## 2. Senaryo YAML Formatı

Senaryolar `docs/evaluation-scenarios.yaml` dosyasında tanımlanır. Kök yapı: `version` + `scenarios` listesi.

```yaml
version: 1

scenarios:
  - id: S01
    category: product_inquiry_simple
    query: "Dizüstü bilgisayarınız var mı?"
    expected_intent: "ürün_bilgisi"
    expected_agents: [PlanningAgent, ProductInquiryAgent, ResponseAgent]
    expected_tools: [product_inquiry_tool]
    success_criteria:
      - "response contains 'Dizüstü' OR 'laptop'"
      - "no_hallucinated_price"
      - "turn_count <= 1"

  - id: S06
    category: missing_customer_id_for_order
    query: "Son siparişimi göster"
    expected_behavior: clarification_request
    expected_agents: [PlanningAgent, ResponseAgent]
    success_criteria:
      - "agent requests customer_id"
      - "no tool called with null customer_id"
      - "turn_count <= 1"
    known_failure_mode: missing_param_tool
```

### Senaryo Alanları

| Alan | Zorunlu | Açıklama |
|------|---------|----------|
| `id` | Evet | Senaryo kimliği (ör. `S01`, `S02`) |
| `category` | Evet | Senaryo kategorisi (ör. `product_inquiry_simple`, `compound_request`) |
| `query` | Evet | Kullanıcı mesajı |
| `expected_intent` | Hayır | Beklenen reasoning intent'i (ör. `"sipariş_sorgulama"`, `"şikayet"`) |
| `expected_behavior` | Hayır | Beklenen davranış tipi (ör. `clarification_request`) |
| `expected_agents` | Hayır | Beklenen agent geçiş sırası |
| `expected_tools` | Hayır | Beklenen tool çağrıları |
| `success_criteria` | Evet | String pattern tabanlı değerlendirme kuralları (aşağıda) |
| `known_failure_mode` | Hayır | Bilinen hata kategorisi (ör. `routing_error`, `hallucination`) |
| `turns` | Hayır | Çok turlu senaryolar için (multi-turn) |

### Senaryo Kategorileri (evaluation-scenarios.yaml'dan)

```yaml
# Hata kategorileri:
# routing_error       : PlanningAgent yanlış specialist'e yönlendirdi
# missing_param_tool  : Specialist eksik parametreyle tool çağırdı
# contradictory_answer: ResponseAgent tutarsız bilgi üretti
# infinite_loop       : Workflow 20 iterasyona takıldı
# intent_miss         : Kullanıcı niyeti yanlış anlaşıldı
# false_escalation    : Gereksiz ComplaintAgent tetiklendi
# hallucination       : ResponseAgent kaynakta olmayan bilgi üretti
# state_leak          : Oturum state'i agent'lar arası düzgün taşınmadı
```

---

## 3. CriteriaEvaluator — Regex Tabanlı Pattern Matching

`CriteriaEvaluator.Evaluate(criterion, ctx)` her `success_criteria` satırını **string pattern** olarak yorumlar. Tipler enum değil, serbest metin pattern'leridir.

### Desteklenen Pattern'lar

| Pattern | Örnek | Ne yapar? |
|---------|-------|-----------|
| `response contains '<text>'` | `"response contains 'Dizüstü' OR 'laptop'"` | Yanıtta anahtar kelimeleri arar. `OR` ile alternatifler desteklenir |
| `turn_count <op> N` | `"turn_count <= 1"` | İterasyon sayısını karşılaştırır. Operatörler: `<=`, `<`, `==`, `>=`, `>`, `=` |
| `<tool_name> called` | `"order_status_tool called"` | Belirtilen tool'un çağrılıp çağrılmadığını kontrol eder |
| `<tool_name> NOT called` | `"complaint_registration_tool NOT called"` | Tool'un çağrılmadığını doğrular |
| `no extra tool calls` | `"no extra tool calls"` | Beklenen tool sayısından fazla çağrı yapılmadığını kontrol eder |
| `no missing_param_tool error` | `"no missing_param_tool error"` | Tool validation hatası olmadığını doğrular |
| `agent requests <field>` | `"agent requests customer_id"` | Yanıtta ek bilgi isteniyor mu kontrol eder (ör. müşteri kimliği, sipariş numarası) |
| `complaint id returned` | `"complaint id returned"` | Yanıtta `CMP-\d+` formatında şikayet ID'si var mı? |
| `order id returned` | `"order id returned"` | Yanıtta `ORD-\d+` formatında sipariş ID'si var mı? |
| `customer_id used correctly` | `"customer_id used correctly"` | Specialist PreToolCheck'te `customer_id` parametresinin toplandığını doğrular |
| `response contains order status` | `"response contains order status"` | Yanıtta sipariş durumu bilgisi (durum/teslim/kargo) var mı? |

### Eşleşmeyen Pattern'lar

Desteklenmeyen pattern'lar (ör. `"no blind retry"`, `"graceful clarification"`) `manual_review_needed` olarak işaretlenir ve başarısız sayılmaz.

### CriteriaEvaluator Akışı

```
CriteriaEvaluator.Evaluate(criterion, ScenarioRunContext)
    ├─ "response contains ..." → EvalContains() — OR ile split, case-insensitive arama
    ├─ "turn_count <= N"       → IterationCount operatör karşılaştırma
    ├─ "no extra tool calls"   → ToolsCalled.Count <= ExpectedTools.Count
    ├─ "<tool> NOT called"     → ToolsCalled regex match (olumsuz)
    ├─ "<tool> called"         → ToolsCalled regex match
    ├─ "agent requests ..."    → Response'ta müşteri/sipariş sorusu arama
    ├─ "complaint id returned" → CMP-\d+ regex
    ├─ "order id returned"     → ORD-\d+ regex
    └─ (tanınmayan)            → Skipped = "manual_review_needed"
    │
    ▼
    CriterionResult { Criterion, Passed, Evaluation, Skipped? }
```

---

## 4. EvaluationRunner Çalışma Mantığı

Her senaryo **izole session** içinde çalışır (birbirinden bağımsız):

1. `_sessionManager.GetOrCreate(null)` — yeni session oluştur
2. `_reasoningService.ReasonAsync(query, session)` — reasoning pipeline'ı çalıştır
3. `_team.RunAsync(query, null, session, reasoning)` — workflow çalıştır
4. `_traceStore.GetBySession(sid).Last()` — trace'den sonuçları topla
5. Her `success_criteria` satırını `CriteriaEvaluator.Evaluate()` ile değerlendir
6. `expected_intent` varsa otomatik intent match kontrolü ekle

### Ek Otomatik Kontroller

`EvaluationRunner` `success_criteria` dışında ek kontroller de yapar:

- **Intent match**: `expected_intent` set'liyse `reasoning.Intent` ile case-insensitive karşılaştırma
- **Agent trace**: `trace.AgentVisits` üzerinden ziyaret edilen agent'ları derler
- **Tool mapping**: Specialist agent adından tool adını türetir (`OrderAgent` → `order_status_tool`)

---

## 5. ScenarioRunContext

`CriteriaEvaluator`'a iletilen bağlam objesi:

| Alan | Tip | Kaynak |
|------|-----|--------|
| `Response` | `string?` | `CustomerSupportTeam.RunAsync` çıktısı |
| `TerminationReason` | `string?` | `trace.TerminationReason` |
| `DetectedIntent` | `string?` | `reasoning.Intent` |
| `IterationCount` | `int` | `trace.IterationCount` |
| `ToolsCalled` | `List<string>` | Agent→tool mapping'den türetilmiş |
| `AgentsVisited` | `List<string>` | `trace.AgentVisits` (basitleştirilmiş isimler) |
| `ExpectedTools` | `List<string>` | Senaryo tanımından (`expected_tools`) |
| `SpecialistReasonings` | `List<SpecialistReasoning>` | `trace.SpecialistReasonings` |
| `Reasoning` | `ReasoningResult?` | `trace.Reasoning` |
| `Planning` | `PlanningResult?` | `trace.Planning` |

---

## 6. API Endpoint'leri

| Endpoint | Metod | Açıklama |
|----------|-------|----------|
| `/eval/scenarios` | `GET` | Tanımlı senaryoları listele |
| `/eval/run` | `POST` | Tüm senaryoları çalıştır |
| `/eval/run?limit=N` | `POST` | İlk N senaryoyu çalıştır |

### Yanıt Formatı (`EvaluationRunResult`)

```json
{
  "startedAt": "2026-05-24T10:00:00Z",
  "completedAt": "2026-05-24T10:02:30Z",
  "totalScenarios": 23,
  "passedScenarios": 18,
  "failedScenarios": 3,
  "partialScenarios": 2,
  "passRate": 0.7826,
  "results": [
    {
      "scenarioId": "S01",
      "category": "product_inquiry_simple",
      "query": "Dizüstü bilgisayarınız var mı?",
      "traceId": "trace_01HG...",
      "passedCriteria": 3,
      "totalCriteria": 3,
      "passed": true,
      "criteriaResults": [
        {
          "criterion": "response contains 'Dizüstü' OR 'laptop'",
          "passed": true,
          "evaluation": "'Dizüstü' yanıtta bulundu"
        },
        {
          "criterion": "turn_count <= 1",
          "passed": true,
          "evaluation": "iteration_count=1, beklenen <= 1"
        }
      ],
      "response": "Evet, Dell XPS 15 dizüstü bilgisayarımız mevcuttur...",
      "terminationReason": "completed",
      "detectedIntent": "ürün_bilgisi",
      "agentsVisited": ["PlanningAgent", "ProductInquiryAgent", "ResponseAgent"],
      "toolsCalled": ["product_inquiry_tool"],
      "durationMs": 3200,
      "error": null
    }
  ]
}
```

---

## 7. Admin UI

`/admin.html` → "Evaluation" sekmesi:

- **Senaryo listesi**: Tüm tanımlı senaryolar, kategori bilgileriyle
- **Çalıştır butonu**: Toplu çalıştırma
- **Sonuç tablosu**: Her senaryo için pass/fail/partial durumu, süre, hata detayı
- **Genel skor**: `PassRate` yüzdesi

---

## 8. Yeni Senaryo Ekleme

1. `docs/evaluation-scenarios.yaml` dosyasına yeni senaryo ekleyin
2. `success_criteria` listesinde desteklenen pattern'ları kullanın (§3 tablosu)
3. `expected_intent`, `expected_agents`, `expected_tools` ile otomatik kontroller ekleyin
4. `known_failure_mode` ile bilinen hata kategorisini belirtin
5. Admin UI üzerinden veya API ile test edin

```yaml
- id: S24
  category: yeni_kategori
  query: "Kullanıcı mesajı"
  expected_intent: "beklenen_intent"
  expected_agents: [PlanningAgent, BeklenenAgent, ResponseAgent]
  expected_tools: [beklenen_tool]
  success_criteria:
    - "response contains 'anahtar kelime'"
    - "beklenen_tool called"
    - "turn_count <= 2"
  known_failure_mode: routing_error
```

> **Not:** `CriteriaEvaluator` tarafından tanınmayan pattern'lar (ör. `"no blind retry"`) `manual_review_needed` olarak işaretlenir. Bu senaryolar rapordan düşmez ama `Passed=false`, `Skipped="manual_review_needed"` olur.

---

## Çapraz Referanslar

- **Senaryo dosyası** → [evaluation-scenarios.yaml](evaluation-scenarios.yaml)
- **EvaluationRunner kaynak** → `Application/Services/Evaluation/EvaluationRunner.cs`
- **CriteriaEvaluator kaynak** → `Application/Services/Evaluation/CriteriaEvaluator.cs`
- **Model tanımları** → `Application/Ports/Driving/EvaluationModels.cs`
- **Geliştirici rehberi** → [developer-guide.md](developer-guide.md)
- **API endpoint'leri** → [api/](api/README.md)
- **Agent davranışları** → [adapters-agents/](adapters-agents/README.md)
