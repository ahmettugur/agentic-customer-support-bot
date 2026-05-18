# Evaluation — Senaryo Tabanlı Değerlendirme

Bu doküman bot'un senaryo tabanlı test/değerlendirme sistemi olan `EvaluationRunner`'ı, senaryo YAML formatını ve metrik toplama mekanizmasını anlatır.

---

## 1. Genel Bakış

Değerlendirme sistemi YAML dosyasında tanımlı senaryoları sırayla çalıştırır, her birini beklenen sonuçlarla karşılaştırır ve detaylı rapor üretir.

```
evaluation-scenarios.yaml
    │
    ▼
EvaluationRunner
    ├─ Senaryo #1 → ReasoningService → CustomerSupportTeam → CriteriaEvaluator
    ├─ Senaryo #2 → ...
    └─ ...
    │
    ▼
EvaluationReport (JSON)
    ├─ overall: pass/fail, score
    ├─ scenarios: [{ name, status, criteria[], metrics }]
    └─ failureCategories: [{ category, count }]
```

---

## 2. Senaryo YAML Formatı

Senaryolar `docs/evaluation-scenarios.yaml` dosyasında tanımlanır:

```yaml
scenarios:
  - name: "Sipariş durumu sorgulama (geçerli ID)"
    input: "ORD-1 siparişimin durumu ne?"
    expected:
      intent: "order_inquiry"
      agent: "OrderAgent"
      tools: ["order_status_tool"]
      responseContains: ["ORD-1"]
    criteria:
      - type: intent_match
      - type: agent_selection
      - type: tool_usage
      - type: response_content
    tags: ["order", "happy-path"]
    timeout: 30

  - name: "Geçersiz ürün ID sorgulama"
    input: "PROD-999 ürününü sipariş edebilir miyim?"
    expected:
      intent: "order_placement"
      responseContains: ["bulunamadı", "geçersiz"]
    criteria:
      - type: response_content
      - type: no_hallucination
    tags: ["order", "edge-case"]
```

### Senaryo Alanları

| Alan | Zorunlu | Açıklama |
|------|---------|----------|
| `name` | Evet | Senaryo açıklaması |
| `input` | Evet | Kullanıcı mesajı |
| `expected` | Evet | Beklenen sonuçlar |
| `criteria` | Evet | Değerlendirme kriterleri |
| `tags` | Hayır | Kategori etiketleri (filtreleme için) |
| `timeout` | Hayır | Max çalışma süresi (saniye) |

### Kriter Tipleri

| Tip | Ne kontrol eder? |
|-----|-------------------|
| `intent_match` | Reasoning intent'i beklenenle eşleşiyor mu? |
| `agent_selection` | Doğru specialist agent seçildi mi? |
| `tool_usage` | Beklenen tool'lar çağrıldı mı? |
| `response_content` | Yanıt beklenen anahtar kelimeleri içeriyor mu? |
| `no_hallucination` | Yanıt uydurma bilgi içermiyor mu? |
| `no_tool_error` | Tool çağrısı hata döndürmedi mi? |
| `escalation_triggered` | Eskalasyon oluşturuldu mu? |

---

## 3. CriteriaEvaluator

Her kriter tipi için ayrı değerlendirme mantığı:

```
CriteriaEvaluator.Evaluate(scenario, result)
    ├─ intent_match    → result.reasoning.intent == expected.intent
    ├─ agent_selection → result.trace.agents.contains(expected.agent)
    ├─ tool_usage      → result.trace.tools ⊇ expected.tools
    ├─ response_content→ expected.responseContains.all(kw → response.contains(kw))
    └─ ...
    │
    ▼
    CriterionResult { type, pass, message }
```

---

## 4. Metrikler

Her senaryo çalışması için toplanan metrikler:

| Metrik | Açıklama |
|--------|----------|
| `totalDurationMs` | Toplam çalışma süresi |
| `reasoningDurationMs` | Reasoning süresi |
| `workflowDurationMs` | Workflow süresi |
| `tokenUsage` | Input + output token sayısı |
| `toolCallCount` | Tool çağrı sayısı |
| `agentTransitions` | Agent geçiş sayısı |

### Hata Kategorileri

```yaml
failureCategories:
  - intent_mismatch      # Yanlış intent tespiti
  - wrong_agent          # Yanlış agent seçimi
  - missing_tool_call    # Beklenen tool çağrılmadı
  - hallucination        # Uydurma bilgi üretildi
  - timeout              # Zaman aşımı
  - tool_error           # Tool çalışma hatası
```

---

## 5. API Endpoint'leri

| Endpoint | Metod | Açıklama |
|----------|-------|----------|
| `/eval/scenarios` | `GET` | Tanımlı senaryoları listele |
| `/eval/run` | `POST` | Tüm senaryoları çalıştır |
| `/eval/run?tag=order` | `POST` | Belirli tag'li senaryoları çalıştır |

### Yanıt Formatı

```json
{
  "overall": {
    "total": 12,
    "passed": 10,
    "failed": 2,
    "score": 0.833,
    "durationMs": 45000
  },
  "scenarios": [
    {
      "name": "Sipariş durumu sorgulama (geçerli ID)",
      "status": "passed",
      "criteria": [
        { "type": "intent_match", "pass": true },
        { "type": "agent_selection", "pass": true }
      ],
      "metrics": {
        "totalDurationMs": 3200,
        "tokenUsage": { "input": 1500, "output": 320 }
      }
    }
  ],
  "failureCategories": [
    { "category": "hallucination", "count": 1 },
    { "category": "timeout", "count": 1 }
  ]
}
```

---

## 6. Admin UI

`/admin.html` → "Evaluation" sekmesi:

- **Senaryo listesi**: Tüm tanımlı senaryolar, tag'leriyle
- **Çalıştır butonu**: Toplu veya tag bazlı çalıştırma
- **Sonuç tablosu**: Her senaryo için pass/fail durumu, süre, hata detayı
- **Genel skor**: Başarı yüzdesi

---

## 7. Yeni Senaryo Ekleme

1. `docs/evaluation-scenarios.yaml` dosyasına yeni senaryo ekleyin
2. `criteria` listesinde uygun kriter tiplerini seçin
3. `tags` ile kategori belirleyin
4. Admin UI üzerinden veya API ile test edin

```yaml
- name: "Yeni senaryo"
  input: "Kullanıcı mesajı"
  expected:
    intent: "beklenen_intent"
    agent: "BeklenenAgent"
    tools: ["beklenen_tool"]
    responseContains: ["anahtar", "kelime"]
  criteria:
    - type: intent_match
    - type: agent_selection
    - type: tool_usage
    - type: response_content
  tags: ["yeni-kategori"]
```

---

## Çapraz Referanslar

- **Senaryo dosyası** → [evaluation-scenarios.yaml](evaluation-scenarios.yaml)
- **Geliştirici rehberi** → [developer-guide.md](developer-guide.md)
- **API endpoint'leri** → [api.md](api.md)
- **Agent davranışları** → [agents.md](agents.md)
