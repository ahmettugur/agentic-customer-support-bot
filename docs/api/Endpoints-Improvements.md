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

`IImprovementsPort.MineAsync(ct)` çağrılır. Sonuç JSON olarak döner.

### `GET /improvements/?status=Proposed`

```json
[
  {
    "id": "lesson-abc",
    "title": "ID yokken specialist çağrılmamalı",
    "lessonText": "PlanningAgent ID yoksa önce kullanıcıdan iste",
    "observation": "Trace #X'te order_id olmadan OrderAgent çağrıldı",
    "suggestedAgent": "PlanningAgent",
    "sourceTraceIds": ["trace-1", "trace-2"],
    "status": "Proposed"
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

`IImprovementsPort.ApproveAsync(id, decidedBy, reason, ct)` çağrılır. Onaylanan lesson Knowledge memory'e gider.

### `POST /improvements/{id}/reject`

```json
{ "decidedBy": "admin-1", "reason": "Geçersiz; LLM yanlış genelleme yaptı" }
```

`IImprovementsPort.Reject(id, decidedBy, reason)`. Status=Rejected, VectorStore'a eklenmez. Audit için tutulur.

---

## MemoryEndpoints — `/memory`

Semantic memory (Qdrant) admin operasyonları.

| Route | Method | Açıklama |
|---|---|---|
| `/memory/stats` | GET | Toplam doc + embedding config |
| `/memory/search?q=...&kind=knowledge&topK=5` | GET | Semantic search |
| `/memory/ingest` | POST | KB re-ingest tetikle |

### `GET /memory/stats`

`IMemoryPort.Enabled` false ise `{ "enabled": false }` döner. Aksi halde:

```json
{
  "enabled": true,
  "collections": {
    "episodic": 1234,
    "lessons": 56,
    "knowledge": 89
  },
  "config": {
    "embeddingModel": "text-embedding-3-small",
    "dimension": 1536,
    "topK": 5,
    "minScore": 0.7
  }
}
```

### `GET /memory/search`

```http
GET /memory/search?q=sipariş iade&kind=knowledge&topK=5
```

`kind` query param: `episodic` | `lesson` | `knowledge` (default: `knowledge`). Geçersiz kind → 400.

**Response:**

```json
[
  {
    "score": 0.89,
    "doc": {
      "Id": "doc-1",
      "kind": "Knowledge",
      "Title": "İade Politikası",
      "Source": "policies.md",
      "SessionId": null,
      "text": "14 gün içinde...",
      "CreatedAt": "2026-05-20T...",
      "Tags": { "category": "policy" }
    }
  }
]
```

### `POST /memory/ingest`

`IMemoryPort.IngestAsync(ct)` — `FileSystemKnowledgeBaseSource` değişen dosyaları embed eder. `{ "status": "ok" }` döner.

---

## PersonalizationEndpoints — `/customers`

CustomerProfile yönetimi.

| Route | Method | Açıklama |
|---|---|---|
| `/customers/?take=100` | GET | Profil listesi |
| `/customers/{id}/profile` | GET | Tek profile |
| `/customers/{id}/profile/refresh` | POST | LLM consolidate tetikle |
| `/customers/{id}/profile/note` | PUT | Admin not ekle/güncelle |
| `/customers/{id}/profile` | DELETE | Profile sil |

### `GET /customers/?take=100`

`IPersonalizationPort.GetProfiles(take)` döner. Response: `{ count, items }`.

### `GET /customers/{id}/profile`

`IPersonalizationPort.GetProfile(id)` — null ise `{ customerId, found: false }` ile 404.

### `POST /customers/{id}/profile/refresh`

`IPersonalizationPort.RefreshProfileAsync(id, ct)` — LLM konsolidasyonu tetikler. Profile null ise 404.

⚠️ LLM maliyeti var — sık çalıştırılmamalı.

### `PUT /customers/{id}/profile/note`

```http
PUT /customers/1990/profile/note
Content-Type: application/json

{ "note": "VIP müşteri, premium destek hattı kullanmalı" }
```

`IPersonalizationPort.SetAdminNote(id, input?.Note)` — `AdminNote` field'ı specialist agent prompt'una eklenir.

### `DELETE /customers/{id}/profile`

`IPersonalizationPort.DeleteProfile(id)` → 204 veya 404. GDPR / right to be forgotten için.

---

## WorkflowEndpoints — `/workflows`

Low-code DSL — deterministic akış tanımları.

| Route | Method | Açıklama |
|---|---|---|
| `/workflows` | GET | Tüm workflow'lar `{ count, items }` |
| `/workflows/{id}` | GET | Tek workflow |
| `/workflows` | POST | Oluştur |
| `/workflows/{id}` | PUT | Güncelle |
| `/workflows/{id}` | DELETE | Sil |
| `/workflows/{id}/test` | POST | Test verisiyle çalıştır |

### `POST /workflows`

Request body: `WorkflowRequest` ([Models.md](Models.md) detayı).

```json
{
  "name": "Sipariş Takibi",
  "description": "Tek tıkla sipariş durumu",
  "isActive": true,
  "triggerKeywords": ["takip", "nerede"],
  "inputPatterns": { "order_id": "\\d{4,}" },
  "steps": [
    { "type": "Branch", "condition": "order_id missing", "skipNext": 99 },
    { "type": "Lookup", "tool": "order_status_tool", "parameters": { "order_id": "{order_id}" }, "storeAs": "status" },
    { "type": "Respond", "template": "Sipariş {order_id} durumu: {status}" }
  ]
}
```

`ClaimsPrincipal.Identity?.Name` `createdBy` olarak saklanır.

### `PUT /workflows/{id}`

Body `WorkflowRequest`. `def.Id = id` set edilerek mevcut kayıt güncellenir.

### `DELETE /workflows/{id}`

204 (silindi) veya 404 (bulunamadı).

### `POST /workflows/{id}/test`

```http
POST /workflows/siparis-takibi/test
Content-Type: application/json

{ "input": "5 nerede", "variables": {} }
```

`IWorkflowPort.Test(id, input, variables)` döner. Hata varsa `{ error }` ile 404 veya doğrudan hata mesajı.

### WorkflowRequest → WorkflowDefinition dönüşümü

```csharp
private static WorkflowDefinition MapToDefinition(WorkflowRequest req) => new()
{
    Name = req.Name,
    Steps = req.Steps.Select(s => new WorkflowStep
    {
        Id = s.Id ?? Guid.NewGuid().ToString("N")[..6],
        Type = Enum.TryParse<WorkflowStepType>(s.Type, true, out var t) ? t : WorkflowStepType.Respond,
        // ...
    }).ToList()
};
```

Parse başarısız olursa `Respond` default. Domain modeli doğrudan API sınırına maruz kalmaz.

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
