# Endpoints — Observability

**Dosyalar:**
- `Endpoints/TelemetryEndpoints.cs` — `/telemetry/cost`
- `Endpoints/TraceEndpoints.cs` — `/traces`
- `Endpoints/AnalyticsEndpoints.cs` — `/analytics`, `/sessions/.../rating`
- `Endpoints/SlaEndpoints.cs` — `/sla`
- `Endpoints/EvaluationEndpoints.cs` — `/eval`

Tüm endpoint'ler **Admin** scope (public rating ve Evaluation hariç).

---

## TelemetryEndpoints — `/telemetry`

LLM kullanım ve maliyet snapshot'ı.

| Route | Method | Auth | Açıklama |
|---|---|---|---|
| `/telemetry/cost` | GET | Admin | Per-model token + USD snapshot |
| `/telemetry/cost/models` | GET | Admin | Bilinen modeller listesi |
| `/telemetry/cost/reset` | POST | Admin | Snapshot'ı sıfırla |

### `GET /telemetry/cost`

`ITelemetryPort.GetCostSnapshot()` döner.

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

### `GET /telemetry/cost/models`

```json
{ "knownModels": ["gpt-4o-mini", "gpt-4o", "default"] }
```

`ITelemetryPort.GetKnownModels()` — pricing tablosundaki key'ler. UI dropdown için.

### `POST /telemetry/cost/reset`

`ITelemetryPort.ResetCostSnapshot()` — in-memory toplamları sıfırlar. Günlük/oturum bazlı rapor için.

⚠️ Production'da kalıcı toplam için `analytics.llm_call_usages` tablosu kullanılmalı.

Detay: [Adapters.Telemetry CostUsageStore](../adapters-telemetry/CostUsageStore.md).

---

## TraceEndpoints — `/traces`

Reasoning trace audit — debug ve replay için. Admin scope.

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
  "userQuery": "5 nerede",
  "startedAt": "2026-05-24T10:00:00Z",
  "completedAt": "2026-05-24T10:00:02.5Z",
  "durationMs": 2500,
  "reasoning": { "analysis": "...", "intent": "OrderInquiry", "confidence": "yüksek" },
  "planning": { "selectedAgent": "OrderAgent", "needsClarification": false },
  "agentVisits": [
    { "agentName": "PlanningAgent", "durationMs": 230 },
    { "agentName": "OrderAgent", "durationMs": 1400 }
  ],
  "toolCalls": [
    {
      "toolName": "order_status_tool",
      "agentName": "OrderAgent",
      "parametersSummary": "{ order_id: 5 }",
      "resultSummary": "Success: Kargoda",
      "success": true
    }
  ],
  "terminationReason": "completed",
  "finalResponse": "Sipariş 5 'Kargoda' durumunda.",
  "iterationCount": 3,
  "wasRevised": false
}
```

Trace bulunamazsa 404 döner.

### `GET /traces/sessions`

Sidebar — session başına özet:

```json
[
  {
    "sessionId": "sess-123",
    "title": "5 nerede",
    "traceCount": 5,
    "messageCount": 12,
    "lastTraceAt": "..."
  }
]
```

`ITracePort.GetSessionsSummary()` — trace'leri session'a göre grupla.

### `GET /traces/stats`

```json
{
  "totalTraces": 1234,
  "averageDurationMs": 1850,
  "successRate": 0.96,
  "topAgents": [{ "name": "OrderAgent", "visits": 600 }],
  "topTools": [{ "name": "order_status_tool", "calls": 450 }]
}
```

---

## AnalyticsEndpoints — `/analytics`, `/sessions/.../rating`

Dashboard + kullanıcı rating'i.

| Route | Method | Auth | Rate limit | Açıklama |
|---|---|---|---|---|
| `/analytics/dashboard` | GET | Admin | — | Tüm istatistikler |
| `/analytics/session/{sid}` | GET | Admin | — | Tek session analytics |
| `/sessions/{sid}/rating` | POST | Anonymous | `general` (60/dak) | Rating gönder |
| `/sessions/{sid}/rating` | GET | Anonymous | `general` | Rating getir |
| `/analytics/ratings/recent?count=20` | GET | Admin | — | Son N rating |

### `GET /analytics/dashboard`

```json
{
  "totalSessions": 1234,
  "totalMessages": 5678,
  "averageRating": 4.2,
  "totalRatings": 800,
  "averageMessagesPerSession": 4.6,
  "averageSentimentScore": 0.68,
  "negativeSessions": 234,
  "sentimentAlerts": 12,
  "ratingDistribution": { "1": 5, "2": 12, "3": 50, "4": 200, "5": 800 },
  "sentimentDistribution": { "positive": 600, "neutral": 400, "negative": 234 },
  "intentDistribution": { "OrderInquiry": 450, "Complaint": 200 },
  "phaseDistribution": { "Action": 300, "Clarification": 100 },
  "approvalStats": { "total": 50, "approved": 40, "rejected": 5, "timedOut": 5 },
  "escalationStats": { "total": 30, "resolved": 25, "dismissed": 5 },
  "recentRatings": [{ "sessionId": "...", "stars": 5, "feedback": "...", "ratedAt": "..." }]
}
```

### `POST /sessions/{sid}/rating` (anonymous)

```http
POST /sessions/sess-123/rating
Content-Type: application/json

