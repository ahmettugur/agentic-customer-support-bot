# ImprovementsPortService

**Dosya:** `Services/Improvement/ImprovementsPortService.cs`  
**Implements:** `IImprovementsPort`  
**Yaşam döngüsü:** Singleton

## Ne yapar?

Self-improvement (kendi kendini geliştirme) pipeline'ını API katmanına açar. Başarısız veya düşük puanlı trace'lerden LLM ile lesson (ders) önerileri çıkarır; admin bu önerileri onaylar veya reddeder.

---

## Constructor bağımlılıkları

| Bağımlılık | Açıklama |
|-----------|---------|
| `LessonMiner` | Trace analizi ve lesson üretimi servisi |
| `ILessonStore` | Lesson kayıtlarının deposu |

---

## Metodlar

### `MineAsync`

```csharp
Task<MiningRunReport> MineAsync(CancellationToken ct = default)
```

`LessonMiner.MineAsync` çağırır. Aday trace'leri tarar, LLM'e analiz ettirir, `MiningRunReport` döner.

**`MiningRunReport` alanları:**

| Alan | Açıklama |
|------|---------|
| `Skipped` | Self-improvement devre dışıysa `true` |
| `SkipReason` | Neden atlandı |
| `Candidates` | İncelenen trace sayısı |
| `ProposedLessons` | Üretilen lesson önerisi sayısı |
| `LessonIds` | Oluşturulan lesson ID'leri |
| `Error` | LLM hatası varsa mesaj |

---

### `GetLessons`

```csharp
IReadOnlyList<Lesson> GetLessons(LessonStatus? status = null)
```

Tüm lesson'ları döner. `status` verilirse filtreler (Proposed / Approved / Rejected).

---

### `GetLesson`

```csharp
Lesson? GetLesson(string id)
```

Tekil lesson kaydını döner.

---

### `ApproveAsync`

```csharp
Task<bool> ApproveAsync(string id, string decidedBy, string? reason, CancellationToken ct = default)
```

Lesson'ı onaylar. `LessonMiner.ApproveAsync` çağırır:
1. `lesson.Status = Approved`
2. SemanticMemory enabled ise VectorStore Lessons koleksiyonuna yazar — ileriki konuşmalarda context olarak kullanılır
3. `ILessonStore.Update(lesson)`

---

### `Reject`

```csharp
bool Reject(string id, string decidedBy, string? reason)
```

Lesson'ı reddeder. `lesson.Status = Rejected` yazar.

---

## Lesson yaşam döngüsü

```
MineAsync() üretir
      │
      ▼
Status: Proposed
      │
      ├─── ApproveAsync() ──→ Status: Approved → VectorStore'a yazılır
      │
      └─── Reject()       ──→ Status: Rejected
```

---

## API endpoint'leri

```http
POST /improvements/mine          → MineAsync
GET  /improvements/lessons       → GetLessons(?status=proposed)
GET  /improvements/lessons/{id}  → GetLesson
POST /improvements/lessons/{id}/approve  → ApproveAsync
POST /improvements/lessons/{id}/reject   → Reject
```

---

## LessonMiner ile ilişki

`ImprovementsPortService` ince bir delegator'dır. İş mantığı tamamen `LessonMiner`'dadır. Detaylı bilgi için bkz. [LessonMiner.md](LessonMiner.md).
