# Reasoning & Planning Modelleri

**Dosyalar:**
- `Model/ReasoningResult.cs`
- `Model/ReasoningStep.cs`
- `Model/ReasoningIssue.cs`
- `Model/ConfidenceLevel.cs`
- `Model/PlanningResult.cs`
- `Model/SubTask.cs`
- `Model/TurnSignals.cs`

`ReasoningAgent` ve `PlanningAgent`'ın LLM çıktılarını temsil eder. `ReasoningResultParser` ve `PlanningResultParser` bu modelleri üretir.

---

## PlanningResult

PlanningAgent'ın seçtiği agent ve gerekçeleri:

```csharp
public class PlanningResult
{
    public List<string> SupportingEvidence { get; set; }  // Kullanıcı mesajından alıntılar

    public string SelectedAgent { get; set; }             // "OrderAgent"
    public string Rationale { get; set; }                 // Neden bu agent
    public List<RejectedAlternative> AlternativesRejected { get; set; }

    public bool NeedsClarification { get; set; }
    public string? ClarificationQuestion { get; set; }    // Eğer açıklama gerekiyorsa

    public string TaskDescription { get; set; }           // Seçilen agent'a verilecek görev
}

public class RejectedAlternative
{
    public string Agent { get; set; } = "";
    public string Reason { get; set; } = "";
}
```

**Niyet sahipliği:** PlanningAgent **routing-only**'dir — niyet tespiti yapmaz. Niyet ReasoningService'in tekil sorumluluğudur (`ReasoningResult.Intent`); PlanningAgent reasoning hint'indeki intent'i nihai karar kabul eder ve buna göre ajan seçer.

### NeedsClarification flow

Eğer `NeedsClarification == true`:
- `SelectedAgent` çağrılmaz
- `ClarificationQuestion` doğrudan kullanıcıya gönderilir
- Bir sonraki turn'de PlanningAgent tekrar çalışır

Bu, ambiguous mesajlar için (örn. `"siparişim"` — hangisi?) sistemin "tahmin yerine soru sor" davranışını sağlar.

---

## ReasoningResult

ReasoningAgent'ın çıktısı — daha kapsamlı analiz:

```csharp
public sealed class ReasoningResult
{
    // Temel
    public string Analysis { get; set; }                  // Kullanıcı niyet özeti
    public List<ReasoningStep> Steps { get; set; }        // Adım adım düşünce
    public string Intent { get; set; }
    public List<string> RequiredInfo { get; set; }        // Eksik bilgiler
    public string Confidence { get; set; }                // Legacy: "yüksek"/"high"

    // Genişletilmiş
    public string Rationale { get; set; }
    public List<string> Assumptions { get; set; }
    public string NextAction { get; set; }
    public string DecisionReason { get; set; }
    public double ConfidenceScore { get; set; }           // 0.0-1.0 (yeni format)

    // Sanity check (kendi kendini denetler)
    public List<ReasoningIssue> SanityIssues { get; set; }

    // Decomposition
    public List<SubTask> SubTasks { get; set; }

    // Sentiment
    public string Sentiment { get; set; }                 // "angry"/"negative"/"neutral"/"positive"
    public double SentimentScore { get; set; }

    // Helper computed — type-safe enum karşılığı
    public ConfidenceLevel ConfidenceLevel => ConfidenceScore switch
    {
        >= 0.75 => ConfidenceLevel.High,
        >= 0.5 => ConfidenceLevel.Medium,
        _ => ConfidenceLevel.Low
    };

    // Static yardımcılar
    public static string ScoreToString(double score) => score switch
    {
        >= 0.75 => WellKnown.Confidence.High,
        >= 0.5 => WellKnown.Confidence.Medium,
        _ => WellKnown.Confidence.Low
    };

    public static double StringToScore(string? s) => (s ?? "").Trim().ToLowerInvariant() switch
    {
        "yüksek" or "high" => 0.85,
        "orta" or "medium" => 0.6,
        "düşük" or "low" => 0.3,
        _ => 0.5
    };
}
```

