# Endpoints — Observability

**Dosyalar:**
- `Endpoints/TelemetryEndpoints.cs` — `/telemetry/cost`
- `Endpoints/TraceEndpoints.cs` — `/traces`
- `Endpoints/AnalyticsEndpoints.cs` — `/analytics`, `/sessions/.../rating`
- `Endpoints/SlaEndpoints.cs` — `/sla`
- `Endpoints/EvaluationEndpoints.cs` — `/eval`

Tüm endpoint'ler **Admin** scope (Evaluation ve public rating hariç).

---

## TelemetryEndpoints — `/telemetry/cost`

LLM kullanım ve maliyet snapshot'ı.

| Route | Method | Açıklama |
|---|---|---|
| `/telemetry/cost` | GET | Per-model token + USD snapshot |
| `/telemetry/cost/models` | GET | Bilinen modeller listesi |
| `/telemetry/cost/reset` | POST | Snapshot'ı sıfırla |

### `GET /telemetry/cost`

```json
{
  "totalCalls": 1234,
  "totalInputTokens": 567890,
  "totalOutputTokens": 234567,
  "totalCostUsd": 1.234567,
  "byModel": [
    {
      "model": "gpt-4o-mini",
      "calls": 1000,
      "inputTokens": 500000,
      "outputTokens": 200000,
      "costUsd": 0.075,
      "averageLatencyMs": 450.32,
      "lastUsed": "2026-05-24T10:30:00Z"
    }
  ]
}
```

`CostUsageStore.GetUsageSnapshot()` döner. En pahalı model üstte.

### `GET /telemetry/cost/models`

```json
["gpt-4o-mini", "gpt-4o", "claude-haiku-4", "default"]
```

`CostCalculator.KnownModels` — pricing tablosundaki key'ler. UI dropdown için.

### `POST /telemetry/cost/reset`

`CostUsageStore.Reset()` — in-memory toplamları sıfırlar. Günlük/oturum bazlı rapor için.

⚠️ Production'da kalıcı toplam için `analytics.llm_call_usages` tablosu kullanılmalı.

Detay: [Adapters.Telemetry CostUsageStore](../adapters-telemetry/CostUsageStore.md).

---

## TraceEndpoints — `/traces`

Reasoning trace audit — debug ve replay için.

| Route | Method | Açıklama |
|---|---|---|
| `/traces/recent?count=20` | GET | Son N trace |
| `/traces/{traceId}` | GET | Tek trace detayı |
| `/traces/by-session/{sessionId}` | GET | Bir session'ın tüm trace'leri |
| `/traces/sessions` | GET | Session başına özet (sidebar) |
| `/traces/stats` | GET | Aggregate istatistikler |

### `GET /traces/{traceId}`

```json
{
  "traceId": "trace-abc",
  "sessionId": "sess-123",
  "userQuery": "ORD-5 nerede",
  "startedAt": "2026-05-24T10:00:00Z",
  "completedAt": "2026-05-24T10:00:02.5Z",
  "durationMs": 2500,
  "reasoning": {
    "analysis": "Kullanıcı ORD-5 siparişinin durumunu soruyor",
    "intent": "OrderInquiry",
    "confidence": "yüksek",
    "confidenceScore": 0.95
  },
  "planning": {
    "detectedIntent": "OrderInquiry",
    "selectedAgent": "OrderAgent"
  },
  "agentVisits": [
    { "agentName": "PlanningAgent", "durationMs": 230 },
    { "agentName": "OrderAgent", "durationMs": 1400 },
    { "agentName": "ResponseAgent", "durationMs": 180 }
  ],
  "toolCalls": [
    {
      "toolName": "order_status_tool",
      "agentName": "OrderAgent",
      "parametersSummary": "{ order_id: ORD-5 }",
      "resultSummary": "Success: Kargoda",
      "success": true
    }
  ],
  "terminationReason": "completed",
  "finalResponse": "Sipariş ORD-5 'Kargoda' durumunda.",
  "iterationCount": 3,
  "estimatedTokens": 1234
}
```

Reasoning admin paneli her trace'i bu detay seviyesinde gösterir.

### `GET /traces/sessions`

Sidebar — session başına özet:

```json
[
  {
    "sessionId": "sess-123",
    "firstQuery": "ORD-5 nerede",
    "traceCount": 5,
    "lastTraceAt": "..."
  }
]
```

`TracePortService.GetSessionsSummary` — trace'leri session'a göre grupla.

### `GET /traces/stats`

```json
{
  "totalTraces": 1234,
  "averageDurationMs": 1850,
  "successRate": 0.96,
  "topAgents": [
    { "name": "OrderAgent", "visits": 600 }
  ],
  "topTools": [
    { "name": "order_status_tool", "calls": 450 }
  ]
}
```

Son 500 trace'in aggregate'i.

---

## AnalyticsEndpoints — `/analytics`, `/sessions/.../rating`

Dashboard + kullanıcı rating'i.

| Route | Method | Auth | Rate limit |
|---|---|---|---|
| `/analytics/dashboard` | GET | Admin | — |
| `/analytics/session/{sid}` | GET | Admin | — |
| `/sessions/{sid}/rating` | POST | Anonymous | `general` (60/dak) |
| `/sessions/{sid}/rating` | GET | Anonymous | `general` |
| `/analytics/ratings/recent?count=20` | GET | Admin | — |

