# Reasoning & Planning Modelleri

**Dosyalar:**
- `Model/ReasoningResult.cs`
- `Model/ReasoningStep.cs`
- `Model/ReasoningIssue.cs`
- `Model/ConfidenceLevel.cs`
- `Model/PlanningResult.cs`
- `Model/SubTask.cs`

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

### Tipik issue code'ları

| Code | Anlam |
|---|---|
| `overconfident_clarification` | Yüksek confidence ama clarification gerekiyor — tutarsız |
| `redundant_required_info` | Toplanmış bilgi zaten var, tekrar istiyor |
| `grounding_missing` | Tüm step'ler assumption — delilsiz |
| `intent_evidence_mismatch` | Intent declared, evidence farklı niyete işaret ediyor |

`Severity=Error` issue varsa Application katmanı **replan tetikler** — düşünceyi tekrar yaptır.

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
    public string Intent { get; set; }                     // "OrderInquiry"
    public string Description { get; set; }
    public string? TargetAgent { get; set; }               // "OrderAgent"
    public Dictionary<string, string> Entities { get; set; } = new();
    public List<int> Dependencies { get; set; } = new();   // Sırasıyla çalıştırma
}
```

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
