# Reasoning

Bu dokümanda sistemdeki **reasoning (düşünme)** mekanizmaları ayrıntılı anlatılır. Bu sistem "tek bir LLM çağrısı sorar-yanıtlar" yapmak yerine, **3 farklı katmanda explicit structured reasoning** üretir. Her katman kendi JSON şemasını kullanır, trace'e kaydedilir ve downstream kararlarını etkiler.

## Neden birden fazla reasoning katmanı?

Naif yaklaşım: "Ajanın kendisi düşünsün, yanıt versin." Bu yaklaşımın sorunları:

- **İzlenebilirlik yok** — Neden bu cevabı verdi bilemeyiz.
- **Hata ayıklama zor** — Yanlış tool çağrısı yapıldığında nerede bozuldu görmek imkansız.
- **Niyet-karar uyumsuzluğu** — Ajan kullanıcıyı yanlış anlayıp doğru araç çağırabilir (tersine doğru anlayıp yanlış araç çağırabilir).
- **Kalite güvencesi yok** — Hallucination veya eksik yanıt sessizce geçer.

Çözüm: **Her kritik karar öncesi/sonrası explicit structured reasoning** üret. Reasoning'i trace'e kaydet.

## 3 katmanlı reasoning

```
┌────────────────────────────────────────────────────────────────┐
│  Katman 1 — Global Reasoning (ReasoningService)                │
│  Model: o4-mini (reasoning_effort: medium)                     │
│  Çıktı: ReasoningResult JSON                                   │
│  Amaç: Ön-analiz — intent, requiredInfo, nextAction, rationale │
└────────────────────┬───────────────────────────────────────────┘
                     │ hint olarak
                     ▼
┌────────────────────────────────────────────────────────────────┐
│  Katman 2 — Planning Reasoning (PlanningAgent)                 │
│  Model: gpt-4o                                                 │
│  Çıktı: PlanningResult JSON                                    │
│  Amaç: Routing — hangi ajan + clarification gerek mi?          │
└────────────────────┬───────────────────────────────────────────┘
                     │
                     ▼
┌────────────────────────────────────────────────────────────────┐
│  Katman 3 — Specialist Reasoning (pre/post-tool)               │
│  Model: gpt-4o                                                 │
│  Çıktı: SpecialistReasoning JSON (preToolCheck + post…)        │
│  Amaç: Parametre doğrulama + tool sonrası değerlendirme        │
└────────────────────────────────────────────────────────────────┘
```

---

## Katman 1 — Global Reasoning (ReasoningService)

**Dosya**: `@Services/ReasoningService.cs`
**Prompt**: `Prompts/services/reasoning-system.md` (+ koşullu `reasoning-history-note.md`)
**Çıktı modeli**: `Models/ReasoningResult.cs`

### Ne zaman çalışır?

Her `/chat/` veya `/chat/stream` isteğinde workflow'dan **ÖNCE** çalışır. Çıktısı workflow'a "hint" olarak enjekte edilir.

### Neden ayrı bir servis/model?

O-series (o4-mini, o1) modeller **daha uzun iç düşünme** süresine sahiptir — "thinking tokens" üretirler, sonra cevaplarlar. Niyet tespiti, eksik bilgi çıkarımı ve plan üretmek için bu düşünme faydalıdır. Ancak her tool çağrısında bu maliyete katlanmak istemeyiz, o yüzden asıl workflow **daha hızlı gpt-4o** ile yürütülür.

### Girdi

`@Services/ReasoningService.cs:232-253`:

```
[System] reasoning-system.md (STATE_INFO + HISTORY_NOTE placeholder'ları ile render edilmiş)
[User]   … önceki tur …
[Assist] … önceki tur …
[User]   güncel query
```

`STATE_INFO` içeriği:

```
CustomerId: CUST-001, Phase: inquiry, TurnCount: 3
```