{ "stars": 5, "feedback": "Çok yardımcı oldu, teşekkürler!" }
```

**Validation:** `stars` 1-5 arası olmalı. Session bulunamazsa 404. Rate limit aşılırsa 429.

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

SLA Guardian metric/event'leri. Admin scope.

| Route | Method | Açıklama |
|---|---|---|
| `/sla/events?count=100` | GET | Son N warn/breach event |
| `/sla/status` | GET | Mevcut kuyruk + max yaş + breach sayısı |

### `GET /sla/events?count=100`

`ISlaPort.GetRecentEvents(count)` döner.

```json
{
  "count": 2,
  "items": [
    {
      "timestamp": "2026-05-24T10:00:00Z",
      "kind": "approval",
      "severity": "breach",
      "targetId": "app-xxx",
      "action": "AutoReject",
      "ageSeconds": 320,
      "note": "Pending > 300s threshold"
    }
  ]
}
```

### `GET /sla/status`

`ISlaPort.GetStatus()` döner — anlık snapshot:

```json
{
  "enabled": true,
  "pollIntervalSeconds": 30,
  "approvals": {
    "pendingCount": 5,
    "oldestSeconds": 240.0,
    "warnAfter": 60,
    "breachAfter": 300,
    "onBreach": "AutoReject",
    "breachCountRecent": 1
  },
  "escalations": {
    "openCount": 3,
    "oldestSeconds": 180.0,
    "warnAfter": 120,
    "breachAfter": 600,
    "boostPriorityOnBreach": true,
    "breachCountRecent": 0
  }
}
```

Admin dashboard widget için.

---

## EvaluationEndpoints — `/eval`

YAML-based scenario testing. Admin scope.

| Route | Method | Auth | Açıklama |
|---|---|---|---|
| `/eval/scenarios` | GET | Admin | YAML'dan senaryo listesi |
| `/eval/run?limit=N` | POST | Admin | Tümünü veya N tane çalıştır |
| `/eval/run/{id}` | POST | Admin | Tek senaryo |

### `GET /eval/scenarios`

`ScenarioLoader.LoadScenarios(path)` ile YAML okunur. `docs/evaluation-scenarios.yaml` (veya üst `docs/`) aranır.

```json
{
  "version": "1.0",
  "totalScenarios": 10,
  "scenarios": [
    {
      "Id": "order-inquiry-basic",
      "Category": "order",
      "Query": "5 nerede",
      "ExpectedIntent": "OrderInquiry",
      "ExpectedBehavior": "...",
      "ExpectedAgents": ["PlanningAgent", "OrderAgent"],
      "ExpectedTools": ["order_status_tool"],
      "criteriaCount": 3,
      "KnownFailureMode": null
    }
  ]
}
```

### `POST /eval/run?limit=10`

`IEvaluationPort.RunAsync(scenarios, ct)` çalıştırır.

### `POST /eval/run/{id}`

`IEvaluationPort.RunScenarioAsync(scenario, ct)` tek senaryo. Bulunamazsa 404.

### YAML path çözümü

```csharp
var candidates = new[]
{
    Path.Combine(env.ContentRootPath, "docs", WellKnown.Evaluation.ScenarioFileName),
    Path.Combine(env.ContentRootPath, "..", "docs", WellKnown.Evaluation.ScenarioFileName),
};
```

---

## Bağlantılar

- [Application TelemetryPortService](../application/TelemetryPortService.md)
- [Application TracePortService](../application/TracePortService.md)
- [Application AnalyticsPortService](../application/AnalyticsPortService.md)
- [Application SlaGuardian](../application/SlaGuardian.md)
- [Adapters.Telemetry CostUsageStore](../adapters-telemetry/CostUsageStore.md)
- [Infrastructure ScenarioLoader](Infrastructure.md#scenarioloader)
- [Domain Model-Trace](../domain/Model-Trace.md)
- [Workers.md](Workers.md) — SlaGuardianService