---

## ReasoningStep

LLM'in düşünce sürecindeki tek adım:

```csharp
public sealed class ReasoningStep
{
    public int Order { get; set; }
    public string Description { get; set; }   // UI'a gösterilen metin

    public string Action { get; set; }
    public string Premise { get; set; }
    public string Grounding { get; set; }
    public double Confidence { get; set; }
    public string? AlternativeRejected { get; set; }
}
```

### Action tag'leri

| Action | Açıklama |
|---|---|
| `extract` | Kullanıcı mesajından bilgi çıkarma (ID, intent) |
| `route` | Hangi agent'a yönlenecek |
| `clarify` | Açıklama soru sor |
| `call_tool` | Tool çağrısı yapılacak |
| `verify` | Sonuç doğrulama |
| `terminate` | Workflow sonlandır |

### Grounding (delil kaynağı)

| Grounding | Anlamı |
|---|---|
| `regex` | IdExtractor regex match |
| `session_state` | Session'ın CollectedInfo'sundan |
| `history` | Önceki mesajlardan |
| `DB` | Veritabanı sorgusundan |
| `derived` | Türetilmiş (örn. "müşterinin son siparişi") |
| `assumption` | Varsayım (delil yok) |

`assumption` çoksa `SanityIssues` artar — sistem kendine güvenmediğini bilir.

---

## ReasoningIssue (Sanity Checker)

Reasoning'in **kendini denetlemesi**:

```csharp
public sealed class ReasoningIssue
{
    public string Code { get; set; }               // "overconfident_clarification"
    public IssueSeverity Severity { get; set; }    // Info | Warn | Error
    public string Message { get; set; }            // Türkçe açıklama
    public string? Field { get; set; }
    public string? SuggestedFix { get; set; }
}

public enum IssueSeverity { Info, Warn, Error }
```

### Kural kodları