`HISTORY_NOTE` — `history.Count > 0` ise enjekte edilir (`reasoning-history-note.md`'den): "BAĞLAM NOTU: Önceki turlarda geçen order_id/customer_id entity'lerini KULLAN; requiredInfo'da tekrar isteme."

### Çıktı şeması

```json
{
  "analysis": "Müşteri ORD-1 siparişinin durumunu sorguluyor.",
  "steps": [
    "Adım 1: order_status_tool'u ORD-1 ile çağır",
    "Adım 2: durumu kullanıcıya ilet"
  ],
  "intent": "sipariş_sorgulama",
  "requiredInfo": [],
  "rationale": "Kullanıcı net bir sipariş numarası verdi, clarification gerekmiyor.",
  "assumptions": ["Kullanıcının bu siparişin sahibi olduğu"],
  "nextAction": "OrderInquiryAgent'e yönlendir",
  "decisionReason": "ComplaintAgent alternatif değil çünkü şikayet iması yok.",
  "confidenceScore": 0.92
}
```

### Alanların anlamı

| Alan | Amaç |
|---|---|
| `analysis` | Kullanıcı ne istiyor? (1-2 cümle özet) |
| `steps` | Çözüm için atılacak adımlar (sıralı) |
| `intent` | `sipariş_oluşturma | sipariş_sorgulama | ürün_bilgisi | şikayet | genel | bilinmiyor` |
| `requiredInfo` | **Gerçekten** eksik alanlar; ZATEN bilinenleri tekrar isteme |
| `rationale` | Niyet+plan seçim gerekçesi |
| `assumptions` | Yapılan varsayımlar (transparency için) |
| `nextAction` | Somut bir sonraki aksiyon |
| `decisionReason` | Alternatifler + neden seçilmedikleri |
| `confidenceScore` | 0.0-1.0 sayısal güven |

`Confidence` string'i (yüksek/orta/düşük) legacy uyumluluk için hâlâ modelde; yeni kod `ConfidenceScore` kullanmalı. `ReasoningResult.ScoreToString` / `StringToScore` dönüşümleri mevcut.

### PlanningAgent'a nasıl iletilir?

`@Agents/CustomerSupportTeam.cs:590-612` — `BuildReasoningHint(r)` metodu boş olmayan alanları satır satır birleştirip `Prompts/services/reasoning-hint.md` template'ine `{{REASONING_LINES}}` placeholder'ı olarak geçer. Bu hint bir system mesajı olarak workflow'un başına eklenir.

Örnek hint (kullanıcı mesajından ÖNCE PlanningAgent'ın görecekleri):

```
[ÖN-ANALİZ REASONING ÇIKTISI — yalnızca bilgilendirme amaçlı]
Aşağıdaki ön-analiz kullanıcı sorgusu üzerinde yapıldı. …

- Analiz: Müşteri ORD-1 siparişinin durumunu sorguluyor.
- Ön-tahmin edilen niyet: sipariş_sorgulama
- Önerilen adımlar: order_status_tool çağır → durumu ilet
- Gerekli olduğu tahmin edilen bilgiler:
- Önerilen sonraki aksiyon: OrderInquiryAgent'e yönlendir

ÖNEMLİ KURALLAR:
  - Clarification sorusu üreteceksen …
  - Eğer 'Gerekli bilgiler' listesinde 1'den fazla alan varsa …
```

PlanningAgent bu hint'i "ön bilgi" olarak görür, farklı karar verebilir ama `rationale` alanında gerekçesini belirtmesi beklenir.

### Hata yönetimi

Reasoning başarısız olursa (API hatası, timeout, JSON parse hatası) **fallback** ile devam edilir:

```
Analysis = "Reasoning şu anda kullanılamıyor."
Intent = session.State.CurrentIntent ?? "bilinmiyor"
ConfidenceScore = 0.3
NextAction = "workflow'a düşük güvenle devam et"
```

Bu sayede reasoning katmanı down olsa bile workflow çalışmaya devam eder — sadece hint olmadan.

### Streaming

`ReasonStreamingAsync` SSE için tasarlandı. O-series modellerde "thinking" evresi sessiz olduğundan, thinking sonrası JSON hızlı akıtılır. Biz token'lar arası minimum 20ms pacing uygulayarak kullanıcıya progressive reasoning görseli sunarız (`@Services/ReasoningService.cs:122-163`).

---

## Katman 2 — Planning Reasoning (PlanningAgent)

**Prompt**: `Prompts/agents/planning-agent.md`
**Çıktı modeli**: `Models/PlanningResult.cs`
**Parser**: `Services/PlanningResultParser.cs`

### Katman 1'den farkı

| | Global Reasoning | Planning Reasoning |
|---|---|---|
| Model | o4-mini (ayrı client) | gpt-4o (ana client) |
| Bağlam | Sadece sorgu + history | + context + reasoning hint + entity hint |
| Çıktı odağı | Niyet analizi + plan | **Seçim** (hangi ajan, clarification?) |
| Side effect | Yok | Workflow'u yönlendirir |

### Çıktı şeması

`@Models/PlanningResult.cs`:

```json
{
  "detectedIntent": "sipariş_sorgulama",
  "intentConfidence": 0.95,
  "supportingEvidence": ["siparişim nerede", "ORD-1"],
  "selectedAgent": "OrderInquiryAgent",
  "rationale": "Net sipariş numarası + durum sorgusu niyeti.",
  "alternativesRejected": [
    { "agent": "ComplaintAgent", "reason": "Şikayet iması yok." },
    { "agent": "OrderPlacementAgent", "reason": "Yeni sipariş değil, sorgu." }
  ],
  "needsClarification": false,
  "clarificationQuestion": null,
  "taskDescription": "ORD-1 siparişinin durumunu sorgula"
}
```

### Clarification eşiği

`intentConfidence < 0.7` → `needsClarification=true` + `selectedAgent=ResponseAgent` üretilmeli. ChatManager bunu görürse specialist'i atlayıp doğrudan ResponseAgent'ı çağırır:

```csharp
@Agents/CustomerSupportChatManager.cs:91-96
if (plan.NeedsClarification || plan.IntentConfidence < 0.7)
{
    var responseAgent = _agents.FirstOrDefault(a =>
        a.Name?.Equals("ResponseAgent", StringComparison.OrdinalIgnoreCase) == true);
    if (responseAgent != null) return responseAgent;
}
```

### alternativesRejected neden zorunlu?

"Negatif gerekçe" izlenmesi için. Ajan doğru kararı verdi ama yanlış alternatifleri **eleyemedi** ise bu bir eğitim sinyalidir — trace dashboard'unda alternativesRejected boşsa prompt iyileştirilmelidir.

---

## Katman 3 — Specialist Reasoning (pre/post-tool)

**Prompt**: `Prompts/agents/<specialist>-agent.md`
**Çıktı modeli**: `Models/SpecialistReasoning.cs`
**Parser**: `Services/SpecialistReasoningParser.cs`

Her specialist (ProductInquiry, OrderPlacement, OrderInquiry, Complaint) tool çağrısı **etrafında** iki JSON bloğu üretir:

### 3a — Pre-tool check

**Amacı**: Tool çağrılmadan önce doğrulama. Eksik parametreyle yan-etkili tool çağırmayı engeller.

```json
{
  "preToolCheck": {
    "requiredParams": ["customer_id", "product_id", "quantity"],
    "collectedParams": ["CUST-001", "Dell XPS 15"],
    "missingParams": ["quantity"],
    "canProceed": false,
    "reasoning": "Adet belirtilmemiş, kullanıcıdan istenmeli.",
    "confidence": 0.9
  }
}
```

`canProceed=false` ise specialist **tool çağırmaz**, `postToolReflection.status=needs_followup` üretip ResponseAgent'a geçer.

### 3b — Post-tool reflection

**Amacı**: Tool'un sonucunu değerlendirir, sonraki adımı önerir.

```json
{
  "postToolReflection": {
    "taskComplete": true,
    "status": "done",
    "handoffSuggestion": "ResponseAgent",
    "handoffReason": "Sipariş başarıyla oluşturuldu.",
    "missingContext": [],
    "summary": "ORD-3 oluşturuldu, kullanıcıya iletildi."
  }
}
```

### `status` değerleri ve anlamı

| Status | Anlamı | ChatManager davranışı |
|---|---|---|
| `done` | Görev tamamlandı | ResponseAgent'a geç |
| `needs_followup` | Kullanıcıdan bilgi gerek | ResponseAgent (clarification) |
| `needs_escalation` | İnsan desteği gerek | ResponseAgent (escalation mesajı) |
| `failed` | Tool hatası | ResponseAgent (hata mesajı) |
| `partial` | Kısmi sonuç (bulunamadı vb.) | ResponseAgent (not_found mesajı) |

### Dinamik handoff

`handoffSuggestion` başka bir specialist ismiyse ChatManager **ping-pong guard**'ını kontrol eder (aynı ajana max 2 handoff) ve yönlendirme yapar:

```csharp
@Agents/CustomerSupportChatManager.cs:127-144
if (!string.IsNullOrWhiteSpace(reflection.HandoffSuggestion)
    && !reflection.HandoffSuggestion.Equals("ResponseAgent", …))
{
    _handoffCounts.TryGetValue(targetName, out var count);
    if (count < MaxHandoffsPerAgent)  // = 2
    {
        _handoffCounts[targetName] = count + 1;
        return target;
    }
    // Limit aşıldı → ResponseAgent fallback
}
```

---

## Trace'e yansıma

Tüm bu katmanların çıktıları tek bir `ReasoningTrace` nesnesinde birleştirilir:

```csharp
@Models/ReasoningTrace.cs
public class ReasoningTrace
{
    public string TraceId { get; set; }
    public string SessionId { get; set; }
    public string UserQuery { get; set; }
    public DateTime StartedAt, CompletedAt;

    public ReasoningResult? Reasoning { get; set; }              // Katman 1
    public PlanningResult? Planning { get; set; }                // Katman 2
    public List<SpecialistReasoning> SpecialistReasonings { … }  // Katman 3

    public List<AgentVisit> AgentVisits { … }
    public List<ToolInvocation> ToolCalls { … }
    public string? TerminationReason { get; set; }
    public string? FinalResponse { get; set; }
    public int IterationCount { get; set; }
}
```

Trace'e `GET /traces/{id}` veya `GET /traces/recent?count=20` ile erişilebilir (`@Endpoints/TraceEndpoints.cs`).

### Aggregate stats

`GET /traces/stats` — tüm trace'lerin istatistiği:

```json
{
  "totalTraces": 234,
  "completedCount": 228,
  "errorCount": 6,
  "avgDurationMs": 3420.5,
  "avgIterationCount": 4.2,
  "terminationReasons": {
    "completed": 180,
    "awaiting_user_input": 30,
    "not_found": 12,
    "escalation_needed": 4,
    "repeated_tool_call_guard": 2,
    "timeout": 0,
    "error": 6
  }
}
```

---

## Reasoning'in gerçek hayattaki etkisi (örnekler)

### Örnek 1 — Clarification ping-pong önleme

**Kullanıcı**: "Sipariş vermek istiyorum."

**Naif sistem**: 3 tur

1. "Hangi ürünü?" → kullanıcı "Dell XPS"
2. "Kaç adet?" → kullanıcı "2"
3. "Müşteri kimliğiniz?" → kullanıcı "CUST-001"

**Bu sistem**: 1 tur (reasoning + planning + clarification)

- Reasoning: `requiredInfo = ["müşteri_kimliği", "ürün_adı", "adet"]`
- Planning hint'i okur, `clarificationQuestion`: "Sipariş için ürün adı, adet ve müşteri kimliğinizi birlikte paylaşır mısınız?"
- Response: tek mesajda hepsini ister

Ping-pong azaltır, kullanıcı deneyimi iyileşir, token tasarrufu sağlar.

### Örnek 2 — Tool seçim önceliği

**Kullanıcı**: "ORD-1 siparişim nerede?"

**Naif sistem**: Ajan belki tool seçmekte karar verir, belki customer_id ister.

**Bu sistem** — üç katmanlı tutarlılık:

- `IdExtractor` → `order_id=ORD-1` deterministik olarak çıkarır
- Hint mesajı: "order_id VAR → order_status_tool kullan, customer_id İSTEME"
- Reasoning (o4-mini): `intent=sipariş_sorgulama, requiredInfo=[], nextAction=OrderInquiryAgent'e yönlendir`
- Planning (gpt-4o): `selectedAgent=OrderInquiryAgent, needsClarification=false`
- OrderInquiryAgent: `order_status_tool(ORD-1)` → direkt tool çağırır

Ajan "hangi aracı seçmeliyim?" diye tereddüt etmez.

### Örnek 3 — Hallucination yakalama

**Specialist output**: "Sipariş bulunamadı." (`ORDER_NOT_FOUND`)

ResponseAgent prompt'unda hallucination sıfır tolerans kuralı vardır: "Specialist çıktısında OLMAYAN veri/numara üretme. Uydurma sipariş/müşteri/ürün numarası YASAK." Bu kural sayesinde ResponseAgent doğrudan doğru yanıtı üretir:
"Üzgünüm, 'ORD-1' numaralı siparişi sistemimizde bulamadım. Sipariş numarasını kontrol eder misiniz?"

---

## Özet

| Katman | Model | Girdi | Çıktı | Tetiklenme | Hata toleransı |
|---|---|---|---|---|---|
| Global Reasoning | o4-mini | Sorgu + history + state | `ReasoningResult` | Her istekte | Fallback boş sonuç |
| Planning | gpt-4o | +Context +Hint +Entities | `PlanningResult` | Her istekte | Parser null → varsayılan fallback |
| Specialist Pre/Post | gpt-4o | +Tool sonucu | `SpecialistReasoning` | Specialist aktivasyonunda | Pre-check fail → tool skip |

Bu 3 katman birlikte çalışarak **explicit reasoning + kalite gates + izlenebilirlik** sağlar.

---

# Enhanced Reasoning Pipeline

> **Soru**: *"Bizim yaptığımız reasoning gerçek reasoning mi?"*
>
> **Cevap**: Önceki hali *structured planning* idi — model tek çağrıda bir plan tarif ediyordu ama adımlar **yürütülmüyor**, **doğrulanmıyor**, alternatifler **aranmıyordu**. Bu eksikleri kapatmak için 4 geliştirme eklendi.

## Yeni pipeline şeması

```
User query
    ↓
┌──────────────────────────────────────────────────────────────────┐
│  Katman 0 — Entity Verification (deterministic, no LLM)          │
│  Servis: EntityVerifier                                          │
│  Input:  query + history + session state                         │
│  Output: VerifiedEntities { OrderId, CustomerId, ComplaintId,    │
│                              DerivedLastOrderId, ...}            │
│  Amaç:   Query'deki ID'leri extract + DB ile doğrula; reasoning  │
│          modelinin "zaten bilinen" alanları requiredInfo'ya      │
│          eklemesini engelle.                                     │
└──────────────────────┬───────────────────────────────────────────┘
                       │ verifiedBlock → prompt'a enjekte
                       ▼
┌──────────────────────────────────────────────────────────────────┐
│  Katman 1 — Global Reasoning (o4-mini)  [GENİŞLETİLDİ]           │
│  Çıktı: ReasoningResult JSON + YAPILANDIRILMIŞ STEPS + SUB-TASKS │
│    • steps[]: { order, description, action, premise, grounding,  │
│                confidence, alternativeRejected }                  │
│    • subTasks[]: { order, intent, targetAgent, entities, deps }  │
└──────────────────────┬───────────────────────────────────────────┘
                       │
                       ▼
┌──────────────────────────────────────────────────────────────────┐
│  Katman 1.5 — Sanity Checker (deterministic, no LLM)  [YENİ]     │
│  Servis: ReasoningSanityChecker                                  │
│  Input:  ReasoningResult + VerifiedEntities                      │
│  Output: List<ReasoningIssue> → result.SanityIssues              │
│  Amaç:   Mantık tutarsızlıklarını yakala (8 kural)               │
└──────────────────────┬───────────────────────────────────────────┘
                       │ hint + issues → PlanningAgent
                       ▼
  [Mevcut 2-3. katmanlar: Planning → Specialist → Response]
```

## 1. Entity Grounding (ReAct-lite)

**Dosyalar**:

- `@Models/VerifiedEntities.cs`
- `@Services/EntityVerifier.cs`

**Problem**: Reasoning modeli query'deki *"ORD-9999"* ifadesini gördüğünde, sadece formatın doğruluğuna bakıyordu — DB'de gerçekten var olup olmadığını kontrol etmiyordu. Bu *hallucination* kaynağıydı.

**Çözüm**: `EntityVerifier`, reasoning LLM çağrısından **önce** çalışır:

1. `IdExtractor.Extract(query)` — regex ile ID'leri çıkar
2. History'deki önceki turlardan eksikleri tamamla
3. `session.State.CustomerId` varsa onu da ekle
4. Her entity için `FakeDatabase`'te lookup yap:
   - `Verified` — format + DB'de var
   - `NotFoundInDb` — format doğru ama DB'de yok
   - `FormatOnly` — DB lookup uygulanmadı
5. Türetilmiş alanları hesapla (ör. `customer_id` Verified ise `DerivedLastOrderId = GetLastOrder(customerId)`)

### Prompt'a nasıl enjekte edilir?

`Prompts/services/reasoning-system.md`'de `{{VERIFIED_ENTITIES}}` placeholder'ı yerine şu blok gelir:

```
[VERIFIED ENTITIES — session/DB ile doğrulandı]
Aşağıdaki bilgiler ZATEN elinizde. requiredInfo'ya EKLEMEYİN, kullanıcıdan tekrar İSTEMEYİN.
- order_id = "ORD-1" [VERIFIED, source=Query, status=Kargolandı, product=Dell XPS 15, customerId=1990]
- customer_id = "CUST-1990" [VERIFIED, source=SessionState, has_orders=true]
- last_order_id = "ORD-1" [derived: customer_id'nin en son siparişi]
```

Reasoning prompt'u ayrıca buna uygun **grounding kuralları** içerir:

> **VERIFIED** entity için `requiredInfo`'ya ekleme — zaten elinde.
> **NOT_FOUND_IN_DB** için `nextAction = "kullanıcıya doğrulat"`, `confidenceScore` düşür.
> **Derived** alanlar VERIFIED kabul — kullanıcıya tekrar sorma.

### Etkisi

- Hallucination riski düşer (DB'den gelen değerler yazılı).
- Gereksiz clarification'lar engellenir (VERIFIED alan zaten bilinir).
- Sanity checker buna dayanarak `redundant_required_info` ve `not_found_ignored` kurallarını uygular.

---

## 2. Structured Steps

**Dosya**: `@Models/ReasoningStep.cs`

**Önceki hal**:

```json
"steps": ["Adım 1: order_id çıkardım", "Adım 2: OrderInquiryAgent'a yönlendirdim"]
```

**Yeni hal**:

```json
"steps": [
  {
    "order": 1,
    "description": "order_id mesajdan regex ile çıkarıldı",
    "action": "extract",
    "premise": null,
    "grounding": "regex",
    "confidence": 0.95,
    "alternativeRejected": null
  },
  {
    "order": 2,
    "description": "ORD-1 için OrderInquiryAgent'a yönlendir",
    "action": "route",
    "premise": "order_id mevcut ve VERIFIED",
    "grounding": "DB",
    "confidence": 0.92,
    "alternativeRejected": "get_last_order_tool (spesifik ID var, gereksiz)"
  }
]
```

### Neden yararlı?

- **`grounding` alanı** — her adımın kanıt kaynağını işaret eder. `assumption` değeri sanity checker için kırmızı bayrak.
- **`confidence` per-step** — final `confidenceScore`'un bağımsız kontrolü; min/çarpım ile türetilebilir.
- **`alternativeRejected`** — modelin niye o yolu seçmediğini yazar; daha iyi debuggable karar kayıtları.
- **`premise`** — adımın önkoşulu explicit; ihlal olursa adım geçersiz.

### Parser + geriye dönük uyumluluk

`@Services/ReasoningService.cs:399-458` içindeki `ParseSteps` helper'ı **hem** object array hem legacy string array formatını destekler. Model eski tarz dönerse (`["Adım 1: ..."]`), her string otomatik olarak `ReasoningStep.Description`'a wrap edilir.

### Frontend render

`@wwwroot/js/chat-ui.js`:

- `getStepText(step)` — text veya `description` çıkarımı (legacy/yeni dual-shape)
- `renderStepInnerHtml(step)` — zengin chip'li HTML: `grounding` (normal/warn), `confidence` (yeşil %), `action` (mor monospace)

CSS: `@wwwroot/css/styles.css` `.step-chip` ve türevleri.

---

## 3. Sanity Checker (deterministic rules)

**Dosya**: `@Services/ReasoningSanityChecker.cs`
**Model**: `@Models/ReasoningIssue.cs`

Reasoning LLM çağrısından **sonra** deterministic kural tabanlı tarama. **Sıfır ekstra LLM çağrısı**. Bulunan her tutarsızlık `ReasoningIssue` olarak `result.SanityIssues` listesine eklenir ve trace'e yazılır.

### 8 kural

Her kural ayrı bir `IReasoningSanityRule` implementasyonudur (Strategy pattern). Yeni kural eklemek için sınıf yaz + `_rules` listesine ekle.

| # | Kural sınıfı | Kod | Severity | Tetiklenme |
|---|---|---|---|---|
| 1 | `OverconfidentClarificationRule` | `overconfident_clarification` | warn | `confidenceScore >= 0.7` AMA `nextAction` clarification istiyor |
| 2 | `RedundantRequiredInfoRule` | `redundant_required_info` | **error** | `requiredInfo`'da VERIFIED entity var (→ ping-pong) |
| 3 | `IntentActionMismatchRule` | `intent_action_mismatch` | warn | Intent ile seçilen agent çelişiyor (ör. intent=şikayet + action=OrderInquiry) |
| 4 | `LowConfidenceNoMissingRule` | `low_confidence_no_missing` | info | `confidenceScore < 0.5` AMA `requiredInfo=[]` (neden düşük güven?) |
| 5 | `AssumptionHeavyStepsRule` | `assumption_based_step` | info | Bir veya daha fazla step'te `grounding=assumption` |
| 6 | `OverconfidentAssumptionsRule` | `overconfident_assumptions` | warn | `confidenceScore >= 0.8` AMA `assumptions.Count >= 3` |
| 7 | `NotFoundIgnoredRule` | `not_found_ignored` | **error** | NOT_FOUND_IN_DB entity var AMA `nextAction` doğrulatmıyor (→ hallucination riski) |
| 8 | `SubTasksIgnoredRule` | `subtasks_ignored` | warn | `subTasks.Count >= 2` (farklı agent'lar) AMA `nextAction` hepsinden bahsetmiyor |

### Issue formatı

```json
{
  "code": "redundant_required_info",
  "severity": "error",
  "message": "requiredInfo[0]='sipariş_numarası' ama order_id zaten VERIFIED — ping-pong olur.",
  "field": "requiredInfo[0]",
  "suggestedFix": "requiredInfo'dan 'sipariş_numarası' kaldır; order_id zaten elinde."
}
```

### Frontend

Chat UI reasoning panelinde ayrı bir "Tutarsızlık kontrolleri" bölümü:

- `⛔ error` → kırmızı
- `⚠️ warn` → sarı
- `ℹ️ info` → mavi

Her issue: kod badge + mesaj + *"→ suggestedFix"*.

### İleri: otomatik düzeltme?

Şu an **bilgilendirme modunda** — issues sadece işaretlenir, workflow akışı değişmez. Sonraki iterasyonda:

- Severity=Error ise reasoning'i ikinci pass'te LLM critique'e gönder
- Auto-downgrade: `overconfident_clarification` tespit edildiğinde confidence'ı kod tarafında düşür

---

## 4. Sub-task Decomposition

**Dosya**: `@Models/SubTask.cs`

### Problem

Compound query: *"ORD-1 nerede ve ORD-2 için şikayet açmak istiyorum"*

Eskiden: Reasoning tek intent seçiyor (örn. `şikayet`), ilk görev unutuluyor veya ping-pong oluyor.

### Çözüm

Reasoning modelinin `subTasks[]` alanında query'yi ayrıştırması:

```json
"subTasks": [
  { "order": 1, "intent": "sipariş_sorgulama", "description": "ORD-1 için durum sorgula",
    "targetAgent": "OrderInquiryAgent", "entities": { "order_id": "ORD-1" }, "dependencies": [] },
  { "order": 2, "intent": "şikayet", "description": "ORD-2 için şikayet aç",
    "targetAgent": "ComplaintAgent", "entities": { "order_id": "ORD-2" }, "dependencies": [] }
]
```

### Decomposition kuralları (`Prompts/services/reasoning-system.md`)

- Query tek niyetliyse `subTasks: []` (boş).
- *" ve "*, *"sonra"*, *"ayrıca"*, iki farklı ID varsa → decompose et.
- Aynı niyet içinde çoklu parametre (*"ORD-1 ve ORD-2'nin durumu"*) → decompose **etme**, tek görev.
- `targetAgent` mutlaka 4 specialist'ten biri olmalı.

### Workflow'a nasıl iletilir?

`@Agents/CustomerSupportTeam.cs:607-621` — `BuildReasoningHint`, subTasks varsa PlanningAgent'a şu formatta enjekte eder:

```
- ⚠️ COMPOUND QUERY: 2 alt göreve ayrıştırıldı. PlanningAgent olarak her birini SIRAYLA aynı yanıtta yönlendir:
    1. OrderInquiryAgent: ORD-1 için sipariş durumu sorgula [order_id=ORD-1]
    2. ComplaintAgent: ORD-2 için şikayet aç [order_id=ORD-2]
```

PlanningAgent prompt'u (`Prompts/agents/planning-agent.md`) buna uygun davranacak şekilde güncellendi:

- `taskDescription` tüm alt görevleri özetler
- Routing bölümünde her alt görev için ayrı satır
- `selectedAgent` = ilk alt görevin agent'ı (GroupChat sıralama için)

ResponseAgent prompt'u (`Prompts/agents/response-agent.md`) compound query yanıtını **maddelenmiş** yazmakla yükümlü:

> 1) ORD-1 için: ...
> 2) ORD-2 için: ...

### Compound query — Tam orkestrasyon (tamamlandı)

MAF GroupChat native multi-agent sequential routing desteklemediği için orkestrasyon **kod katmanında** yapılır. `CustomerSupportTeam` compound query algıladığında her subtask için ayrı bir workflow run çalıştırır.

#### Akış

```
RunAsync(query, reasoning)
    │
    ├─ ShouldDecompose(reasoning)?                       ← bool
    │    reasoning.SubTasks.Count >= 2 AND
    │    DistinctAgents(SubTasks) >= 2
    │
    ├─[false]→  Tek workflow run (mevcut akış)
    │
    └─[true]──→ RunDecomposedAsync
                 │
                 ├─ subTask #1
                 │    ├─ subReasoning = DeriveSubReasoning(parent, subTask)
                 │    │    (SubTasks=[] ← recursive loop koruması)
                 │    ├─ subQuery = BuildSubQuery(subTask)  // desc + entities
                 │    └─ RunAsync(subQuery, history, session, subReasoning)  // recursive
                 │         └─ Tam planning + specialist + response döngüsü
                 │
                 ├─ subTask #2 (önceki response history'ye eklendi)
                 │    └─ ...
                 │
                 └─ JoinAggregatedParts(results)
                      └─ "**1) <desc>**\n\n<response>\n\n---\n\n**2) <desc>**\n\n<response>"
```

#### Helper'lar (`@Agents/CustomerSupportTeam.cs:912-1192`)

| Helper | Amaç |
|---|---|
| `ShouldDecompose(ReasoningResult?)` | SubTasks ≥ 2 ve farklı agent'lar → true |
| `DeriveSubReasoning(parent, subTask)` | Mini reasoning (SubTasks=[] → recursion safe) |
| `BuildSubQuery(subTask)` | *"Description (entity1=val1, entity2=val2)"* |
| `FormatSubResult(subTask, response)` | `**{order}) {desc}**\n\n{response}` |
| `JoinAggregatedParts(parts)` | `\n\n---\n\n` ile birleştir |
| `RunDecomposedAsync(...)` | Non-streaming orkestrasyon |
| `RunDecomposedStreamingAsync(...)` | Streaming orkestrasyon — agent events forward + final aggregated response |

#### Streaming davranışı

`RunStreamingAsync` da `ShouldDecompose` kontrolü yapar. Compound query'de:

1. **Başlangıç**: `agent` event — `{ name: "Orchestrator", status: "decomposing", subTaskCount: N }`
2. **Her subtask için**:
   - `agent` event — `{ name: "SubTask#1", status: "running", description, targetAgent, order, total }`
   - İç `RunStreamingAsync` consume edilir:
     - Agent events → frontend'e forward
     - Reasoning events → yutulur (subtask seviyesinde görünmesin)
     - Response start/complete → yutulur (final aggregated'ta tek başına)
     - Response delta → `subResponseBuilder`'a biriktirilir
   - `agent` event — `{ name: "SubTask#1", status: "done", order }`
3. **Son**:
   - `agent` event — `{ name: "Orchestrator", status: "aggregating" }`
   - `response_start` — `{ decomposed: true, subTaskCount: N }`
   - `response_delta` chunk'ları (birleştirilmiş metin)
   - `response_complete` — `{ text, decomposed: true, ... }`

Böylece UI:

- Her subtask için ayrı agent transition göstergesi görür
- Tek bir birleştirilmiş yanıt bloğu render eder
- `decomposed` flag'iyle compound badge ekleyebilir

#### Maliyet

| Senaryo | LLM çağrısı |
|---|---|
| Normal (tek görev) | 5 (reasoning + planning + specialist + critique + optional revision) |
| 2 subtask compound | ~9 (1 reasoning + 2×planning + 2×specialist + 2×critique + optional 2×revision) |
| N subtask | ~1 + 4N |

Üst düzey reasoning (compound algılayan) yalnızca **bir kere** çalışır. Sonraki her subtask kendi planning+specialist+response döngüsünü yapar. Bu, bir kerelik reasoning maliyetini amortize eder.

#### Continuity

Her subtask önceki subtask'ın sonucunu `conversationHistory`'ye ekli olarak görür:

```csharp
runningHistory.Add(new ChatMessage(ChatRole.User, subQuery));
runningHistory.Add(new ChatMessage(ChatRole.Assistant, subResponse));
```

Böylece örneğin *"ORD-1'i sor, sonra ORD-2 için şikayet aç"* senaryosunda ikinci subtask ilk sonucu bilir ve *"ORD-1'in kargoda olduğunu gördük"* diye refere edebilir.

### Frontend

Reasoning panelinde "Alt görevler" section (subTasks.length ≥ 2 ise):

- Her subtask: `#order` badge + `targetAgent` chip (mor) + `description` + entity chip'leri (yeşil)

Streaming tarafında:

- `agent` event'leri normal agent indicator'ları gibi render edilir (Orchestrator, SubTask#1, SubTask#2)
- Final response bloğu tek bir yanıt olarak gelir — maddelenmiş markdown formatında

---

## Özet Tablo

| Katman | Tip | LLM? | Input | Output |
|---|---|---|---|---|
| **0. Entity Verify** | Deterministic | ❌ | query + history + state | `VerifiedEntities` |
| **1. Global Reasoning** | LLM | ✅ o4-mini | + verified bloğu | `ReasoningResult` (structured steps + subTasks) |
| **1.5 Sanity Check** | Deterministic | ❌ | result + verified | `List<ReasoningIssue>` |
| 2. Planning | LLM | ✅ gpt-4o | + hint | `PlanningResult` |
| 3. Specialist Pre/Post | LLM | ✅ gpt-4o | + tool | `SpecialistReasoning` |

**Toplam katman**: 5 (2 deterministic + 3 LLM)
**LLM çağrı sayısı per request**: 4 (reasoning + planning + specialist + response)
**Deterministic kontrol**: 2 (EntityVerifier + SanityChecker) — **sıfır ekstra API maliyeti**.

## "Gerçek reasoning" denkliği

| Pattern | Öncesi | Sonrası |
|---|---|---|
| **Grounded reasoning (ReAct)** | ❌ Sadece regex format | ✅ DB lookup ile verified |
| **Structured reasoning trace** | ⚠️ String listesi | ✅ Object array + grounding + confidence |
| **Self-verification** | ⚠️ Yok | ✅ Deterministic sanity checker (reasoning'te) |
| **Decomposition** | ❌ Tek intent | ✅ SubTasks (model + prompt + UI) |
| **Iterative refinement** | ❌ | ✅ Compound query — her subtask için ayrı workflow run (orkestrasyon) |
| **Self-consistency (N samples)** | ❌ | ❌ (maliyet — 3-5x LLM çağrısı) |
| **Tree-of-Thoughts** | ❌ | ❌ (maliyet — derinlemesine arama) |

7/9 pattern kısmen veya tam olarak karşılanmış. Kalan Self-Consistency ve ToT açıkça maliyet nedeniyle dışarıda — production'da değer/maliyet oranı düşük.
