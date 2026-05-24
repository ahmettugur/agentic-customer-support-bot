# Reasoning Pipeline

Reasoning pipeline, her kullanıcı mesajı için **LLM'den önce çalışan 4 katmanlı analiz zinciridir.** Çıktısı olan `ReasoningResult`, hem `PlanningAgent`'a hint olarak hem de sanity check'e girdi olarak kullanılır.

## Katmanlar ve dosyalar

```
Kullanıcı sorgusu
    │
    ▼
[L0] EntityVerifier              → DB ile deterministik entity doğrulama
    │  EntityVerifier.cs
    │
    ▼
[L1] ReasoningMessageBuilder     → System prompt + history + entity bloğu hazırlama
    │  ReasoningMessageBuilder.cs
    │
    ▼
[L2] ReasoningService (LLM)      → o-series modele gönder, JSON al
    │  ReasoningService.cs
    │
    ▼
[L3] ReasoningSanityChecker      → 8 deterministik kural ile tutarsızlık tara
       ReasoningSanityChecker.cs
```

---

## EntityVerifier

**Dosya:** `Services/EntityVerifier.cs`  
**Tür:** Singleton, LLM çağrısı yapmaz

### Görevi

Kullanıcı sorgusundan ve konuşma geçmişinden `ORD-xxx`, `CMP-xxx`, `CUST-xxx` formatındaki ID'leri çıkarır; bunları DB'de doğrular; türetilmiş alanları hesaplar. Çıktısı `VerifiedEntities` nesnesidir.

### Öncelik sırası

```
Query > History (yeni mesajdan eskiye) > SessionState.CustomerId
```

### Doğrulama türleri

| `EntityVerification` | Anlamı |
|----------------------|--------|
| `Verified` | DB'de bulundu, attribute'lar dolu |
| `NotFoundInDb` | Formatlı ID var ama DB'de yok |
| `FormatOnly` | Sadece format doğru (müşteri için kullanılır) |

### Müşteri doğrulaması (özel durum)

`CUST-xxx` için ayrı müşteri tablosu yoktur. Müşteriye ait en az bir sipariş veya şikayet varsa `Verified` kabul edilir; yoksa `FormatOnly` kalır. Böylece yeni kayıt müşteriler için false-negative önlenir.

### Türetilmiş alanlar

`customerId` Verified ise otomatik hesaplanır:
- `DerivedLastOrderId` — müşterinin en son siparişi
- `DerivedOrderCount` — müşterinin toplam sipariş sayısı

Bu değerler LLM'nin "kaç siparişi var?" sorusunu gereksiz tool çağrısı yapmadan yanıtlamasını sağlar.

### `BuildPromptBlock`

```csharp
public static string? BuildPromptBlock(VerifiedEntities verified)
```

`VerifiedEntities`'i reasoning LLM'inin okuyacağı formata çevirir:

```
[VERIFIED ENTITIES — session/DB ile doğrulandı]
Aşağıdaki bilgiler ZATEN elinizde. requiredInfo'ya EKLEMEYİN, kullanıcıdan tekrar İSTEMEYİN.
- order_id = "ORD-4821" [VERIFIED, source=Query, status=shipped, product=Laptop, ...]
- customer_id = "CUST-12" [FORMAT_ONLY, source=SessionState]
⚠️ UYARI: Şu entity'ler DB'de bulunamadı: order_id=ORD-9999. Kullanıcıya kibarca doğrulatın.
```

---

## ReasoningMessageBuilder

**Dosya:** `Services/ReasoningMessageBuilder.cs`  
**Tür:** Singleton, LLM çağrısı yapmaz

### Görevi

Reasoning LLM'e gönderilecek mesaj listesini (`List<ConversationMessage>`) oluşturur.

### Mesaj sırası

```
[0] System: reasoning-system.md (STATE_INFO + HISTORY_NOTE + VERIFIED_ENTITIES enjekte edilmiş)
[1..N-1] Konuşma geçmişi (varsa)
[N-1?]  System: Replan hint (ForceReplanNextTurn=true ise)
[N]     User: güncel sorgu
```

### Enjekte edilen değişkenler

| Placeholder | Kaynak |
|------------|--------|
| `{{STATE_INFO}}` | `session.State.CustomerId`, `Phase`, `TurnCount` |
| `{{HISTORY_NOTE}}` | `reasoning-history-note.md` (history varsa) veya boş string |
| `{{VERIFIED_ENTITIES}}` | `EntityVerifier.BuildPromptBlock(verified)` |

### Admin "Yeniden Planla" notu

`session.State.ForceReplanNextTurn == true` ise `WellKnown.FallbackMessages.ReplanPlanningHint` + admin notunu system mesaj olarak ekler. State temizliği burada **yapılmaz** — Planning aşamasında (CustomerSupportTeam) yapılır.

---

## ReasoningService

**Dosya:** `Services/ReasoningService.cs`  
**Implements:** `IReasoningPort`  
**Yaşam döngüsü:** Singleton

### `ReasonAsync` (non-streaming)

