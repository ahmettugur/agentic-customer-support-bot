# Endpoints — Improvements & Memory & Personalization & Workflow

**Dosyalar:**
- `Endpoints/ImprovementsEndpoints.cs` — `/improvements` (self-improvement loop)
- `Endpoints/MemoryEndpoints.cs` — `/memory` (semantic memory)
- `Endpoints/PersonalizationEndpoints.cs` — `/customers` (CustomerProfile)
- `Endpoints/WorkflowEndpoints.cs` — `/workflows` (low-code DSL)

Hepsi **Admin** scope.

---

## ImprovementsEndpoints — `/improvements`

Self-improvement loop — geçmiş trace'lerden LLM ile ders çıkarımı.

| Route | Method | Açıklama |
|---|---|---|
| `/improvements/mine` | POST | Recent session'lardan lesson mine et |
| `/improvements/?status=Proposed` | GET | Status filtreli ders listesi |
| `/improvements/proposed` | GET | Sadece proposed |
| `/improvements/{id}` | GET | Tek lesson |
| `/improvements/{id}/approve` | POST | Onayla → Knowledge embed |
| `/improvements/{id}/reject` | POST | Reddet |

### `POST /improvements/mine`

```http
POST /improvements/mine
Authorization: Bearer <admin-jwt>
```

**Akış:**

```
LessonMiner.MineAsync()
   - Düşük rating ≤ 2 olan trace'ler
   - Error veya sanity Error olan trace'ler
   - Timeout olan trace'ler
   ↓
   Max 8 candidate trace seç
   ↓
   LLM ile JSON schema kullanarak analiz
   ↓
   Lesson { Status=Proposed } DB'ye yaz
```

**202 Response (async):**

```json
{ "status": "started", "candidateCount": 5 }
```

İşlem arka planda devam eder — `/improvements/proposed` ile sonuçlar görülür.

### `GET /improvements/?status=Proposed`

```json
[
  {
    "id": "lesson-abc",
    "title": "ID yokken specialist çağrılmamalı",
    "lessonText": "PlanningAgent ID yoksa önce kullanıcıdan iste, specialist'i çağırma",
    "observation": "Trace #X'te order_id olmadan OrderAgent çağrıldı, kullanıcı şikayet etti",
    "suggestedAgent": "PlanningAgent",
    "sourceTraceIds": ["trace-1", "trace-2", "trace-3"],
    "status": "Proposed",
    "createdAt": "2026-05-24T10:00:00Z"
  }
]
```

`status` query param: `Proposed` | `Approved` | `Rejected` | yoksa hepsi.

### `POST /improvements/{id}/approve`

```http
POST /improvements/lesson-abc/approve
Content-Type: application/json

{ "decidedBy": "admin-1", "reason": "Net bir kural, KB'ye eklensin" }
```

**Akış:**

```
ImprovementsPortService.ApproveAsync(id, decidedBy, reason)
   ├── LessonStore.UpdateStatus(id, Approved, decidedBy, reason)
   └── VectorMemoryAdapter.UpsertAsync(memoryDoc)
       - Kind = MemoryKind.Knowledge
       - Tags = { "lessonId": "lesson-abc" }
       - Embedding üretilir
   ↓
   Lesson.VectorMemoryId set edilir
```

Onaylanan lesson **Knowledge** memory'e gider — sonraki specialist prompt'larında semantic search ile bulunabilir.

### `POST /improvements/{id}/reject`

```json
{ "decidedBy": "admin-1", "reason": "Geçersiz; LLM yanlış genelleme yaptı" }
```

Status=Rejected, VectorStore'a eklenmez. Audit için tutulur (silinmez).

---

## MemoryEndpoints — `/memory`

Semantic memory (Qdrant) admin operasyonları.

| Route | Method | Açıklama |
|---|---|---|
| `/memory/stats` | GET | Toplam doc + embedding config |
| `/memory/search?q=...&kind=knowledge&topK=5` | GET | Semantic search |
| `/memory/ingest` | POST | KB re-ingest tetikle |

### `GET /memory/stats`

