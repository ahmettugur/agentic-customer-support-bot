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
      "suggestedAgent": "<ajan adı> | null",
      "traces": [1, 3]
    }
  ]
}
```

JSON parse başarısız olursa (LLM bozuk yanıt dönerse) boş liste döner, hata loglanır.

**`suggestedAgent` doğrulanır.** Değer `WellKnown.AgentNames.All`'a karşı kontrol edilir
(`NormalizeAgent`); tanınmayan bir ad `null`'a düşer. Prompt'ta da geçerli ajanların **tamamı**
listelenir — eskiden şemada tek örnek olarak `ProductAgent` yazıyordu ve model alakasız
dersleri de oraya etiketliyordu. Panelde rozet olarak gösterildiği için yanlış ajan adı,
boş bırakmaktan daha yanıltıcıdır.

**`traces` dersin kanıtını işaretler.** Model dersi destekleyen trace'lerin 1-tabanlı
numaralarını bildirir (prompt'taki `[n]` etiketleri); `ResolveSourceTraces` bunları gerçek
ID'lere çevirir. GUID'leri modele tekrar ettirmek hem token israfı hem hataya açık olurdu.
Model hiç geçerli numara vermezse incelenen tüm trace'lere düşülür.

> Eskiden kod her derse incelenen 8 trace'in **hepsini** yazıyordu; paneldeki "kaynak trace"
> linkleri bu yüzden gerçek kanıta işaret etmiyordu.

**Mükerrer koruması.** Daha önce görülmüş (Proposed veya Approved) bir başlık tekrar eklenmez —
aynı trace kümesi üzerinde tarama yeniden çalıştırıldığında model neredeyse aynı dersleri
üretir ve onay kuyruğu kopyalarla dolardı. Reddedilenler kasıtlı olarak hariçtir: admin bir
dersi reddettiyse aynı sorun tekrar gözlemlendiğinde yeniden önerilebilmelidir.

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
   (VEYA yeniden deneme: status == Approved ama VectorMemoryId boş — aşağıya bakın)
2. lesson.Status = Approved
3. lesson.DecidedBy / DecidedAt / DecisionReason güncelle
4. SemanticMemory.Enabled == true?
   → MemoryDocument oluştur:
      - Kind: Lesson
      - Text: title + lessonText + observation
      - Tags: { "agent": suggestedAgent }
   → SemanticMemoryService.UpsertAsync(doc)
   → lesson.VectorMemoryId = doc.Id

> ⚠️ **"Onaylı ama etkisiz" durumu.** Vektör yazımı başarısız olursa hata yutulur ve ders yine
> `Approved` işaretlenir; ders DB'de onaylı görünür ama hiçbir konuşmaya context olarak
> girmez. Ayırt edici sinyal `VectorMemoryId`'nin boş kalmasıdır. Admin paneli bu durumu uyarı
> + "Hafızaya yeniden yaz" butonu olarak gösterir ve aynı approve ucunu tekrar çağırır;
> `ApproveAsync` yalnızca bu özel durumda yeniden denemeye izin verir (ilk onay gerekçesi
> korunur, zaten hafızada olan ders mükerrer yazıma karşı reddedilir).
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
    "RecentTracesToScan": 50,
    "MinRatingForLesson": 3,
    "RequireApprovalBeforeActivation": true
  }
}
```

> **Tarama yalnızca manueldir.** Tek tetikleyici admin panelindeki "Yeni Tarama Çalıştır"
> düğmesidir (`POST /improvements/mine`); zamanlanmış bir arka plan servisi yoktur. Burada
> eskiden `MiningIntervalHours: 24` ayarı duruyordu ama onu okuyan hiçbir kod yoktu — ölü bir
> ayardı ve kaldırıldı.

| Ayar | Açıklama |
|------|---------|
| `Enabled` | Kapalıysa `MineAsync` hemen `Skipped` döner |
| `RecentTracesToScan` | Kaç trace incelensin |
| `MinRatingForLesson` | Bu puanın altındaki oturumlar aday sayılır |
