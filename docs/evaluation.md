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
    expected_agents: [PlanningAgent, ProductAgent, ResponseAgent]
    expected_tools: [product_inquiry_tool]
    success_criteria:
      - type: contains_any
        values: ["Dizüstü", "laptop"]
      - type: manual_review
        note: "no_hallucinated_price"
      - type: turn_count
        op: "<="
        value: 1

  - id: S06
    category: missing_customer_id_for_order
    query: "Son siparişimi göster"
    expected_behavior: clarification_request
    expected_agents: [PlanningAgent, ResponseAgent]
    success_criteria:
      - type: agent_requests_field
        field: customer_id
      - type: manual_review
        note: "no tool called with null customer_id"
      - type: turn_count
        op: "<="
        value: 1
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
| `expected_tools` | Hayır | Beklenen tool çağrıları (isim listesi — `no_extra_tool_calls` için) |
| `expected_tool_calls` | Hayır | İsim+argüman beklentisi — `[{name, arguments}]` (`tool_call_args_match` kriteri için, bkz. §3) |
| `success_criteria` | Evet | Yapılandırılmış (typed) değerlendirme kuralları (§3) |
| `known_failure_mode` | Hayır | Bilinen hata kategorisi (ör. `routing_error`, `hallucination`) |
| `repetitions` | Hayır | Senaryo kaç kez koşturulsun (non-determinism ölçümü). Varsayılan `1`. Bkz. §4a. |
| `quality_checks` | Hayır | MEAI LLM-judge kalite kontrolleri — `["relevance", "coherence"]`. Varsayılan boş. Bkz. §4b. |
| `turns` | Hayır | **Bilinen sınırlama:** YAML'da tanımlanabilir ama `EvaluationScenario`/`EvaluationRunner` bunu okumuyor — çok-turlu senaryolar (S12, S22, S23) bugün fiilen tek-turlu (`query`) gibi çalışır. Ayrı bir iş olarak ele alınmadı. |

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

## 3. CriteriaEvaluator — Yapılandırılmış (typed) Criterion Dispatch

`CriteriaEvaluator.Evaluate(spec, evalItem, ctx)` her `success_criteria` girdisini **`type` alanına göre** bir dispatch table üzerinden bir `Microsoft.Agents.AI.EvalCheck` delegate'ine yönlendirir (`EvalCheck = delegate EvalCheckResult(EvalItem)`, `Microsoft.Agents.AI` 1.15.0). Built-in eşleşen check'ler için framework'ün gerçek `EvalChecks`/`FunctionEvaluator` tiplerini kullanır; bu uygulamaya özgü olanlar (`turn_count`, `customer_id_used` vb.) `FunctionEvaluator.Create` ile yazılmış custom closure'lardır. Bu yüzden `CriteriaEvaluator`/`EvaluationRunner`, MAF'a bağımlı olmayan `CustomerSupportBot.Application` yerine `CustomerSupportBot.Adapters.Agents/Evaluation/`'da yaşar.

### Desteklenen `type`'lar

| `type` | Alanlar | Mekanizma | Ne yapar? |
|--------|---------|-----------|-----------|
| `contains_any` | `values: [...]` | `FunctionEvaluator.Create` | Yanıtta verilen kelimelerden en az biri var mı (OR, case-insensitive) |
| `tool_called` | `values: [...]` | `EvalChecks.ToolCalledCheck` (gerçek built-in) | Belirtilen tool'ların tümü çağrıldı mı |
| `tool_not_called` | `values: [...]` | `FunctionEvaluator.Create` | Belirtilen tool'lardan hiçbiri çağrılmadı mı |
| `turn_count` / `iteration_count` | `op`, `value` | `FunctionEvaluator.Create` | `ctx.IterationCount` karşılaştırması. Operatörler: `<=`, `<`, `==`, `>=`, `>`, `=` |
| `no_extra_tool_calls` | — | `FunctionEvaluator.Create` | Çağrılan tool sayısı beklenenden fazla değil |
| `no_missing_param_tool` | — | `FunctionEvaluator.Create` | Specialist `PreToolCheck`'te validation hatası yok |
| `agent_requests_field` | `field` | `FunctionEvaluator.Create` | Yanıtta ek bilgi isteniyor mu (müşteri kimliği/sipariş no) |
| `complaint_id_returned` | — | `FunctionEvaluator.Create` | Yanıtta `\d{4,}` formatında şikayet ID'si var mı |
| `order_id_returned` | — | `FunctionEvaluator.Create` | Yanıtta `\d{4,}` formatında sipariş ID'si var mı |
| `customer_id_used` | — | `FunctionEvaluator.Create` | Specialist `PreToolCheck.CollectedParams`'ta `customer_id` toplandı mı |
| `order_status_contains` | — | `FunctionEvaluator.Create` | Yanıtta sipariş durumu bilgisi (durum/teslim/kargo) var mı |
| `tool_call_args_match` | — (senaryo seviyesinde `expected_tool_calls`) | `EvalChecks.ToolCallArgsMatch` (gerçek built-in) | Çağrılan tool'ların argümanları senaryonun `expected_tool_calls` listesiyle eşleşiyor mu (subset match — fazladan argüman sorun değil). `expected_tool_calls` boşsa otomatik geçer. |
| `manual_review` | `note` | — | Her zaman `Passed=false, Skipped="manual_review_needed"` — otomatik değerlendirilemeyen kriterler için açık işaretleme |