```json
{
  "enabled": true,
  "embedding": {
    "model": "text-embedding-3-small",
    "dimension": 1536,
    "topK": 5,
    "minScore": 0.7
  },
  "counts": {
    "episodic": 1234,
    "lesson": 56,
    "knowledge": 89
  }
}
```

Qdrant'taki belge sayıları kind bazında.

### `GET /memory/search`

```http
GET /memory/search?q=sipariş iade&kind=knowledge&topK=5
```

**Akış:**

```
OpenAiEmbeddingAdapter.EmbedAsync(query)
   ↓ 1536-dim vector
QdrantVectorMemoryAdapter.SearchAsync(vector, topK=5, tagFilter={_kind: "knowledge"})
   ↓ ScoredPoint'ler
HydrateDocument → MemoryDocument[]
```

**Response:**

```json
[
  {
    "score": 0.89,
    "document": {
      "id": "doc-1",
      "kind": "Knowledge",
      "title": "İade Politikası",
      "text": "14 gün içinde...",
      "source": "policies.md",
      "tags": { "category": "policy" },
      "createdAt": "2026-05-20T..."
    }
  }
]
```

`kind` query param default `knowledge`. `episodic` veya `lesson` da geçilebilir.

### `POST /memory/ingest`

```http
POST /memory/ingest
Authorization: Bearer <admin-jwt>
```

`IMemoryPort.IngestAsync` çağrılır — `FileSystemKnowledgeBaseSource` değişen dosyaları embed eder.

**Use case:** Yeni KB dosyası eklediniz, KnowledgeBaseIngestor startup'ta çalışmadı (auto-ingest kapalı) → manuel tetikle.

---

## PersonalizationEndpoints — `/customers`

CustomerProfile yönetimi.

| Route | Method | Açıklama |
|---|---|---|
| `/customers/?take=100` | GET | Profil listesi |
| `/customers/{id}/profile` | GET | Tek profile |
| `/customers/{id}/profile/refresh` | POST | LLM consolidate tetikle |
| `/customers/{id}/profile/note` | PUT | Admin not ekle |
| `/customers/{id}/profile` | DELETE | Profile sil |

### `GET /customers/?take=100`

```json
[
  {
    "customerId": "CUST-1990",
    "preferredLanguage": "tr",
    "preferredTone": "formal",
    "totalSessions": 12,
    "totalTurns": 87,
    "lastInteractionAt": "2026-05-23T..."
  }
]
```

LastInteractionAt DESC sıralı — en yeni müşteri en üstte.

### `GET /customers/{id}/profile`

```json
{
  "customerId": "CUST-1990",
  "preferredLanguage": "tr",
  "preferredTone": "formal",
  "summary": "VIP müşteri, sıkça iPhone ürünleri alır, kibar tonlu yanıt tercih eder",
  "adminNote": "VIP — hızlı yanıt",
  "intentFrequency": { "OrderInquiry": 30, "Complaint": 5 },
  "productInterests": ["iPhone", "Dell XPS"],
  "recentRatings": [
    { "stars": 5, "feedback": "Harika", "ratedAt": "..." }
  ],
  "totalSessions": 12,
  "totalTurns": 87,
  "lastConsolidatedAt": "2026-05-20T..."
}
```

### `POST /customers/{id}/profile/refresh`

```http
POST /customers/CUST-1990/profile/refresh
```

**Akış:**

```
CustomerProfileService.ConsolidateAsync(customerId)
   - Tüm session history + sayaçlar LLM'e ver
   - LLM Summary + PreferredTone üretir
   - Profile update + LastConsolidatedAt = now
```

**202 Async:** İşlem arka planda — admin yenileyince güncel veriyi görür.

⚠️ LLM maliyeti var — sık çalıştırılmamalı. Default deterministic update her turn'de ücretsiz yapılır.

### `PUT /customers/{id}/profile/note`

```http
PUT /customers/CUST-1990/profile/note
Content-Type: application/json

{ "note": "VIP müşteri, premium destek hattı kullanmalı" }
```

`AdminNote` field'ı specialist agent prompt'una eklenir — bot bilinçli davranır:

```
[Müşteri profili]
- Müşteri: CUST-1990
- Dil: tr, Ton: formal
- Admin Notu: VIP müşteri, premium destek hattı kullanmalı
```

### `DELETE /customers/{id}/profile`

GDPR / right to be forgotten için. Profile silinir; ama ilgili session'lar kalır (audit).

---

## WorkflowEndpoints — `/workflows`

Low-code DSL — deterministic akış tanımları.

| Route | Method | Açıklama |
|---|---|---|
| `/workflows` | GET | Tüm workflow'lar |
| `/workflows/{id}` | GET | Tek workflow |
| `/workflows` | POST | Oluştur |
| `/workflows/{id}` | PUT | Güncelle |
| `/workflows/{id}` | DELETE | Sil |
| `/workflows/{id}/test` | POST | Test verisiyle çalıştır |

### `GET /workflows`

```json
{
  "count": 3,
  "items": [
    {
      "id": "siparis-takibi",
      "name": "Sipariş Takibi",
      "version": 1,
      "isActive": true,
      "triggerKeywords": ["takip", "nerede"]
    }
  ]
}
```

### `POST /workflows`

Request body: `WorkflowRequest` ([Models.md](Models.md) detayı).

```json
{
  "name": "Sipariş Takibi",
  "description": "Tek tıkla sipariş durumu",
  "triggerKeywords": ["takip", "nerede"],
  "inputPatterns": { "order_id": "ORD-\\d+" },
  "steps": [
    { "type": "Branch", "condition": "order_id missing", "skipNext": 99 },
    { "type": "Lookup", "tool": "order_status_tool", "parameters": { "order_id": "{order_id}" }, "storeAs": "status" },
    { "type": "Respond", "template": "Sipariş {order_id} durumu: {status}" }
  ]
}
```

**Slugify:** `Name` → `Id` (yoksa). Türkçe karakter normalize edilir.

### `POST /workflows/{id}/test`

```http
POST /workflows/siparis-takibi/test
Content-Type: application/json

{
  "input": "ORD-5 nerede",
  "variables": {}
}
```

**Akış:**

```
WorkflowExecutor.Execute(definition, input, variables)
   - InputPatterns regex match → variables zenginleştir
   - Steps çalıştır
   - StepTraces logla
```

**Response:**

```json
{
  "workflowId": "siparis-takibi",
  "success": true,
  "finalResponse": "Sipariş ORD-5 durumu: Kargoda",
  "durationMs": 12,
  "stepTraces": [
    { "stepId": "0", "type": "Branch", "label": "order_id var mı?", "skipped": false, "output": "Condition met (order_id=ORD-5)" },
    { "stepId": "1", "type": "Lookup", "label": "Sipariş durumu", "output": "{ status: 'Kargoda' }" },
    { "stepId": "2", "type": "Respond", "output": "Sipariş ORD-5 durumu: Kargoda" }
  ],
  "finalVariables": { "order_id": "ORD-5", "status": "Kargoda" }
}
```

Deploy etmeden önce **canlı verisiz** test — admin panelinde "Test Et" butonu.

### Forbidden tools

```csharp
public static readonly HashSet<string> ForbiddenTools = new()
{
    "order_placement_tool",
    "complaint_registration_tool",
    "human_handoff_tool"
};
```

Workflow Lookup step bu tool'ları çağıramaz. **Runtime guard** — admin yazsa bile execute fail eder:

```
Error: Tool 'order_placement_tool' is forbidden in workflows. HITL gerekir.
```

---

## Bağlantılar

- [Application ImprovementsPortService](../application/ImprovementsPortService.md)
- [Application LessonMiner](../application/LessonMiner.md)
- [Application MemoryPortService](../application/MemoryPortService.md)
- [Application PersonalizationPortService](../application/PersonalizationPortService.md)
- [Application CustomerProfileService](../application/CustomerProfileService.md)
- [Application WorkflowPortService](../application/WorkflowPortService.md)
- [Application WorkflowExecutor](../application/WorkflowExecutor.md)
- [Domain Model-Workflow](../domain/Model-Workflow.md)
- [Domain Model-Memory](../domain/Model-Memory.md)
- [Models.md](Models.md) — WorkflowRequest DTO
