# LessonMiner

**Dosya:** `Services/Improvement/LessonMiner.cs`  
**Tür:** Singleton (doğrudan DI kaydı)

## Ne yapar?

Düşük puanlı veya hatalı conversation trace'lerini toplar, LLM'e analiz ettirir ve sistemin kendini iyileştirmesi için `Lesson` önerileri üretir. Admin önerileri onaylar ya da reddeder; onaylanan lesson'lar VectorStore'a yazılarak gelecek konuşmalarda context olarak kullanılır.

---

## Constructor bağımlılıkları

| Bağımlılık | Açıklama |
|-----------|---------|
| `IReasoningTraceStore` | Trace kayıtları |
| `IRatingStore` | Müşteri puanları |
| `ILessonStore` | Lesson kayıtlarının deposu |
| `IGeneralChatClient` | LLM çağrısı için genel chat client |
| `IOptions<SelfImprovementOptions>` | `Enabled`, `RecentTracesToScan`, `MinRatingForLesson` |
| `SemanticMemoryService?` | Lesson approve'da VectorStore'a yazmak için (opsiyonel) |

---

## `MineAsync`

```csharp
Task<MiningRunReport> MineAsync(CancellationToken ct = default)
```

**Önkoşul:** `SelfImprovementOptions.Enabled = true`, aksi hâlde `Skipped = true` döner.

### Aday seçim kriterleri (heuristik, LLM-siz)

Şu trace'ler aday sayılır:
1. Rating `≤ MinRatingForLesson` olan session'ın son trace'i
2. `trace.Error != null` — hata ile sonlanan trace
3. `TerminationReason == "timeout"` veya `"error"`
4. `Reasoning.SanityIssues` içinde `Error` severity olan trace

---

### LLM analizi

En yeni 8 aday seçilir (token bütçesi sınırı). Her trace için şu bilgiler LLM'e verilir:
- Soru ve yanıt (max 240 karakter)
- Sonlanma nedeni, hata, iterasyon sayısı, süre
- Kullanıcı puanı ve feedback (varsa)
- SanityIssues (varsa)
- Ziyaret edilen agent'lar

**LLM çıktı şeması:**
```json
{
  "lessons": [
    {
      "title": "Kısa başlık",
      "lesson": "X durumunda Y yapılmalıdır",
      "observation": "Gözlemlenen sorun açıklaması",
      "suggestedAgent": "ProductAgent | null"
    }
  ]
}
```

JSON parse başarısız olursa (LLM bozuk yanıt dönerse) boş liste döner, hata loglanır.

---

### `MiningRunReport` alanları

| Alan | Açıklama |
|------|---------|
| `Skipped` | Self-improvement kapalıysa `true` |
| `SkipReason` | Neden atlandı |
| `Candidates` | Seçilen trace sayısı |
| `ProposedLessons` | Oluşturulan lesson önerisi sayısı |
| `LessonIds` | Oluşturulan ID'ler |
| `Error` | LLM hatası varsa mesaj |

---

## `ApproveAsync`

```csharp
Task<bool> ApproveAsync(string lessonId, string decidedBy, string? reason, CancellationToken ct = default)
```

**Akış:**
```
1. ILessonStore.Get(lessonId) → status == Proposed kontrolü
2. lesson.Status = Approved
3. lesson.DecidedBy / DecidedAt / DecisionReason güncelle
4. SemanticMemory.Enabled == true?
   → MemoryDocument oluştur:
      - Kind: Lesson
      - Text: title + lessonText + observation
      - Tags: { "agent": suggestedAgent }
   → SemanticMemoryService.UpsertAsync(doc)
   → lesson.VectorMemoryId = doc.Id
   (Hata olursa Warning log + status yine Approved — VectorStore opsiyonel)
5. ILessonStore.Update(lesson)
6. true döner
```

---

## `Reject`

```csharp
bool Reject(string lessonId, string decidedBy, string? reason)
```

`lesson.Status = Rejected` yazar. VectorStore'a yazmaz.

---

## Lesson yaşam döngüsü

```
MineAsync()
    │
    ▼ LessonStatus.Proposed (ILessonStore'da)
    │
    ├── ApproveAsync() → LessonStatus.Approved
    │                    VectorStore Lessons koleksiyonuna yaz
    │                    (SemanticMemoryContextProvider context'e dahil eder)
    │
    └── Reject()       → LessonStatus.Rejected (arşiv amaçlı tutulur)
```

---

## SelfImprovementOptions konfigürasyonu

```json
{
  "SelfImprovement": {
    "Enabled": true,
    "RecentTracesToScan": 100,
    "MinRatingForLesson": 2
  }
}
```

| Ayar | Açıklama |
|------|---------|
| `Enabled` | Kapalıysa `MineAsync` hemen `Skipped` döner |
| `RecentTracesToScan` | Kaç trace incelensin |
| `MinRatingForLesson` | Bu puanın altındaki oturumlar aday sayılır |
