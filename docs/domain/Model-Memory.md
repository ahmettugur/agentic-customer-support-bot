# Memory & Improvement Modelleri

**Dosyalar:**
- `Model/Memory/CustomerProfile.cs`
- `Model/Memory/MemoryDocument.cs`
- `Model/Improvement/Lesson.cs`

Uzun vadeli hafıza ve self-improvement loop için modeller.

---

## CustomerProfile

Bir müşterinin **kalıcı profili** — turn'ler arası bilgi taşımak için.

```csharp
public sealed class CustomerProfile
{
    public string CustomerId { get; init; }              // Primary key

    // Tercihler
    public string PreferredLanguage { get; set; } = "tr";
    public string? PreferredTone { get; set; }           // "formal" | "casual"

    // Davranış toplamları
    public Dictionary<string, int> IntentFrequency { get; set; } = new();
    public List<string> ProductInterests { get; set; } = new();
    public List<RatingSnapshot> RecentRatings { get; set; } = new();

    // LLM tarafından oluşturulan
    public string? Summary { get; set; }                 // 1-2 satır profil
    public string? AdminNote { get; set; }               // Admin manuel ekledi

    // Sayaçlar
    public int TotalSessions { get; set; }
    public int TotalTurns { get; set; }
    public DateTime? LastInteractionAt { get; set; }
    public DateTime? LastConsolidatedAt { get; set; }    // LLM consolidation timestamp
}

public sealed record RatingSnapshot(int Stars, string? Feedback, DateTime RatedAt);
```

### İki güncelleme yolu

| Yol | Trigger | Maliyet |
|---|---|---|
| **Deterministic** | Her turn (otomatik) | Ücretsiz, ms |
| **LLM consolidate** | Admin tetikler / scheduled | Bir LLM call |

**Deterministic:** `CustomerProfileService.RecordInteractionAsync` her turn'de:
- `TotalTurns++`
- `IntentFrequency[currentIntent]++`
- Ürün isimleri çıkar → `ProductInterests`
- Dil heuristik (Türkçe karakter oranı)
- `RecentRatings` (son 10)
- `LastInteractionAt = now`

**LLM consolidate:** `CustomerProfileService.ConsolidateAsync`:
- Tüm history + sayaçları LLM'e ver
- LLM `Summary` ve `PreferredTone` üretir
- `LastConsolidatedAt = now`

### ToContextLine

```csharp
public string ToContextLine() =>
    $"Müşteri {CustomerId}: {Summary ?? "kayıt yok"}, dil={PreferredLanguage}, tone={PreferredTone}";
```

Specialist prompt'una eklenir — agent müşteriyi tanıyarak yanıt verir.

---

## MemoryDocument

Vector store'da (Qdrant) tutulan **semantik hafıza** birimi:

```csharp
public sealed class MemoryDocument
{
    public string Id { get; init; }
    public MemoryKind Kind { get; init; }              // Episodic | Lesson | Knowledge
    public string Text { get; init; }                  // Embed edilecek metin

    public string? Title { get; init; }
    public string? Source { get; init; }
    public string? SessionId { get; init; }
    public Dictionary<string, string> Tags { get; init; } = new();
    public DateTime CreatedAt { get; init; }
}

public enum MemoryKind
{
    Episodic,     // Konuşma anısı (specific olay)
    Lesson,       // İyileştirme dersi (LessonMiner üretir)
    Knowledge     // Statik KB içeriği (markdown, PDF)
}
```

### Kind'lere göre kullanım

| Kind | Kaynak | Ne için kullanılır |
|---|---|---|
| `Episodic` | Konuşma sonu | "Müşteri ORD-5 hakkında konuştu" |
| `Lesson` | LessonMiner | "Stok kontrolünden önce ürün adı doğrulanmalı" |
| `Knowledge` | KB dosyaları (.md/.pdf) | Ürün dokümantasyonu, politikalar |

### MemorySearchHit

```csharp
public sealed record MemorySearchHit(MemoryDocument Document, float Score);
```

`ISemanticMemoryService.SearchAsync` döner — score, embedding cosine similarity.

### Tags

Filtreleme için key-value:

```csharp
new MemoryDocument {
    Kind = MemoryKind.Episodic,
    Tags = {
        ["customer_id"] = "12345",
        ["intent"] = "Complaint",
        ["resolved"] = "true"
    }
}
```

Search query'sinde `tag=customer_id:12345` filtresi kullanılabilir.

---

## Lesson (Self-Improvement Loop)

`LessonMiner` üretir; admin onayından sonra `Knowledge` olarak embed edilir.

```csharp
public sealed class Lesson
{
    public string Id { get; init; }
    public string Title { get; set; }
    public string LessonText { get; set; }              // "Ne öğrendik"
    public string Observation { get; set; }             // "Neyden gözlemledik"
    public string? SuggestedAgent { get; set; }         // "OrderAgent"

    public List<string> SourceTraceIds { get; init; } = new();
    public LessonStatus Status { get; set; }            // Proposed | Approved | Rejected
    public DateTime CreatedAt { get; init; }

    // Karar
    public string? DecidedBy { get; set; }              // Admin user id
    public DateTime? DecidedAt { get; set; }
    public string? DecisionReason { get; set; }

    // Approved ise
    public string? VectorMemoryId { get; set; }         // Qdrant ID
}

public enum LessonStatus { Proposed, Approved, Rejected }
```

### Yaşam döngüsü

```
LessonMiner.MineAsync()
   → Düşük rating + error trace + sanity Error → candidate traces
   → LLM analiz → Lesson { Status=Proposed }
   ↓
Admin paneli:
   → Approve → VectorMemoryId set + Knowledge embed
   → Reject  → DecisionReason kaydedilir
   ↓
Approved lesson sonraki konuşmalarda specialist prompt'a girer (semantic search hit)
```

### SourceTraceIds

Bir Lesson **birden fazla trace**'ten türetilebilir (örn. 3 başarısız konuşma aynı pattern'i gösteriyor). LessonMiner trace'leri grupla → ortak ders çıkar.

### Maliyet kontrolü

LessonMiner default `take=8` (en fazla 8 trace) ile çalışır — büyük dataset'lerde maliyeti sınırlar. Daha fazla için admin manuel tetikler.

---

## Tüm akış (memory döngüsü)

```
Konuşma 1: Müşteri ORD-5 sordu → Episodic kayıt
Konuşma 2: Müşteri başka soru sordu → embed search → ORD-5 episodic hit → bot bağlam taşıyor

Trace analizi (haftalık):
   → LessonMiner → Lesson: "Stok kontrolü önce ürün adını doğrulamalı"
   ↓
Admin Approve
   → VectorStore'a Knowledge olarak eklenir
   ↓
Sonraki konuşma:
   → ProductInquiryAgent prompt → semantic search → Lesson hit → daha iyi yanıt
```

---

## Bağlantılar

- [Application LessonMiner](../application/LessonMiner.md)
- [Application CustomerProfileService](../application/CustomerProfileService.md)