### `GET /analytics/dashboard`

```json
{
  "totalSessions": 1234,
  "totalMessages": 5678,
  "averageRating": 4.2,
  "ratingDistribution": { "1": 5, "2": 12, "3": 50, "4": 200, "5": 800 },
  "intentDistribution": {
    "OrderInquiry": 450,
    "Complaint": 200,
    "ProductInfo": 150
  },
  "sentimentDistribution": { "positive": 600, "neutral": 400, "negative": 234 },
  "openEscalations": 3,
  "pendingApprovals": 1,
  "activeHumanAgents": 5
}
```

4 farklı port'tan veri aggregate'i: rating, session, approval, escalation.

### `POST /sessions/{sid}/rating` (anonymous)

```http
POST /sessions/sess-123/rating
Content-Type: application/json

{ "stars": 5, "feedback": "Çok yardımcı oldu, teşekkürler!" }
```

**Validation:** `stars` 1-5 arası olmalı.

**429 Response (rate limit):**

```
HTTP/1.1 429 Too Many Requests
Retry-After: 30
```

Rate limit IP bazlı — spam önleme. Anonymous endpoint olduğu için JWT yok.

### `GET /analytics/ratings/recent?count=20`

```json
[
  {
    "sessionId": "sess-123",
    "stars": 5,
    "feedback": "Çok yardımcı oldu",
    "ratedAt": "2026-05-24T10:30:00Z"
  }
]
```

Düşük rating'li session'lar `LessonMiner` için **candidate**.

---

## SlaEndpoints — `/sla`

SLA Guardian metric/event'leri.

| Route | Method | Açıklama |
|---|---|---|
| `/sla/events` | GET | Son N warn/breach event |
| `/sla/status` | GET | Mevcut kuyruk + max yaş + breach sayısı |

### `GET /sla/events?count=100`

```json
[
  {
    "id": "sla-abc",
    "timestamp": "2026-05-24T10:00:00Z",
    "kind": "approval",
    "severity": "breach",
    "targetId": "app-xxx",
    "ageSeconds": 320,
    "action": "AutoReject",
    "note": "Pending > 300s threshold"
  }
]
```

`ISlaEventSink.GetRecent(count)` döner.

### `GET /sla/status`

```json
{
  "pendingApprovals": {
    "count": 5,
    "maxAgeSeconds": 240,
    "breachCount": 1
  },
  "openEscalations": {
    "count": 3,
    "maxAgeSeconds": 180,
    "breachCount": 0
  }
}
```

Anlık snapshot — admin dashboard widget.

---

## EvaluationEndpoints — `/eval`

YAML-based scenario testing.

| Route | Method | Auth | Açıklama |
|---|---|---|---|
| `/eval/scenarios` | GET | Anonymous | YAML'dan senaryo listesi |
| `/eval/run?limit=N` | POST | Anonymous | Tümünü veya N tane çalıştır |
| `/eval/run/{id}` | POST | Anonymous | Tek senaryo |

### `GET /eval/scenarios`

`docs/evaluation-scenarios.yaml` dosyasını okur:

```yaml
scenarios:
  - id: order-inquiry-basic
    title: "Basit sipariş sorgu"
    user_query: "ORD-5 nerede"
    expected_intent: OrderInquiry
    expected_agents: [PlanningAgent, OrderAgent, ResponseAgent]
    expected_tools: [order_status_tool]
    known_failure_modes: []
```

### `POST /eval/run?limit=10`

Senaryoları çalıştırır, her birinin sonucunu döner:

```json
[
  {
    "scenarioId": "order-inquiry-basic",
    "passed": true,
    "actualIntent": "OrderInquiry",
    "actualAgents": ["PlanningAgent", "OrderAgent", "ResponseAgent"],
    "durationMs": 1850,
    "issues": []
  },
  {
    "scenarioId": "complaint-with-refund",
    "passed": false,
    "actualIntent": "Complaint",
    "expectedIntent": "Complaint",
    "actualAgents": ["PlanningAgent", "OrderAgent"],
    "expectedAgents": ["PlanningAgent", "ComplaintAgent"],
    "issues": ["Wrong agent selected: OrderAgent (expected ComplaintAgent)"]
  }
]
```

Regression testing için — CI/CD pipeline'a entegre edilebilir.

### Neden anonymous?

Eval senaryoları **production data içermez** — hardcoded test query'leri. Public erişimin sakıncası yok. Production'da `RequireAuthorization` eklenebilir.

---

## Bağlantılar

- [Application TelemetryPortService](../application/TelemetryPortService.md)
- [Application TracePortService](../application/TracePortService.md)
- [Application AnalyticsPortService](../application/AnalyticsPortService.md)
- [Application SlaGuardian](../application/SlaGuardian.md)
- [Adapters.Telemetry CostUsageStore](../adapters-telemetry/CostUsageStore.md)
- [Infrastructure ScenarioLoader](Infrastructure.md#scenarioloader)
- [Domain Model-Trace](../domain/Model-Trace.md)