Bilinmeyen `type` değeri de aynı şekilde `manual_review_needed` olarak işaretlenir (fail-safe, exception atmaz).

> **Kapsam notu:** Bu tablo, redesign öncesi regex/keyword-sniffing ile tanınan ~9 kalıbın 1:1 typed karşılığıdır. Yeni otomasyon kapasitesi eklenmedi — `docs/evaluation-scenarios.yaml`'daki, eskiden de fiilen `manual_review_needed`'a düşen kriterler (`no_hallucinated_price`, `refuses to comply`, `first_token_latency_ms < 3000` vb.) bu redesign'da açıkça `type: manual_review` olarak işaretlendi.

### EvalItem İnşası

`EvaluationRunner`, `EvalChecks.ToolCalledCheck` gibi built-in'lerin gerçekten çalışabilmesi için sentetik bir `EvalItem` kurar: kullanıcı sorgusu + `trace.ToolCalls` listesinden türetilen `FunctionCallContent`'li asistan mesajları. Gerçek `ChatMessage` geçmişi trace'te tutulmadığından bu sentetik ama framework'ün beklediği gerçek mekanizmayı (`item.Conversation` taraması) kullanır.

### CriteriaEvaluator Akışı

```
CriteriaEvaluator.Evaluate(CriterionSpec, EvalItem, ScenarioRunContext)
    ├─ Checks[spec.Type] bulunamadı → Skipped = "manual_review_needed"
    └─ Checks[spec.Type] bulundu   → EvalCheck(evalItem) çalıştırılır
    │
    ▼
    EvalCheckResult { Passed, Reason, CheckName } → CriterionResult { Criterion, Passed, Evaluation, Skipped? }
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
- **Tool listesi**: `trace.ToolCalls` (gerçek `ToolInvocation.ToolName` listesi) doğrudan kullanılır — daha önce agent adından tool adı tahmin eden bir heuristic vardı, redesign'da doğru veri kaynağına geçildi

### 4a. `repetitions` — Non-determinism Ölçümü

`scenario.Repetitions > 1` ise `EvaluationRunner.RunScenarioAsync`, senaryoyu N kez ayrı ayrı (her seferinde yeni izole session) koşturur ve sonuçları tek bir `ScenarioResult`'a indirger:

```
RunScenarioAsync(scenario)
    ├─ Repetitions <= 1 → tek koşu, eski davranış (RunSingleAsync)
    └─ Repetitions > 1  → N × RunSingleAsync → AggregateRepetitions(runs)
            ├─ İlk koşunun tüm alanları (Response, CriteriaResults, ToolsCalled...) korunur
            ├─ Repetitions = N
            ├─ RepetitionOutcomes = [koşu1.Passed, koşu2.Passed, ...]
            └─ RepetitionPassRate = geçen koşu sayısı / N
