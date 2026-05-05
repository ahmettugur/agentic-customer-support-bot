# API — HTTP Endpoint & SSE Event Referansı

Backend `CustomerSupportBot` üzerinde tanımlı **tüm HTTP endpoint'leri** + **SSE event şemaları**. Frontend geliştiricileri, test yazanlar ve sistem entegratörleri için tek referans.

**Bölümler**:

- [1. Chat endpoints](#1-chat-endpoints)
- [2. Session endpoints](#2-session-endpoints)
- [3. Trace endpoints](#3-trace-endpoints)
- [4. Evaluation endpoints](#4-evaluation-endpoints)
- [5. SSE event şemaları](#5-sse-event-şemaları)
- [6. Genel JSON konvansiyonları](#6-genel-json-konvansiyonları)
- [7. Admin endpoints (HITL)](#7-admin-endpoints-hitl)

Sınıf/interface sözleşmeleri → [reference.md](reference.md).

---

## 1. Chat endpoints

### `POST /chat/` — Non-streaming

Tek bir kullanıcı sorgusunu işler ve **JSON yanıt** döner. Reasoning + workflow **sıralı** çalışır.

**Dosya**: `Endpoints/ChatEndpoints.cs`

**Request body** (`ChatRequest`):

```json
{
  "query": "ORD-1 siparişim nerede?",
  "sessionId": "c8e1..." 
}
```

| Alan | Tip | Zorunlu | Açıklama |
|---|---|---|---|
| `query` | string | ✅ | Kullanıcının güncel mesajı |
| `sessionId` | string | ❌ | Var olan oturuma devam. Boş/null ise yeni GUID üretilir |

**Response body** (`ChatResponse`):

```json
{
  "response": "Merhaba! ORD-1 numaralı siparişiniz (Dell XPS 15, 1 adet) şu an Kargolandı durumunda.",
  "sessionId": "c8e1...",
  "reasoning": {
    "analysis": "Kullanıcı ORD-1 için sipariş durumu sorguluyor",
    "intent": "sipariş_sorgulama",
    "confidence": "yüksek",
    "confidenceScore": 0.95,
    "steps": [...],
    "requiredInfo": [],
    "rationale": "order_id mevcut, OrderInquiryAgent çalıştırılabilir",
    "nextAction": "OrderInquiryAgent ile order_status_tool çağır",
    "decisionReason": "order_id ORD-1 regex ile çıkarıldı ve DB'de VERIFIED",
    "assumptions": [],
    "sanityIssues": [],
    "subTasks": []
  }
}
```

| Alan | Tip | Açıklama |
|---|---|---|
| `response` | string | ResponseAgent'ın temizlenmiş final mesajı (TERMINATE marker çıkarılmış) |
| `sessionId` | string | Cevap dönüldüğü oturum (client bunu sonraki istekte yollar) |
| `reasoning` | `ReasoningResult?` | Tam reasoning çıktısı (şema → [reference.md](reference.md)) |

**Status kodları**:

- `200 OK` — normal akış
- `5xx` — yakalanmayan hata (workflow guard'ları exception olarak fırlarsa)

**Örnek cURL**:

```bash
curl -X POST http://localhost:5099/chat/ \
  -H "Content-Type: application/json" \
  -d '{"query": "ORD-1 siparişim nerede?", "sessionId": null}'
```

---

### `POST /chat/stream` — SSE streaming

Aynı input, **Server-Sent Events** olarak adım adım stream. Reasoning token'ları, agent transitions, response delta'ları **sırayla** yayınlanır.

**Request body**: `ChatRequest` (aynı format).

**Response**:

- Header: `Content-Type: text/event-stream`, `X-Accel-Buffering: no`
- Body: SSE formatında event dizisi

**Event sırası (tipik akış)**:

```
event: session
data: {"sessionId": "c8e1..."}

event: reasoning_start
data: {}

event: reasoning_delta
data: {"text": "Kullanıcı ORD-1"}

event: reasoning_delta
data: {"text": " için sipariş durumu..."}

event: reasoning_complete
data: { /* tam ReasoningResult objesi */ }

event: agent
data: {"name": "PlanningAgent", "status": "running"}

event: agent
data: {"name": "OrderInquiryAgent", "status": "running"}

event: agent
data: {"name": "ResponseAgent", "status": "running"}

event: response_start
data: {}

event: response_delta
data: {"text": "Merhaba!"}

event: response_delta
data: {"text": " ORD-1"}

...

event: response_complete
data: {"text": "Merhaba! ORD-1 numaralı..."}

event: done
data: {"sessionId": "c8e1..."}
```

Compound query durumunda ek orchestrator event'leri olur → [5. SSE event şemaları](#5-sse-event-şemaları).

**Bağlantı iptali**: Client bağlantıyı kaparsa `OperationCanceledException` sessizce yakalanır, log'a yazılmaz.

**Hata akışı**:

```
event: error
data: {"message": "Reasoning timeout aşıldı"}
```

---

## 2. Session endpoints

**Dosya**: `Endpoints/SessionEndpoints.cs`

### `GET /sessions/`

Tüm oturum özetlerini döner (sidebar için). Sıralama: `LastActivity DESC`.

**Response**:

```json
[
  {
    "sessionId": "c8e1...",
    "title": "ORD-1 siparişim nerede?",
    "lastActivity": "2026-04-21T18:30:00+03:00",
    "messageCount": 4
  },
  { ... }
]
```

| Alan | Tip | Açıklama |
|---|---|---|
| `sessionId` | string | Oturum GUID |
| `title` | string | İlk kullanıcı mesajı (max 50 char + "...") |
| `lastActivity` | ISO 8601 | Son aktivite zamanı |
| `messageCount` | int | Toplam mesaj sayısı (user + assistant) |

---

### `GET /sessions/{sessionId}/messages`

Belirli oturumun mesaj geçmişi.

**Response**:

```json
[
  {"role": "user", "text": "ORD-1 siparişim nerede?"},
  {"role": "bot",  "text": "Merhaba! ORD-1 numaralı..."},
  {"role": "user", "text": "Teşekkürler"},
  {"role": "bot",  "text": "Rica ederim..."}
]
```

| Alan | Tip | Açıklama |
|---|---|---|
| `role` | `"user" \| "bot"` | Mesaj sahibi |
| `text` | string | Mesaj içeriği |

Oturum yoksa `[]` (boş array) döner, 404 dönmez.

---

### `GET /sessions/{sessionId}/state`

Oturumun **full state** dökümü — debug/frontend için.

**Response**:

```json
{
  "sessionId": "c8e1...",
  "createdAt": "2026-04-21T18:00:00+03:00",
  "lastActivity": "2026-04-21T18:30:00+03:00",
  "state": {
    "customerId": "CUST-1990",
    "currentIntent": "sipariş_sorgulama",
    "collectedInfo": {
      "LastMentionedOrderId": "ORD-1"
    },
    "turnCount": 2,
    "conversationSummary": null,
    "phase": "action"
  }
}
```

**Status kodları**:

- `200 OK` — oturum var
- `404 Not Found` — oturum yok

---

## 3. Trace endpoints

**Dosya**: `Endpoints/TraceEndpoints.cs`

### `GET /traces/recent?count=20`

Son N trace'i döner. Varsayılan `count=20`.

**Response**: `ReasoningTrace[]` — tam şema [reference.md#reasoningtrace](reference.md) → `ReasoningTrace` alan listesi. Ör:

```json
[
  {
    "traceId": "7b3f...",
    "sessionId": "c8e1...",
    "userQuery": "ORD-1 siparişim nerede?",
    "startedAt": "2026-04-21T18:30:00Z",
    "completedAt": "2026-04-21T18:30:02Z",
    "durationMs": 2140,
    "reasoning": { /* ReasoningResult */ },
    "planning": { /* PlanningResult? */ },
    "specialistReasonings": [...],
    "agentVisits": [
      {"agentName": "PlanningAgent", "startedAt": "...", "completedAt": "...", "durationMs": 580, "output": "..."},
      {"agentName": "OrderInquiryAgent", ...}
    ],
    "toolCalls": [
      {
        "toolName": "order_status_tool",
        "invokedAt": "...",
        "agentName": "OrderInquiryAgent",
        "parametersSummary": "{\"orderId\":\"ORD-1\"}",
        "resultSummary": "{\"success\":true,...}",
        "success": true,
        "signature": "order_status_tool:ORD-1"
      }
    ],
    "terminationReason": "completed",
    "finalResponse": "Merhaba! ORD-1 numaralı...",
    "iterationCount": 4,
    "error": null,
    "estimatedTokens": 3420
  }
]
```

---

### `GET /traces/{traceId}`

Tek bir trace detayı.

**Status kodları**:

- `200 OK` — trace bulundu
- `404 Not Found` — bulunamadı

---

### `GET /traces/by-session/{sessionId}`

Oturumun tüm trace'leri, **`StartedAt DESC`** sıralı. Compound query durumunda bir oturumda **N+1 trace** olabilir (user query başına N subtask trace).

---

### `GET /traces/stats`

Son 500 trace üzerinden aggregate istatistikler:

```json
{
  "totalTraces": 347,
  "completedCount": 340,
  "errorCount": 3,
  "avgDurationMs": 2134.5,
  "avgIterationCount": 3.8,
  "terminationReasons": {
    "completed": 295,
    "max_iterations": 28,
    "repeated_tool_call": 12,
    "timeout": 5
  }
}
```

---

## 4. Evaluation endpoints

**Dosya**: `Endpoints/EvaluationEndpoints.cs`

YAML tabanlı senaryo koşucusu. Senaryo dosyası: `docs/evaluation-scenarios.yaml` (veya `docs/...`).

### `GET /eval/scenarios`

Mevcut senaryoları listeler (koşturmadan).

**Response**:

```json
{
  "version": 1,
  "totalScenarios": 42,
  "scenarios": [
    {
      "id": "order-inquiry-happy-path",
      "category": "order_inquiry",
      "query": "ORD-1 siparişim nerede?",
      "expectedIntent": "sipariş_sorgulama",
      "expectedBehavior": "direct_answer",
      "expectedAgents": ["PlanningAgent", "OrderInquiryAgent", "ResponseAgent"],
      "expectedTools": ["order_status_tool"],
      "criteriaCount": 3,
      "knownFailureMode": null
    },
    { ... }
  ]
}
```

---

### `POST /eval/run?limit=N`

Tüm senaryoları koşturur (veya `limit=N` kadar). Uzun sürebilir — her senaryo **tam bir workflow**.

**Response** (`EvaluationRunResult`):

```json
{
  "startedAt": "2026-04-21T18:00:00Z",
  "completedAt": "2026-04-21T18:05:23Z",
  "totalScenarios": 42,
  "passedScenarios": 38,
  "failedScenarios": 1,
  "partialScenarios": 3,
  "passRate": 0.904,
  "results": [ /* ScenarioResult[] */ ]
}
```

---

### `POST /eval/run/{id}`

Tek senaryo.

**Response** (`ScenarioResult`):

```json
{
  "scenarioId": "order-inquiry-happy-path",
  "category": "order_inquiry",
  "query": "ORD-1 siparişim nerede?",
  "traceId": "7b3f...",
  "passedCriteria": 3,
  "totalCriteria": 3,
  "passed": true,
  "criteriaResults": [
    {"criterion": "response contains \"Kargolandı\"", "passed": true, "evaluation": "bulundu", "skipped": null},
    {"criterion": "order_status_tool called", "passed": true, "evaluation": "çağrıldı", "skipped": null},
    {"criterion": "turn_count <= 2", "passed": true, "evaluation": "iteration_count=2, beklenen <= 2", "skipped": null}
  ],
  "response": "Merhaba! ORD-1 numaralı...",
  "terminationReason": "completed",
  "detectedIntent": "sipariş_sorgulama",
  "agentsVisited": ["PlanningAgent", "OrderInquiryAgent", "ResponseAgent"],
  "toolsCalled": ["order_status_tool"],
  "durationMs": 2140,
  "error": null
}
```

**Status kodları**:

- `200 OK`
- `404 Not Found` — `id` ile senaryo yok

Desteklenen criterion pattern'ları → [reference.md#criteriaevaluator](reference.md).

---

## 5. SSE event şemaları

`/chat/stream` üzerinden yayınlanan tüm event tipleri. Sabitler: `Models/StreamEvent.cs` → `StreamEventTypes`.

### `session`

İlk event. Client `sessionId`'yi yakalamalı ve sonraki isteklerde kullanmalı.

```json
{ "sessionId": "c8e1..." }
```

---

### `reasoning_start`

Reasoning LLM çağrısı başladı. Boş data.

```json
{}
```

---

### `reasoning_delta`

Reasoning LLM'den gelen metin chunk'ı. Frontend bunları biriktirerek canlı reasoning paneli gösterebilir.

```json
{ "text": "Kullanıcı ORD-1 için..." }
```

Birden fazla kez yayınlanır.

---

### `reasoning_complete`

Reasoning bitti. Payload **full `ReasoningResult` objesi** — artık parse edilmiş JSON:

```json
{
  "analysis": "...",
  "steps": [...],
  "intent": "sipariş_sorgulama",
  "requiredInfo": [],
  "confidence": "yüksek",
  "confidenceScore": 0.95,
  "rationale": "...",
  "assumptions": [],
  "nextAction": "...",
  "decisionReason": "...",
  "sanityIssues": [],
  "subTasks": []
}
```

---

### `agent`

Agent transition. ChatManager bir yeni agent seçtiğinde yayınlanır. **Ortak alanlar**:

```json
{
  "name": "PlanningAgent",
  "status": "running"
}
```

| Alan | Tip | Değerler |
|---|---|---|
| `name` | string | `PlanningAgent \| ProductInquiryAgent \| OrderPlacementAgent \| OrderInquiryAgent \| ComplaintAgent \| ResponseAgent` veya compound query'de: `Orchestrator`, `SubTask#N` |
| `status` | string | `running \| done \| decomposing \| aggregating` |

**Compound query ek event'leri** (compound query sırasında):

```json
// Decomposition başladı
{ "name": "Orchestrator", "status": "decomposing", "subTaskCount": 2 }

// Subtask başladı
{ "name": "SubTask#1", "status": "running", "description": "ORD-1 için durum sorgula", "targetAgent": "OrderInquiryAgent", "order": 1, "total": 2 }

// Subtask içindeki normal agent event'leri forward edilir
{ "name": "PlanningAgent", "status": "running" }
{ "name": "OrderInquiryAgent", "status": "running" }
{ "name": "ResponseAgent", "status": "running" }

// Subtask bitti
{ "name": "SubTask#1", "status": "done", "order": 1 }

// ...ikinci subtask...

// Aggregation başladı
{ "name": "Orchestrator", "status": "aggregating" }
```

---

### `response_start`

Final yanıt üretimi başladı. Compound query'de Orchestrator'ın aggregated response yayınlaması öncesi.

**Tek workflow**:

```json
{}
```

**Compound query**:

```json
{ "decomposed": true, "subTaskCount": 2, "terminationReason": "completed" }
```

---

### `response_delta`

Final yanıt metin chunk'ı. Client bunları biriktirerek canlı yanıt gösterir.

```json
{ "text": "Merhaba! ORD-1 numaralı..." }
```

Compound query'de aggregated metin **`ChunkTextAsync`** ile kelime kelime parçalanarak yayınlanır (tek-workflow akışıyla UX tutarlılığı için).

---

### `response_complete`

Final yanıt bitti. Full text payload'da bulunur.

**Tek workflow**:

```json
{ "text": "Merhaba! ORD-1 numaralı..." }
```

**Compound query**:

```json
{
  "text": "**1) ORD-1 için durum...** \n\n ... \n\n --- \n\n **2) ORD-2 için şikayet...** \n\n ...",
  "decomposed": true,
  "subTaskCount": 2,
  "terminationReason": "completed"
}
```

---

### `error`

Beklenmedik hata. Workflow iptal edilmedi ama bir event'te sorun oldu. Frontend bu event'i görünce kullanıcıya "bir sorun oluştu" gösterebilir ve `done` event'ini beklemeli.

```json
{ "message": "Reasoning çağrısı başarısız: timeout" }
```

---

### `done`

Stream tamamlandı. Client bağlantıyı kapatabilir veya sonraki input'u bekleyebilir.

```json
{ "sessionId": "c8e1..." }
```

---

## 6. Genel JSON konvansiyonları

### Property naming: `camelCase`

Program.cs global config:

```csharp
services.Configure<JsonOptions>(opt => {
    opt.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    opt.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
});
```

- C# property: `SessionId` → JSON: `"sessionId"`
- Enum `IssueSeverity.Warn` → JSON: `"warn"`

### Enum serialization

`JsonStringEnumConverter(JsonNamingPolicy.CamelCase)` ile tüm enum'lar **camelCase string** olarak serialize edilir (number yerine):

- `EntitySource.Query` → `"query"`
- `EntitySource.SessionState` → `"sessionState"`
- `EntityVerification.NotFoundInDb` → `"notFoundInDb"`
- `IssueSeverity.Error` → `"error"`

### Null handling

Opsiyonel alanlar (`string?`, `VerifiedEntity?`) default olarak JSON'a **dahil edilir ve null olur**:

```json
{ "derivedLastOrderId": null }
```

### Türkçe karakter encoding

SSE'de `UnsafeRelaxedJsonEscaping` kullanılır — "İ", "Ş", "ç" gibi karakterler `\uXXXX` yerine ham UTF-8 yazılır:

```
data: {"text": "Merhaba! Siparişiniz Kargolandı."}    ← doğru
data: {"text": "Merhaba! Sipari\u015finiz..."}          ← kullanılmıyor
```

### Date format

ISO 8601 string'leri:

- `DateTime.UtcNow` → `"2026-04-21T18:30:00Z"`
- `DateTime.Now` (local) → `"2026-04-21T18:30:00+03:00"`

### Tool parametre formatı

`AIFunctionFactory.Create()` MAF'ın tool çağrılarını JSON schema'ya dönüştürür. Tool parametreleri camelCase isim + description attribute'tan şema:

```json
{
  "type": "function",
  "function": {
    "name": "order_status_tool",
    "description": "Sipariş durumunu sipariş numarasıyla sorgular...",
    "parameters": {
      "type": "object",
      "properties": {
        "orderId": { "type": "string", "description": "Sorgulanacak sipariş numarası (örn: ORD-1)" }
      },
      "required": ["orderId"]
    }
  }
}
```

---

## Endpoint özet tablosu

| Method | Route | Handler | Response tipi |
|---|---|---|---|
| POST | `/chat/` | `ChatEndpoints.HandleChatAsync` | `ChatResponse` JSON |
| POST | `/chat/stream` | `ChatEndpoints.HandleChatStreamAsync` | SSE event stream |
| GET | `/sessions/` | `SessionEndpoints` inline | `SessionInfo[]` JSON |
| GET | `/sessions/{sessionId}/messages` | `SessionEndpoints` inline | `{role,text}[]` JSON |
| GET | `/sessions/{sessionId}/state` | `SessionEndpoints` inline | Session + State JSON |
| GET | `/traces/recent?count=20` | `TraceEndpoints` inline | `ReasoningTrace[]` JSON |
| GET | `/traces/{traceId}` | `TraceEndpoints` inline | `ReasoningTrace` JSON |
| GET | `/traces/by-session/{sessionId}` | `TraceEndpoints` inline | `ReasoningTrace[]` JSON |
| GET | `/traces/stats` | `TraceEndpoints` inline | Aggregate stats JSON |
| GET | `/eval/scenarios` | `EvaluationEndpoints.HandleListScenarios` | Scenarios list JSON |
| POST | `/eval/run?limit=N` | `EvaluationEndpoints.HandleRunAll` | `EvaluationRunResult` JSON |
| POST | `/eval/run/{id}` | `EvaluationEndpoints.HandleRunOne` | `ScenarioResult` JSON |
| GET | `/approvals/pending` | `AdminEndpoints` inline | `ApprovalRequest[]` JSON |
| GET | `/approvals/recent?count=50` | `AdminEndpoints` inline | `ApprovalRequest[]` JSON |
| GET | `/approvals/{id}` | `AdminEndpoints` inline | `ApprovalRequest` JSON |
| POST | `/approvals/{id}/approve` | `AdminEndpoints` inline | `{id, status}` JSON |
| POST | `/approvals/{id}/reject` | `AdminEndpoints` inline | `{id, status}` JSON |
| GET | `/escalations/open` | `AdminEndpoints` inline | `EscalationRequest[]` JSON |
| GET | `/escalations/recent?count=50` | `AdminEndpoints` inline | `EscalationRequest[]` JSON |
| GET | `/escalations/{id}` | `AdminEndpoints` inline | `EscalationRequest` JSON |
| POST | `/escalations/{id}/acknowledge` | `AdminEndpoints` inline | `{id, status}` JSON |
| POST | `/escalations/{id}/resolve` | `AdminEndpoints` inline | `{id, status}` JSON |
| POST | `/escalations/{id}/dismiss` | `AdminEndpoints` inline | `{id, status}` JSON |
| POST | `/escalations/{id}/replan` | `AdminEndpoints` inline | `{id, sessionId, status:"replan_queued", requestedBy, releasedFromHuman}` JSON |
| GET | `/chat-sessions/active` | `AdminEndpoints` inline | `ChatSessionState[]` JSON |
| GET | `/chat-sessions/{sid}/state` | `AdminEndpoints` inline | `ChatSessionState` JSON |
| GET | `/chat-sessions/{sid}/history` | `AdminEndpoints` inline | `ChatBridgeMessage[]` JSON |
| POST | `/chat-sessions/{sid}/takeover` | `AdminEndpoints` inline | `{sessionId, mode:"human", humanAgent}` JSON |
| POST | `/chat-sessions/{sid}/release` | `AdminEndpoints` inline | `{sessionId, mode:"bot"}` JSON |
| POST | `/chat-sessions/{sid}/messages` | `AdminEndpoints` inline | `{sessionId, ok}` JSON |
| POST | `/chat-sessions/{sid}/replan` | `AdminEndpoints` inline | `{sessionId, status:"replan_queued", requestedBy, escalationsResolved, releasedFromHuman}` JSON |
| GET | `/chat-sessions/{sid}/subscribe` | `AdminEndpoints` inline | SSE event stream (admin-side) |
| GET | `/chat/events/{sessionId}` | `ChatEndpoints` inline | Persistent SSE event stream (user-side) |
| GET | `/analytics/dashboard` | `AnalyticsEndpoints` inline | Aggregate analytics JSON |
| GET | `/analytics/session/{sid}` | `AnalyticsEndpoints` inline | Session analytics JSON |
| GET | `/analytics/ratings/recent` | `AnalyticsEndpoints` inline | `ConversationRating[]` JSON |
| POST | `/sessions/{sid}/rating` | `AnalyticsEndpoints` inline | `{ok, rating}` JSON |
| GET | `/sessions/{sid}/rating` | `AnalyticsEndpoints` inline | `ConversationRating?` JSON |
| GET | `/admin` → `/admin.html` | Admin panel UI | HTML |

---

## 7. Admin endpoints (HITL)

**Dosya**: `@Endpoints/AdminEndpoints.cs`

Human-in-the-Loop mekanizması için **2 alt grup** endpoint:

- **Approval queue** — `order_placement_tool` / `complaint_registration_tool` öncesi onay
- **Escalation sink** — `needs_escalation` status'u için ticket kuyruğu

Tam pattern açıklaması → [patterns.md#20-human-in-the-loop](patterns.md#20-human-in-the-loop-approval-gate--escalation-sink).

> ⚠️ **Güvenlik**: Bu endpoint'lerde şu an **auth yok**. Production için JWT/role-based middleware eklenmesi gerekir.

### 7.1 Approvals

#### `GET /approvals/pending`

Bekleyen approval request'leri, `requestedAt ASC` sıralı.

**Response** (`ApprovalRequest[]`):

```json
[
  {
    "id": "a3f1b2c9d4e8",
    "sessionId": "c8e1...",
    "traceId": null,
    "toolName": "order_placement_tool",
    "agentName": "OrderPlacementAgent",
    "parameters": {
      "productName": "Dell XPS 15",
      "quantity": 1,
      "customerId": "CUST-1990"
    },
    "userQuery": "Bir Dell XPS 15 sipariş edebilir miyim?",
    "justification": "OrderPlacementAgent bu tool'u çağırmak istiyor.",
    "requestedAt": "2026-04-21T19:30:00Z",
    "decidedAt": null,
    "status": "pending",
    "decidedBy": null,
    "decisionReason": null,
    "timeoutSeconds": 60
  }
]
```

#### `GET /approvals/recent?count=50`

Son N request (karar verilmiş dahil). History tracking için.

#### `GET /approvals/{id}`

Tek bir request.

**Status kodları**:

- `200 OK`
- `404 Not Found`

#### `POST /approvals/{id}/approve`

Admin onayı.

**Request body** (`ApprovalDecisionInput`, hepsi opsiyonel):

```json
{ "decidedBy": "alice@acme.com", "reason": "stok ve miktar tamam" }
```

**Response**:

```json
{ "id": "a3f1b2c9d4e8", "status": "approved" }
```

**Status kodları**:

- `200 OK` — onay kaydedildi, tool lambda unblocked
- `404 Not Found` — request yok veya zaten karara bağlanmış

#### `POST /approvals/{id}/reject`

Admin reddi.

**Request body**:

```json
{ "decidedBy": "alice@acme.com", "reason": "miktar şüpheli (10+ adet)" }
```

**Response**:

```json
{ "id": "a3f1b2c9d4e8", "status": "rejected" }
```

Reddedilen request için tool lambda `ToolResult.ValidationError("İşlem onaylanmadı: ...")` döner ve agent bunu kullanıcıya iletir.

### 7.2 Escalations

#### `GET /escalations/open`

Açık (`open` veya `acknowledged` status'lu) eskalasyonlar, `createdAt ASC`.

**Response** (`EscalationRequest[]`):

```json
[
  {
    "id": "e7d2c4b1a8f5",
    "sessionId": "c8e1...",
    "traceId": "7b3f...",
    "agentName": "ComplaintAgent",
    "userQuery": "Sürekli hatalı ürün geldi, iade istiyorum",
    "reason": "İade/değişim işlemi için insan müdahalesi gerekli",
    "missingContext": ["iade politikası", "customer_tier"],
    "responseSummary": "Şikayetiniz kaydedildi. İade konusunda bir temsilci sizinle iletişime geçecek.",
    "createdAt": "2026-04-21T19:30:00Z",
    "acknowledgedAt": null,
    "resolvedAt": null,
    "status": "open",
    "assignedTo": null,
    "resolution": null
  }
]
```

#### `GET /escalations/recent?count=50`

Son N kayıt — tüm status'ları dahil.

#### `GET /escalations/{id}`

Tek kayıt.

#### `POST /escalations/{id}/acknowledge`

Temsilci işi üstleniyor (henüz çözmedi).

**Request body**:

```json
{ "assignedTo": "bob@acme.com" }
```

**Status transition**: `open` → `acknowledged`.

#### `POST /escalations/{id}/resolve`

Temsilci çözdü.

**Request body**:

```json
{
  "assignedTo": "bob@acme.com",
  "resolution": "İade başlatıldı, kargo 2 gün içinde alınacak."
}
```

**Status transition**: `open` veya `acknowledged` → `resolved`.

#### `POST /escalations/{id}/dismiss`

Yanlış/geçersiz eskalasyon.

**Request body**:

```json
{ "resolution": "bot yanlış algıladı; kullanıcı sadece bilgi istedi" }
```

**Status transition**: `open` veya `acknowledged` → `dismissed`.

#### `POST /escalations/{id}/replan`

**Admin Replan** — botun mevcut planını geçersiz say ve son müşteri mesajını PlanningAgent ile sıfırdan analiz et. Detaylı akış → §7.5.

**Request body** (`ReplanInput`, hepsi opsiyonel):

```json
{
  "requestedBy": "alice@acme.com",
  "note": "şikayet kaydı açmaya yönlendir, sipariş sorgulamadan kaçın"
}
```

- `note` **yalnızca PlanningAgent'a** one-shot system hint olarak iletilir; müşteriye GÖSTERİLMEZ.

**Response**:

```json
{
  "id": "e7d2c4b1a8f5",
  "sessionId": "c8e1...",
  "status": "replan_queued",
  "requestedBy": "alice@acme.com",
  "releasedFromHuman": true
}
```

Yan etkiler:
- Eskalasyon otomatik `resolved` (audit `resolution = note ?? defaultMessage`).
- Session Human modaysa Bot moduna döndürülür.
- Müşteriye `human_message`(`from="system"`) + `bot_typing` on/off + `human_message`(`from="bot"`) yayınlanır.

### 7.3 Live Takeover (chat-sessions)

Gerçek-zamanlı admin sohbet kontrolü. İlgili pattern → [patterns.md#203-live-human-takeover](patterns.md#203-live-human-takeover-real-time-agent-handover).

#### `GET /chat-sessions/active`

Şu an `ChatMode.Human` olan tüm session'lar.

**Response** (`ChatSessionState[]`): `{ sessionId, mode, humanAgent, enteredAt, lastActivityAt, messageCount }`.

#### `GET /chat-sessions/{sid}/state`

Tek session'ın mod snapshot'ı — yoksa varsayılan Bot durumu döner.

#### `GET /chat-sessions/{sid}/history`

Bridge ring buffer'ından son N mesaj (Bot + User + Admin + System). Admin "Devral" deyince geçmişi yükler.

#### `POST /chat-sessions/{sid}/takeover`

**Request body** (`ChatTakeoverInput`): `{ "humanAgent": "alice@acme.com" }` (opsiyonel — default `WellKnown.Defaults.Admin`).

Session'ı `Bot` → `Human` moduna geçirir. Kullanıcı tarafına `human_joined` SSE event'i düşer; bir sonraki `/chat/stream` isteği workflow'u atlar.

#### `POST /chat-sessions/{sid}/release`

Session'ı `Human` → `Bot` moduna döndürür. Kullanıcıya `human_left` SSE event'i düşer.

#### `POST /chat-sessions/{sid}/messages`

Admin sohbet ekranından mesaj gönderir.

**Request body** (`ChatAdminMessageInput`):

```json
{ "text": "Anladım, sizi şikayet ekibimize yönlendiriyorum.", "humanAgent": "alice@acme.com" }
```

Yan etkiler:
- `bridge.PublishAdminMessage` → `human_message` (`from="admin"`) müşteriye.
- `sessions.AppendAssistantMessage` → mesaj LLM-facing session history'sine **assistant** turu olarak yazılır. Böylece bot moduna dönülünce (release/replan) PlanningAgent admin'in vaatlerini/yönlendirmelerini görür.

#### `POST /chat-sessions/{sid}/replan`

Aktif sohbet panelinden replan'ı tetikler — eskalasyon olmasa bile kullanılabilir.

**Request body** (`ReplanInput`):

```json
{ "requestedBy": "admin", "note": "prodürt destek ajanına yönlendir" }
```

**Response**:

```json
{
  "sessionId": "c8e1...",
  "status": "replan_queued",
  "requestedBy": "admin",
  "escalationsResolved": 1,
  "releasedFromHuman": true
}
```

Yan etkiler `/escalations/{id}/replan` ile aynı — ayrıca bu session'a bağlı tüm açık eskalasyonlar otomatik resolve edilir.

#### `GET /chat-sessions/{sid}/subscribe`

Admin paneli için SSE — bu session'da kullanıcı tarafından gelen mesajlar `bridge_message` event'iyle akar.

### 7.4 HITL ile ilgili SSE event'leri

`/chat/stream` akışı sırasında 3 yeni event fırlar:

#### `approval_required`

Bir tool onay bekliyor. Payload tam `ApprovalRequest` objesi.

```
event: approval_required
data: { "id": "a3f1...", "toolName": "order_placement_tool", "parameters": {...}, "timeoutSeconds": 60, ... }
```

Client bu event'i görünce UI'da "Onay bekleniyor..." spinner gösterebilir veya admin için toast çıkarabilir.

#### `approval_resolved`

Karar verildi.

```
event: approval_resolved
data: { "id": "a3f1...", "status": "approved", "reason": null, "decidedBy": "admin" }
```

Status değerleri: `"approved" | "rejected" | "expired"`.

#### `escalation_created`

Yeni bir eskalasyon açıldı. Payload tam `EscalationRequest` objesi.

```
event: escalation_created
data: { "id": "e7d2...", "agentName": "ComplaintAgent", "reason": "...", ... }
```

Bu event workflow sonunda fırlar — kullanıcı zaten yanıtı almıştır, sadece backend admin bildirimi içindir.

#### `human_joined` / `human_left` / `human_message`

HITL **Live Takeover** event'leri — hem `/chat/stream` hem persistent `/chat/events/{sessionId}` kanalında yayınlanır.

```
event: human_joined
data: { "sessionId": "c8e1...", "humanAgent": "alice@acme.com", "enteredAt": "2026-04-21T19:30:00Z" }

event: human_message
data: { "id": "...", "from": "admin" | "system" | "bot", "humanAgent": "alice@acme.com", "text": "...", "timestamp": "..." }

event: human_left
data: { "sessionId": "c8e1..." }
```

- `from="admin"` → admin'in yazdığı mesaj (yeni baloncuk)
- `from="system"` → "temsilci katıldı/ayrıldı", takeover bildirimleri vb.
- `from="bot"` → admin replan'ı sonrası backend tarafından **otomatik** üretilen bot yanıtı (bkz. §7.5)

#### `handoff_pending` / `handoff_cleared`

Eskalasyon açıldı ama admin henüz takeover yapmadı → müşteri tarafında "temsilci bağlanıyor…" sarı banner'ı + input kilidi.

```
event: handoff_pending
data: { "escalationId": "e7d2...", "reason": "...", "createdAt": "..." }

event: handoff_cleared
data: { "escalationId": "e7d2...", "status": "resolved" | "dismissed" }
```

#### `bot_typing`

Bot arka planda otomatik yanıt hazırlıyor (ör. admin replan'ı sonrası). Frontend bu sinyalle typing indicator + input kilidini yönetir.

```
event: bot_typing
data: { "sessionId": "c8e1...", "on": true }
```

#### `sentiment_update` / `sentiment_alert`

Her tur sonrası güncel duygu skoru. `sentiment_alert` admin bildirimi için (kritik eşik aşıldı).

```
event: sentiment_update
data: { "sentiment": "frustrated", "score": -0.72, "consecutive": 2 }
```

### 7.5 Admin Replan akışı

Admin "🔄 Yeniden Planla" butonuna basınca arka planda şunlar tetiklenir:

1. **One-shot flag** — `SessionState.ForceReplanNextTurn = true` + `ReplanNote = note` (opsiyonel).
2. **Eskalasyon kapatma** — ilgili eskalasyon (veya bu session'ın açık tüm eskalasyonları) `resolved` durumuna geçer.
3. **Mod düşme** — session Human modaysa `Release()` çağrılır; müşteri tarafında `human_left` event'i düşer.
4. **Müşteri bildirimi** — `human_message` (`from="system"`) ile `WellKnown.FallbackMessages.ReplanCustomerNotice` ("ℹ️ Talebinizi tekrar değerlendiriyoruz…").
5. **Otomatik bot turu** — fire-and-forget `RunReplanBotTurnAsync`:
   - Session history'den son `User` mesajını çeker.
   - `bot_typing` on → `ReasoningService.ReasonAsync` → `CustomerSupportTeam.RunAsync` → `bot_typing` off.
   - `CustomerSupportTeam` `BuildWorkflowMessagesAsync`'te flag görür ve `WellKnown.FallbackMessages.ReplanPlanningHint` system mesajını prepend eder; `ReplanNote` varsa not da hint'e eklenir ("📌 Admin notu (sadece sana, müşteri görmez)…"). Flag + note tek seferlik kullanılır ve temizlenir.
   - Yanıt `AppendAssistantMessage` ile session history'sine yazılır ve `bridge.PublishBotMessage` ile müşteriye `human_message` (`from="bot"`) olarak yayılanır.

Müşteri tarafından görünen akış: yeşil temsilci bandı kalkar → sistem notu → typing indicator (input kilitli) → bot baloncuğu → (4+ mesaj eshold'ı sağlanıyorsa) puanlama widget'ı.

### 7.6 Konfigürasyon

`appsettings.json > "HumanInTheLoop"`:

```json
{
  "HumanInTheLoop": {
    "Enabled": true,
    "ToolsRequiringApproval": ["order_placement_tool", "complaint_registration_tool"],
    "TimeoutSeconds": 60,
    "AutoApproveOnTimeout": false,
    "EscalationEnabled": true
  }
}
```

`Enabled=false` yaparsanız tüm HITL mekanizması bypass edilir — eski davranış korunur. Detay → `@Models/ApprovalOptions.cs`.

---

## Çapraz referanslar

- **Sınıf/interface sözleşmeleri** → [reference.md](reference.md)
- **Agent davranışı + sub-component anatomisi** → [agents.md](agents.md)
- **Workflow + Compound query orkestrasyon** → [workflow.md](workflow.md)
- **Reasoning pipeline** → [reasoning.md](reasoning.md)
- **Frontend rendering detayı** → kod içi `wwwroot/js/chat-ui.js`