```
1. EntityVerifier.Verify(query, session, history)
2. ReasoningMessageBuilder.Build(...)
3. IReasoningChatClient.CompleteAsync(messages)  ← o-series LLM
4. ReasoningResultParser.Parse(text)             ← JSON → ReasoningResult
5. ReasoningSanityChecker.Check(result, verified) → SanityIssues eklendi
6. return result
```

**Hata durumu:** LLM çağrısı başarısız olursa exception fırlatılmaz. Bunun yerine güven skoru 0.3 olan minimal bir `ReasoningResult` döner — workflow düşük güvenle devam eder.

### `ReasonStreamingAsync` (SSE streaming)

`ReasonAsync` ile aynı mantığı izler, fakat:
- `IReasoningChatClient.StreamAsync` kullanır
- Chunk'lar biriktirilerek `ReasoningDelta` event'leri yield edilir
- Min 20ms aralık uygulanır (frontend için hız kontrolü)
- Tüm metin toplandıktan sonra `ReasoningResultParser.Parse` çağrılır
- `ReasoningComplete` event'ine `ReasoningResult` nesnesi konulur

**Hata durumu:** Stream başlatılamazsa veya ortada kesilirse, `ConfidenceScore=0.3` olan fallback sonuç ile `ReasoningComplete` yield edilir.

### `EnumerateSafely` (özel)

`IAsyncEnumerable<string>` akışını `(Chunk, Error?)` tuple akışına sararak exception'ları context dışına taşımaz — `yield return` içinde exception fırlanamaz, bu yüzden error string olarak taşınır.

---

## ReasoningSanityChecker

**Dosya:** `Services/ReasoningSanityChecker.cs`  
**Tür:** Singleton, LLM çağrısı yapmaz

### Görevi

`ReasoningResult` + `VerifiedEntities` üzerinde 8 deterministik kural çalıştırır. Bulunan tutarsızlıklar `ReasoningIssue` listesi olarak `result.SanityIssues`'a eklenir.

### Kurallar

| Kural sınıfı | Kodu | Severity | Ne kontrol eder? |
|---|---|---|---|
| `OverconfidentClarificationRule` | `overconfident_clarification` | Warn | ConfidenceScore ≥ 0.7 iken nextAction clarification talep ediyor |
| `RedundantRequiredInfoRule` | `redundant_required_info` | **Error** | requiredInfo'da zaten VERIFIED olan entity var |
| `IntentActionMismatchRule` | `intent_action_mismatch` | Warn | Intent ile seçilen agent çelişiyor |
| `LowConfidenceNoMissingRule` | `low_confidence_no_missing` | Info | Güven < 0.5 ama requiredInfo boş |
| `AssumptionHeavyStepsRule` | `assumption_based_step` | Info | Step grounding=assumption olan adımlar var |
| `OverconfidentAssumptionsRule` | `overconfident_assumptions` | Warn | Güven ≥ 0.8 ama 3+ varsayım var |
| `NotFoundIgnoredRule` | `not_found_ignored` | **Error** | DB'de bulunamayan entity var, model bunu görmezden geliyor |
| `SubTasksIgnoredRule` | `subtasks_ignored` | Warn | 2+ alt görev var ama nextAction hepsini içermiyor |

### Severity ne anlama gelir?

| Severity | Kullanım |
|----------|---------|
| `Error` | Ciddi tutarsızlık — workflow devam etse de yanıt kalitesi düşük olabilir |
| `Warn` | Potansiyel sorun — izlenebilir |
| `Info` | Bilgi amaçlı, normal akışta da oluşabilir |

> Şu an sanity issues yalnızca log'a ve trace'e yazılır; workflow bloklanmaz. İleride `Error` severity'de otomatik re-reasoning tetiklemek mümkündür.

### Yeni kural eklemek

1. `IReasoningSanityRule` arayüzünü implemente eden `sealed class` yazın.
2. `ReasoningSanityChecker` constructor'ındaki `_rules` listesine ekleyin.
3. Kural birim testini `CustomerSupportBot.Api.Tests` altına yazın.

```csharp
public sealed class YeniKuralRule : IReasoningSanityRule
{
    public string Code => "yeni_kural";

    public void Apply(ReasoningResult r, VerifiedEntities verified, List<ReasoningIssue> issues)
    {
        if (/* koşul sağlanmıyorsa */ true) return;

        issues.Add(new ReasoningIssue
        {
            Code = Code,
            Severity = IssueSeverity.Warn,
            Message = "...",
            Field = "...",
            SuggestedFix = "..."
        });
    }
}
```

---

## ReasoningResult yapısı (özet)

```csharp
public class ReasoningResult
{
    string Analysis        // Genel değerlendirme
    string Intent          // Tespit edilen niyet (order_inquiry, complaint, ...)
    double ConfidenceScore // 0.0 – 1.0
    string Confidence      // "low" | "medium" | "high"
    List<ReasoningStep> Steps   // Önerilen adım sırası
    List<string> RequiredInfo   // Eksik bilgiler
    List<string> Assumptions    // Varsayımlar
    string NextAction           // PlanningAgent için tavsiye
    List<SubTask> SubTasks      // Compound query ayrıştırması (≥2 ise)
    string? Sentiment           // "positive" | "neutral" | "negative"
    double SentimentScore
    List<ReasoningIssue> SanityIssues  // SanityChecker bulgular
}
```