8 kuralın tam listesi ve her birinin ne kontrol ettiği için bkz. [`ReasoningPipeline.md`](../CustomerSupportBot.Application/ReasoningPipeline.md#reasoningsanitychecker) — burada tekrar edilmiyor çünkü tek doğruluk kaynağı `ReasoningSanityChecker.cs`'teki kural sınıflarıdır.

> ⚠️ `SanityIssues` şu an **yalnızca log'a ve trace'e yazılır** — `Severity=Error` olsa bile otomatik replan tetiklemez, workflow bloklanmaz. (Bu dosyanın önceki bir sürümü "Error → replan tetikler" diyordu; bu doğru değildi, koddan doğrulanamadı.)

---

## ConfidenceLevel

```csharp
public enum ConfidenceLevel { Low, Medium, High }
```

| Level | Score aralığı |
|---|---|
| `Low` | `< 0.5` |
| `Medium` | `0.5 - 0.75` |
| `High` | `≥ 0.75` |

`ReasoningResult.ConfidenceLevel` computed property'sinden türetilir. UI'da renkli badge için kullanılır.

---

## SubTask (Compound Decomposition)

Bazı mesajlar **birden fazla iş** içerir:
> "Sipariş 1'imi sor, ardından şikayet açmak istiyorum"

ReasoningAgent bunu parçalara böler:

```csharp
public sealed class SubTask
{
    public int Order { get; set; }
    public string Intent { get; set; }                     // "sipariş_sorgulama"
    public string Description { get; set; }
    public string TargetAgent { get; set; } = "";           // "OrderAgent"
    public Dictionary<string, string> Entities { get; set; } = new();
    public List<int> Dependencies { get; set; } = new();
}
```

`Dependencies`, bu alt görevin önce tamamlanmış olmasını beklediği başka `Order` numaralarını taşır. `SubTaskOrchestrator.Partition` bunu okuyup öncülü aynı paralel batch'e almaz — çünkü paralel bir batch'in tüm elemanları aynı history snapshot'ıyla eşzamanlı başlar, yani kardeşler birbirinin sonucunu göremez. Sadece **gerçek** bir veri bağımlılığında doldurulmalı; bağımsız görevlere de eklemek paralelleşme fırsatını kaybettirip yürütmeyi yavaşlatır. Detay ve örnek: [`SubTaskOrchestrator.md`](../CustomerSupportBot.Application/SubTaskOrchestrator.md#dependencies--bağımlı-alt-görevler-aynı-batche-girmez).

### Örnek

```json
"subTasks": [
  {
    "order": 1,
    "intent": "OrderInquiry",
    "description": "1 siparişinin durumunu sor",
    "targetAgent": "OrderAgent",
    "entities": { "order_id": "1" },
    "dependencies": []
  },
  {
    "order": 2,
    "intent": "Complaint",
    "description": "Şikayet kaydı oluştur",
    "targetAgent": "ComplaintAgent",
    "entities": {},
    "dependencies": [1]   // Önce sipariş sorgusu tamamlansın
  }
]
```

`AgentTeamCoordinator` bu listeyi sıralı çalıştırır.

---

## TurnSignals

Reasoning'in bir turda ürettiği, session state'e taşınacak sinyaller:

```csharp
public sealed record TurnSignals(string? Intent, string? SentimentLabel, double? SentimentScore)
{
    public static TurnSignals? From(ReasoningResult? reasoning);
}
```

**Ne işe yarar?** `ReasoningResult`'ın tamamı değil, sadece session state'e yazılacak iki alanı (intent, sentiment) taşıyan küçük bir DTO. `From(reasoning)` fabrika metodu bu süzmeyi yapar ve "gerçek bir karar mı, yoksa parser'ın fallback değeri mi" ayrımını burada yapar:

- `Intent` boşsa veya `WellKnown.Intents.Unknown` ise → `null` (parser JSON'u okuyamadığında bu değeri koyar, gerçek bir karar değildir)
- `Sentiment` boşsa veya tam olarak `neutral@0.5` ise → `null` (parser'ın alan hiç dönmediğinde ürettiği varsayılan çift)

**Nereye gider?** `ChatPortService`, reasoning tamamlandığında `TurnSignals.From(reasoningResult)`'ı `SessionStateService.PersistExchangeAsync` → `ISessionManager.AddExchangeAsync` üzerinden `SessionStateExtractor.ExtractAndApply`'a (Domain katmanı) **girdi** olarak geçirir. Dolu alan kural tabanlı çıkarımın yerine geçer; `null` alanlarda kural tabanlı tablo (`WellKnown.IntentKeywords` / `SentimentKeywords`) devreye girer. Ayrıntılı akış: [`Services-SessionStateExtractor.md`](Services-SessionStateExtractor.md#turnsignals--llmin-girdisi-i̇kinci-bir-yazıcı-değil).

**Neden Domain katmanında?** `TurnSignals`'ı hem Application (`ChatPortService`, üretici) hem Domain (`SessionStateExtractor`, tüketici) hem Adapters.Persistence (`InMemorySessionManager`/`PostgresSessionManager`, taşıyıcı — `ISessionManager.AddExchangeAsync` parametresi) kullanıyor. Domain hiçbir üst katmana bağımlı olmadığı için ortak bir tip için doğru yer burasıdır — Application'da tanımlansaydı Adapters.Persistence'ın Application'a bağımlı olması gerekirdi, bu da Hexagonal mimarinin bağımlılık yönünü tersine çevirirdi.

---

## Akış özeti

```
Kullanıcı mesajı
   ↓
ReasoningAgent (LLM)
   → JSON output
   ↓
ReasoningResultParser
   → ReasoningResult (with SanityIssues, SubTasks)
   ↓
SanityChecker (Application)
   → Error varsa replan
   ↓
PlanningAgent (LLM)
   → JSON output
   ↓
PlanningResultParser
   → PlanningResult (SelectedAgent veya NeedsClarification)
   ↓
AgentTeamCoordinator
   → Specialist agent'ları çağırır
```