```

`AggregateRepetitions` saf/deterministik bir fonksiyon (LLM veya I/O gerektirmez) — `EvaluationRunner.AggregateRepetitions` olarak `internal static`, doğrudan unit test edilebilir (bkz. `EvaluationRunnerRepetitionsAndQualityTests.cs`).

**Maliyet uyarısı:** her repetition tam bir workflow koşusudur (reasoning + GroupChat) — `repetitions: 5` demek o senaryo için 5× LLM maliyeti demektir. Varsayılan `1`, bilinçli olarak yüksek tutulmamalı.

### 4b. `quality_checks` — MEAI LLM-judge Kalite Kontrolleri

`scenario.QualityChecks` (`["relevance", "coherence"]`) set'liyse, `RunSingleAsync` kriter değerlendirmesinin sonunda her check için `Microsoft.Extensions.AI.Evaluation.Quality`'nin gerçek `RelevanceEvaluator`/`CoherenceEvaluator`'ını çalıştırır — bunlar 1-5 arası bir skor üreten, ayrı bir "judge" LLM çağrısı yapan değerlendiricilerdir (skor < 4 → `Failed=true`, `Microsoft.Extensions.AI.Evaluation`'ın kendi `InterpretScore()` kuralı).

```
RunQualityCheckAsync("relevance", query, response)
    ├─ EvaluationQualityOptions.Enabled == false (appsettings.json "EvaluationQuality": {"Enabled": false}, VARSAYILAN)
    │       → CriterionResult { Skipped = "quality_checks_disabled" } — LLM çağrısı YAPILMAZ
    │
    └─ Enabled == true
            → new ChatConfiguration(chatClient) + RelevanceEvaluator/CoherenceEvaluator.EvaluateAsync(...)
            → CriterionResult { Passed = (score >= 4), Evaluation = "score=..., rating=..., reason=..." }
```

Sonuç, diğer kriterler gibi `ScenarioResult.CriteriaResults`'a `quality_relevance`/`quality_coherence` adıyla eklenir ve `PassedCriteria`/`TotalCriteria`'ya dahil edilir.

**Neden varsayılan kapalı:** her çağrı gerçek bir ek LLM isteği (judge modeli) — hem maliyetli hem yavaş. CI'da ayrı, isteğe bağlı bir job'da `EvaluationQuality:Enabled=true` ile açılması önerilir; günlük geliştirme akışında kapalı kalmalı.

```json
// appsettings.json
"EvaluationQuality": {
  "Enabled": false
}
```

---

## 5. ScenarioRunContext

`CriteriaEvaluator`'a iletilen bağlam objesi:

| Alan | Tip | Kaynak |
|------|-----|--------|
| `Response` | `string?` | `CustomerSupportTeam.RunAsync` çıktısı |
| `TerminationReason` | `string?` | `trace.TerminationReason` |
| `DetectedIntent` | `string?` | `reasoning.Intent` |
| `IterationCount` | `int` | `trace.IterationCount` |
| `ToolsCalled` | `List<string>` | `trace.ToolCalls` (gerçek `ToolInvocation.ToolName` listesi) |
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
          "criterion": "contains_any: Dizüstü OR laptop",
          "passed": true,
          "evaluation": "Passed"
        },
        {
          "criterion": "turn_count <= 1",
          "passed": true,
          "evaluation": "turn_count=1, beklenen <= 1"
        }
      ],
      "response": "Evet, Dell XPS 15 dizüstü bilgisayarımız mevcuttur...",
      "terminationReason": "completed",
      "detectedIntent": "ürün_bilgisi",
      "agentsVisited": ["PlanningAgent", "ProductAgent", "ResponseAgent"],
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
    - type: contains_any
      values: ["anahtar kelime"]
    - type: tool_called
      values: [beklenen_tool]
    - type: turn_count
      op: "<="
      value: 2
  known_failure_mode: routing_error
```

> **Not:** `type` alanı `CriteriaEvaluator`'ın dispatch table'ında yoksa (§3 tablosu) `manual_review_needed` olarak işaretlenir — aynı `type: manual_review` gibi. Bu senaryolar rapordan düşmez ama `Passed=false`, `Skipped="manual_review_needed"` olur.

---

## Çapraz Referanslar

- **Senaryo dosyası** → [evaluation-scenarios.yaml](evaluation-scenarios.yaml)
- **EvaluationRunner kaynak** → `Adapters.Agents/Evaluation/EvaluationRunner.cs`
- **CriteriaEvaluator kaynak** → `Adapters.Agents/Evaluation/CriteriaEvaluator.cs`
- **Model tanımları** → `Application/Ports/Inbound/EvaluationModels.cs`
- **Geliştirici rehberi** → [developer-guide.md](developer-guide.md)
- **API endpoint'leri** → [api/](api/README.md)
- **Agent davranışları** → [adapters-agents/](adapters-agents/README.md)
