# Reasoning — Uygulamada Kullanılan Pattern'ler

Bu doküman botun **"akıl yürütme"** stratejisini anlatır: hangi reasoning pattern'leri kullanıldı, niçin seçildi, kod içinde nerede yaşıyor.

> Implementasyon detayları için: [`domain/Model-Reasoning.md`](domain/Model-Reasoning.md), [`application/ReasoningPipeline.md`](application/ReasoningPipeline.md), [`adapters-agents/`](adapters-agents/README.md).

---

## TL;DR — Reasoning katmanları

```
1. Deterministic Pre-Processing   (LLM'siz — IdExtractor, SessionStateExtractor)
        ↓
2. Reasoning Agent                (Chain-of-Thought + Self-Reflection)
        ↓
3. Planning Agent                 (Agent selection + Confidence-aware fallback)
        ↓
4. Specialist Agents              (ReAct: PreToolCheck → Tool → PostToolReflection)
        ↓
5. Response Agent                 (Synthesis + Markdown formatting)
```

Her katman **farklı bir reasoning ihtiyacına** karşılık gelir — biri olmadan diğeri yapılamaz.

---

## Pattern haritası

| Pattern | Nerede | Niçin |
|---|---|---|
| **Deterministic + LLM hybrid** | `IdExtractor`, `SessionStateExtractor` | Ucuz ve %100 doğru olabilen işleri LLM'e bırakma |
| **Chain-of-Thought (CoT)** | `ReasoningAgent` → `ReasoningResult.Steps[]` | Görünür akıl yürütme, debug + audit |
| **Self-Reflection / Sanity Check** | `ReasoningAgent` → `SanityIssues[]` | LLM kendi tutarsızlığını fark etsin |
| **Confidence-Aware Routing** | `PlanningAgent` → `IntentConfidence` threshold | Belirsiz durumda specialist çağırma, soru sor |
| **ReAct (Reason + Act)** | Specialist agents → `PreToolCheck` + Tool + `PostToolReflection` | Tool çağrısından önce doğrula, sonra yorumla |
| **Decomposition** | `ReasoningResult.SubTasks[]` | Compound query'leri parçala (örn. "1 ve 2") |
| **Grounding** | `ReasoningStep.Grounding` | Her step'in kaynağı (regex/DB/history/assumption) |
| **Dynamic Handoff** | `PostToolReflection.HandoffSuggestion` | Yanlış agent seçildiyse runtime'da düzelt |
| **Replan** | `ReplanService` + `ChatSessionState.Replan` | Admin/agent tekrar düşünme talep eder |
| **Reasoning Effort Tuning** | `ReasoningChatClient.ReasoningEffort` (low/med/high) | OpenAI o-series için derinlik ayarı |
| **Few-shot via Markdown** | `Prompts/agents/*.md` + `Prompts/services/*.md` | Behavior'u kodda değil prompt'ta tut |

---

## 1. Deterministic + LLM Hybrid

**Problem:** LLM her şeye iyi değil. Regex'in çok daha iyi yapacağı işleri LLM'e bırakmak hem **pahalı** hem **halüsinasyon riski**.

### Çözüm: LLM öncesi deterministic preprocessing

```
Kullanıcı: "5 nerede"
   ↓
IdExtractor.Extract(query)                          ← Regex (mikrosaniye)
   → ExtractedIds { OrderId = "5" }
   ↓
IdExtractor.BuildHintMessage(ids)                   ← Prompt'a inject
   → "[ID İPUCU]
       - order_id: 5
       [TOOL ÖNCELİĞİ]
       order_id mevcutsa order_status_tool kullan..."
   ↓
ReasoningAgent prompt'una eklenir                   ← LLM bu hint'i görür
```

**Sonuç:**
- LLM ID'leri tekrar çıkarmaya çalışmaz (halüsinasyon yok)
- Cost ↓ (daha kısa LLM yanıtı)
- Doğruluk ↑ (regex deterministic)

Aynı pattern `SessionStateExtractor`'da: turn count, intent keyword match, sentiment keyword match — hepsi LLM'siz.

📁 Kod: [`domain/Services-IdExtractor.md`](domain/Services-IdExtractor.md), [`domain/Services-SessionStateExtractor.md`](domain/Services-SessionStateExtractor.md)

---

## 2. Chain-of-Thought (CoT)

**Problem:** LLM "şunu yap" diye direkt cevap verirse:
- Mantığını göremeyiz → debug zor
- Hatalı çıkarımda yakalayamayız
- Audit trail yok

### Çözüm: Yapılandırılmış adım dizisi

`ReasoningAgent` JSON üretir:

```json
{
  "analysis": "Kullanıcı 5 siparişinin durumunu soruyor",
  "steps": [
    {
      "order": 1,
      "description": "Kullanıcı mesajında 5 ID'si tespit edildi",
      "action": "extract",
      "grounding": "regex",
      "confidence": 0.95
    },
    {
      "order": 2,
      "description": "Niyet: OrderInquiry (status sorgu)",
      "action": "route",
      "grounding": "session_state",
      "confidence": 0.9
    },
    {
      "order": 3,
      "description": "OrderAgent'a yönlendirilmeli",
      "action": "route",
      "grounding": "derived",
      "confidence": 0.85
    }
  ],
  "intent": "OrderInquiry",
  "confidenceScore": 0.9
}
```

Her step şunları içerir:
- **action**: ne yapıldı (`extract` / `route` / `clarify` / `call_tool` / `verify` / `terminate`)
- **grounding**: delil kaynağı (`regex` / `session_state` / `history` / `DB` / `derived` / `assumption`)
- **confidence**: per-step güven 0-1

### Neden grounding?

```
Tüm step'ler grounding=assumption → LLM havadan üretti → güvenilmez
Çoğu step grounding=regex/DB/session_state → kanıta dayalı → güvenilir
```

`SanityChecker` (aşağıda) `assumption` oranı yüksekse Warn fırlatır.

📁 Kod: [`domain/Model-Reasoning.md`](domain/Model-Reasoning.md), `Prompts/services/reasoning-system.md`

---

## 3. Self-Reflection / Sanity Check

**Problem:** LLM kendiyle tutarsız çıkarsayabilir:
- `confidence: 0.95` ama aynı zamanda `needsClarification: true` (çelişki)
- `requiredInfo: ["order_id"]` derken `collectedInfo.order_id = "5"` (gereksiz tekrar)
- Intent declared "Complaint" ama steps "OrderInquiry" gibi (uyumsuzluk)

### Çözüm: SanityIssues array

ReasoningAgent **kendini denetler** — JSON çıkışına `sanityIssues[]` ekler:

```json
{
  "sanityIssues": [
    {
      "code": "overconfident_clarification",
      "severity": "Warn",
      "message": "Yüksek güven (0.95) ile clarification gerek diyor — tutarsız",
      "suggestedFix": "Ya güveni düşür ya clarification false yap"
    }
  ]
}
```

### Severity'ye göre davranış

| Severity | Davranış |
|---|---|
| `Info` | Log'a yaz, devam et |
| `Warn` | Log + telemetry, devam et |
| `Error` | **Replan tetikle** — düşünceyi tekrar yaptır |

Bu sayede LLM kendi hatasını fark edip düzeltir. Tek-shot bekleyip kötü output kabul etmek yerine.

📁 Kod: [`domain/Model-Reasoning.md`](domain/Model-Reasoning.md) (`ReasoningIssue`)

---

## 4. Confidence-Aware Routing

**Problem:** PlanningAgent emin değilse ne yapmalı? Yanlış agent seçince specialist hata fırlatır, kullanıcı kötü deneyim yaşar.

### Çözüm: Confidence threshold + clarification fallback

```
PlanningAgent çıktısı:
  IntentConfidence = 0.45
       ↓
  Threshold = 0.7
       ↓
  0.45 < 0.7 → specialist çağırma
       ↓
  NeedsClarification = true
  ClarificationQuestion = "Siparişin numarası 1030 formatında mı?"
       ↓
  ResponseAgent kullanıcıya soru sorar
       ↓
  Bir sonraki turn'de tekrar PlanningAgent (daha çok bağlam ile)
```

### Niçin clarification "asked > guessed"

| Yaklaşım | Risk |
|---|---|
| Düşük confidence + tahmin et | Yanlış tool, yanlış data, kullanıcı şikayeti |
| Düşük confidence + soru sor | +1 turn maliyeti ama doğruluk yüksek |

Bu pattern özellikle ID'siz mesajlarda kritik:
- "siparişim nerede" → hangi sipariş? sor
- "iade istiyorum" → hangi ürün/sipariş için? sor

📁 Kod: [`application/PlanningAgent`](application/README.md), `Prompts/agents/planning-agent.md`

---

## 5. ReAct (Reason + Act)

Klasik **ReAct pattern**: agent önce düşünür, sonra hareket eder, sonra sonucu yorumlar.

### Specialist agent'ın 3 fazı

```
1. PreToolCheck (Reason — tool çağırmadan önce)
   ↓
2. Tool call (Act)
   ↓
3. PostToolReflection (Reason — tool sonucundan sonra)
```

### Faz 1: PreToolCheck

```json
{
  "requiredParams": ["order_id"],
  "collectedParams": ["order_id"],
  "missingParams": [],
  "canProceed": true,
  "reasoning": "5 mevcut, sorgu net",
  "confidence": 0.9
}
```

`canProceed = false` ise tool çağrılmaz — kullanıcıdan eksik bilgi istenir.

### Faz 2: Tool call

```
order_status_tool(order_id = "5")
   → ToolResult.Ok(data = { status: "Kargoda" })
```

### Faz 3: PostToolReflection

```json
{
  "taskComplete": true,
  "status": "done",
  "summary": "Sipariş 5 'Kargoda' durumunda",
  "handoffSuggestion": null
}
```

Eğer task tamamlanamadıysa `handoffSuggestion = "ComplaintAgent"` ile başka agent'a yönlendirilir.

### Neden 3 faz?

- **PreToolCheck:** Yanlış parametre ile tool çağırıp hata almaktansa, önce kontrol et
- **PostToolReflection:** Tool sonucunu kullanıcıya ham vermek yerine yorumla
- **Status normalizasyonu:** `done` / `needs_followup` / `needs_escalation` / `failed` / `partial` — workflow için tek tip karar

📁 Kod: [`domain/Model-Specialist.md`](domain/Model-Specialist.md), `Prompts/agents/order-agent.md` (vb.)

---

## 6. Decomposition (Compound Query)

**Problem:** Kullanıcı tek mesajda **birden fazla iş** ister:

> "1'imi sor, ardından şikayet açmak istiyorum"

Tek agent bunu yapamaz — iki ayrı domain (sorgu + şikayet).

### Çözüm: SubTask array

ReasoningAgent compound query'i **alt görevlere** böler:

```json
{
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
      "dependencies": [1]
    }
  ]
}
```

### Execution strategy

| SubTask özelliği | Davranış |
|---|---|
| `dependencies: []` | Hemen başla |
| `dependencies: [1]` | Subtask 1 tamamlanmasını bekle |
| Yan-etkisiz (Inquiry, ProductInfo) | **Paralel** çalıştırılabilir (`Task.WhenAll`) |
| Yan-etkili (OrderPlacement, Complaint) | **Sıralı** — HITL gate'i bloklar |

```
Yan-etkisiz query: "1 ve 2 durumu"
  → 2 paralel subtask → p50 latency ÷ 2
```

📁 Kod: [`domain/Model-Reasoning.md`](domain/Model-Reasoning.md) (`SubTask`), [`application/`](application/README.md)

---

## 7. Grounding (Delil-Tabanlı Akıl Yürütme)

**Problem:** LLM her şeyi sallayabilir — "muhtemelen kargodadır" gibi. Saklayamadığı/bilmediği şeylere yatırım yapmaz.

### Çözüm: Her step için `grounding` etiketi

| Grounding | Anlamı | Güven |
|---|---|---|
| `regex` | IdExtractor match | ⭐⭐⭐⭐⭐ |
| `DB` | Veritabanı sorgusu | ⭐⭐⭐⭐⭐ |
| `session_state` | Session.CollectedInfo'dan | ⭐⭐⭐⭐ |
| `history` | Önceki mesajlardan | ⭐⭐⭐ |
| `derived` | Türetilmiş (örn. "müşterinin son siparişi") | ⭐⭐⭐ |
| `assumption` | Varsayım (delil yok) | ⭐ |

### Sanity rule

Eğer `steps[]` içinde **çoğunluk assumption** ise:
```
SanityIssues += {
  code: "grounding_missing",
  severity: "Warn",
  message: "Çoğu step delilsiz — model halüsinasyon riski yüksek"
}
```

📁 Kod: [`domain/Model-Reasoning.md`](domain/Model-Reasoning.md) (`ReasoningStep.Grounding`)

---

## 8. Dynamic Handoff (Runtime Agent Switching)

**Problem:** PlanningAgent yanlış agent seçer:

> Kullanıcı: "5 gelmedi, iade istiyorum"  
> Planning: → OrderAgent  
> OrderAgent tool çağırır → "kargoda" → ama kullanıcı **iade** istiyor

### Çözüm: Specialist self-correction

Specialist `PostToolReflection`'da handoff önerebilir:

```json
{
  "taskComplete": false,
  "status": "needs_followup",
  "handoffSuggestion": "ComplaintAgent",
  "handoffReason": "Kullanıcı iade istiyor ama OrderAgent sadece sorgu yapar"
}
```

`AgentTeamCoordinator` bunu okur, runtime'da `ComplaintAgent`'a yönlendirir. **Planning hata yapsa bile** sistem düzeltir.

📁 Kod: [`domain/Model-Specialist.md`](domain/Model-Specialist.md) (`PostToolReflection`)

---

## 9. Replan (Manuel Tekrar Düşün)

**Problem:** Admin canlı izlerken botun yanlış yöne gittiğini görür. Sıfırdan reset yapmak konuşmayı bozar.

### Çözüm: Replan flag + note

Admin paneli "Yeniden Planla" butonu:

```csharp
state.Replan.ForceReplanNextTurn = true;
state.Replan.ReplanRequestedBy = "admin-1";
state.Replan.ReplanNote = "Kullanıcı VIP, escalation tercih etmeli";
```

Sonra `ReplanService.ExecuteAsync` tetiklenir:
1. Session state + history okunur
2. `effectiveQuery` = ReplanNote ?? lastUserMessage
3. Full Reasoning + Planning pipeline yeniden çalışır
4. ReplanNote LLM prompt'una **bağlam** olarak girer
5. Yeni yanıt üretilir, `ForceReplanNextTurn = false` yapılır

📁 Kod: [`application/ReplanService.md`](application/ReplanService.md), [`domain/Model-Session.md`](domain/Model-Session.md) (`ReplanControl`)

---

## 10. Reasoning Effort Tuning (o-series)

**Problem:** OpenAI o1/o3 modelleri "extended thinking" yapar — daha derin reasoning ama yavaş + pahalı. Her use case için uygun değil.

### Çözüm: `reasoning_effort` parametresi

```json
{
  "AI": {
    "OpenAI": {
      "ReasoningModel": "o1-mini",
      "ReasoningEffort": "medium"
    }
  }
}
```

| Effort | Davranış | Kullanım |
|---|---|---|
| `low` | Hızlı, kısa düşünme | Basit sorgu, single-turn |
| `medium` | Orta seviye | Default — çoğu senaryo |
| `high` | Derin reasoning | Compound query, kritik karar |

`ReasoningChatClient.ReasoningEffort` startup'ta belirlenir; ileride **dynamic** olarak senaryo karmaşıklığına göre değişebilir (örn. SubTasks ≥ 2 ise `high`).

📁 Kod: [`adapters-ai/ChatClients.md`](adapters-ai/ChatClients.md) (`ReasoningChatClient`)

---

## 11. Few-Shot via Markdown Prompts

**Problem:** Behavior değişikliği için kod deploy etmek yavaş.

### Çözüm: External markdown prompt'lar

```
CustomerSupportBot.Api/Prompts/
├── agents/
│   ├── planning-agent.md      ← PlanningAgent system prompt
│   ├── order-agent.md
│   ├── complaint-agent.md
│   └── ...
└── services/
    ├── reasoning-system.md    ← ReasoningAgent system prompt
    ├── reasoning-hint.md      ← ID hint template
    └── routing-rewrite-*.md
```

Her prompt:
- Role tanımı ("Sen bir customer support botusun")
- JSON schema (ne döneceği)
- Few-shot örnekleri ("Örnek 1: kullanıcı X dedi → bot Y dedi")
- Constraints ("Türkçe yanıt ver, JSON dışı text yazma")

### Live update

Markdown dosyaları `FileSystemPromptRepository` startup'ta cache'ler. Prompt değişince:
1. Dosyayı düzenle
2. Uygulamayı restart et (60s)
3. Yeni davranış canlı — kod değişikliği yok

📁 Kod: [`adapters-persistence/FileSystemAdapters.md`](adapters-persistence/FileSystemAdapters.md), `Prompts/README.md`

---

## Akış: Tam reasoning pipeline (örnek)

Kullanıcı: **"5 ve 7 durumu nedir?"**

```
[1] Deterministic preprocessing
    IdExtractor → { OrderId: "5" } (ilki)
    SessionStateExtractor → turnCount++, intent keyword: "durum" → OrderInquiry

[2] ReasoningAgent (CoT + decomposition)
    Output:
      analysis: "Kullanıcı 2 farklı siparişin durumunu soruyor"
      steps: [
        { action: "extract", grounding: "regex", confidence: 0.95 },
        { action: "route", grounding: "session_state", confidence: 0.9 }
      ]
      subTasks: [
        { order: 1, intent: OrderInquiry, entities: { order_id: 5 } },
        { order: 2, intent: OrderInquiry, entities: { order_id: 7 } }
      ]
      confidenceScore: 0.92
      sanityIssues: []   ← temiz

[3] PlanningAgent
    Her subtask için → OrderAgent
    intentConfidence: 0.92 → threshold OK, clarification yok

[4] Specialist Agents (paralel — yan-etkisiz)
    OrderAgent #1:
      PreToolCheck → canProceed: true
      Tool: order_status_tool(5) → "Kargoda"
      PostToolReflection → status: done

    OrderAgent #2:
      PreToolCheck → canProceed: true
      Tool: order_status_tool(7) → "Teslim Edildi"
      PostToolReflection → status: done

[5] ResponseAgent (synthesis)
    Input: 2 subtask sonucu
    Output: "Sipariş 5 kargoda, 7 ise teslim edildi."

[6] Trace persistence
    ReasoningTrace { steps, agents, tools, duration } → DB
```

---

## Niçin bu kadar katman?

**"Tek LLM çağrısı yapsak?"** sorusunun cevabı:

| Tek LLM çağrı | Çok katmanlı reasoning |
|---|---|
| Halüsinasyon yüksek | Grounding ile minimize |
| Tool seçimi yanlış olabilir | Confidence threshold + handoff koruması |
| Debug imkansız | Steps + SanityIssues + AgentVisits trace |
| Compound query yapılamaz | SubTask decomposition |
| Self-correction yok | SanityIssues → Error → Replan |
| Cost öngörülemez | Per-katman optimize edilebilir |

---

## Örnek Trace — Gerçek Bir Çalışmadan

Aşağıdaki trace, S05 senaryosu (`"001 1 için hasarlı ürün şikayeti açmak istiyorum"`) için sistemin **fiili davranışı**dır. `ReasoningTrace` formatında, HITL approval ile şikayet kaydı akışını gösterir.

### Kullanıcı mesajı

```
001 1 için hasarlı ürün şikayeti açmak istiyorum
```

### Tam trace (DB'den okunmuş)

```jsonc
{
  "traceId": "trace_01HG8K2P3M9X4N7B5R",
  "sessionId": "sess_8f3c1a2e",
  "userQuery": "001 1 için hasarlı ürün şikayeti açmak istiyorum",
  "startedAt": "2026-05-24T10:00:12.450Z",
  "completedAt": "2026-05-24T10:00:18.927Z",
  "durationMs": 6477,
  "iterationCount": 5,
  "estimatedTokens": 2840,
  "terminationReason": "completed",

  // ─── 1. Deterministic preprocessing (LLM'siz) ───
  // IdExtractor + SessionStateExtractor → reasoning öncesi
  // Bu adım trace'e ayrı yazılmaz ama session.state'e yansır:
  //   collectedInfo: { customer_id: "001", order_id: "1" }
  //   currentIntent: "Complaint"    (keyword: "şikayet")
  //   phase: "Action"

  // ─── 2. ReasoningAgent (Chain-of-Thought) ───
  "reasoning": {
    "analysis": "Müşteri 001, 1 siparişi için 'hasarlı ürün' şikayeti açmak istiyor. Tüm gerekli ID'ler net biçimde verilmiş, niyet açık.",
    "intent": "Complaint",
    "confidence": "yüksek",
    "confidenceScore": 0.95,
    "requiredInfo": [],
    "steps": [
      {
        "order": 1,
        "description": "Kullanıcı mesajında 001 ve 1 ID'leri regex ile tespit edildi",
        "action": "extract",
        "grounding": "regex",
        "confidence": 0.99
      },
      {
        "order": 2,
        "description": "'şikayet açmak' ifadesi → Complaint intent",
        "action": "route",
        "grounding": "session_state",
        "confidence": 0.95
      },
      {
        "order": 3,
        "description": "Şikayet açıklaması 'hasarlı ürün' olarak alındı — yeterli detay",
        "action": "verify",
        "grounding": "regex",
        "confidence": 0.85
      },
      {
        "order": 4,
        "description": "ComplaintAgent'a yönlendirilmeli; complaint_registration_tool yan-etkili → HITL approval gerekecek",
        "action": "route",
        "grounding": "derived",
        "confidence": 0.95
      }
    ],
    "subTasks": [],
    "sanityIssues": [],
    "sentiment": "negative",
    "sentimentScore": 0.35
  },

  // ─── 3. PlanningAgent ───
  "planning": {
    "detectedIntent": "Complaint",
    "intentConfidence": 0.95,
    "supportingEvidence": [
      "şikayet açmak istiyorum",
      "hasarlı ürün"
    ],
    "selectedAgent": "ComplaintAgent",
    "rationale": "Müşteri açıkça şikayet talebinde bulunuyor; ID'ler eksiksiz; alternatif agent gerekmiyor.",
    "alternativesRejected": [
      { "agent": "OrderAgent",          "reason": "Kullanıcı sipariş durumu sormuyor; iade/şikayet niyeti var" },
      { "agent": "ProductAgent", "reason": "Ürün bilgisi sorulmuyor" }
    ],
    "needsClarification": false,
    "taskDescription": "001 müşterisi için 1 siparişinde 'hasarlı ürün' şikayeti kaydet"
  },

  // ─── 4. Specialist: ComplaintAgent ───
  "specialistReasonings": [
    {
      "agentName": "ComplaintAgent",
      "preToolCheck": {
        "requiredParams": ["customer_id", "order_id", "description"],
        "collectedParams": ["customer_id", "order_id", "description"],
        "missingParams": [],
        "canProceed": true,
        "reasoning": "Tüm parametreler session.collectedInfo + kullanıcı mesajından elde edildi",
        "confidence": 0.95
      },
      "resultConfidence": 0.9,
      "resultNotes": "Approval onayı bekleniyor, sonra tool çağrılacak",
      "postToolReflection": {
        "taskComplete": true,
        "status": "done",
        "statusEnum": "Done",
        "handoffSuggestion": null,
        "missingContext": [],
        "summary": "Şikayet 7 olarak kaydedildi (001 / 1)"
      }
    }
  ],

  // ─── 5. Agent timeline ───
  "agentVisits": [
    { "agentName": "ReasoningAgent",  "startedAt": "2026-05-24T10:00:12.500Z", "completedAt": "2026-05-24T10:00:13.880Z", "durationMs": 1380 },
    { "agentName": "PlanningAgent",   "startedAt": "2026-05-24T10:00:13.910Z", "completedAt": "2026-05-24T10:00:14.620Z", "durationMs": 710 },
    { "agentName": "ComplaintAgent",  "startedAt": "2026-05-24T10:00:14.650Z", "completedAt": "2026-05-24T10:00:18.450Z", "durationMs": 3800,
      "output": "PreToolCheck → ApprovalGate (3.2s) → tool çağrısı → reflection"
    },
    { "agentName": "ResponseAgent",   "startedAt": "2026-05-24T10:00:18.480Z", "completedAt": "2026-05-24T10:00:18.927Z", "durationMs": 447 }
  ],

  // ─── 6. Tool çağrıları ───
  "toolCalls": [
    {
      "toolName": "complaint_registration_tool",
      "invokedAt": "2026-05-24T10:00:17.860Z",
      "agentName": "ComplaintAgent",
      "parametersSummary": "{ customer_id: \"001\", order_id: \"1\", description: \"hasarlı ürün\" }",
      "resultSummary": "Success — { complaint_id: \"7\", status: \"Beklemede\" }",
      "success": true,
      "signature": "complaint_registration:001:1:hasarli_urun"
    }
  ],

  // ─── 7. Approval (HITL) ───
  // Tool çağrısı yapılmadan önce approval gate tetiklendi:
  //   ApprovalRequest oluşturuldu (id: app_01HG8K2P5N)
  //   3.2 saniye sonra admin "Onayla" tıkladı
  //   Sonra tool çağrıldı ve sonuç döndü.

  "finalResponse": "Şikayetiniz başarıyla kaydedildi. Şikayet numaranız: **7**. Müşteri hizmetleri ekibimiz en kısa sürede sizinle iletişime geçecektir."
}
```

---

### Bu trace'te hangi pattern'ler kullanıldı?

| Pattern | Trace'te görünüm |
|---|---|
| **Deterministic preprocessing** | `regex` grounding'li step #1 — `001`, `1` LLM'siz çıkarıldı |
| **Chain-of-Thought** | `reasoning.steps[]` — 4 adım, her biri action + grounding + confidence ile |
| **Sanity check** | `sanityIssues: []` — bu örnekte temiz; uyumsuzluk olsaydı Replan tetiklenirdi |
| **Confidence-aware routing** | `intentConfidence: 0.95` > 0.7 threshold → clarification yok, direkt specialist |
| **Grounding** | `regex` (×2), `session_state`, `derived` — sadece 1 step `derived`, çoğunluk delilli |
| **ReAct (3 faz)** | ComplaintAgent: `preToolCheck.canProceed=true` → tool call → `postToolReflection.status=done` |
| **HITL approval gate** | `complaint_registration_tool` high-risk → 3.2 saniye admin onayı beklendi |
| **Alternatives rejected** | Planning OrderAgent ve ProductAgent'ı **gerekçeli** elemiş (audit) |
| **No handoff** | `handoffSuggestion: null` — doğru agent seçildi, dynamic re-route gerekmedi |
| **Sentiment tracking** | `sentimentScore: 0.35` — negative (şikayet), `consecutiveNegativeTurns++` |

### Latency dağılımı

```
ReasoningAgent   1380 ms  ████████████████████
PlanningAgent     710 ms  ██████████
ComplaintAgent   3800 ms  ████████████████████████████████████████████████████████
  ├─ PreToolCheck  ~500 ms
  ├─ Approval gate ~3200 ms  (admin reaksiyonu!)
  ├─ Tool call      ~80 ms
  └─ Reflection    ~20 ms
ResponseAgent     447 ms  ██████
────────────────────────
Toplam            6477 ms
```

**Önemli:** Tool çağrısı + reflection sadece **100 ms** sürdü. Toplam latency'nin **%50'si admin'in onay vermesi**. Production'da SLA Guardian bu süreyi takip eder; threshold aşılırsa otomatik AutoReject veya öncelik boost tetiklenir.

### Token kullanımı

```
ReasoningAgent prompt+response   ~1200 tokens  ($0.00018 @ gpt-4o-mini)
PlanningAgent                     ~480 tokens  ($0.00007)
ComplaintAgent                    ~720 tokens  ($0.00011)
ResponseAgent                     ~440 tokens  ($0.00007)
─────────────────────────────────────────────────────────
Toplam (estimatedTokens)         ~2840 tokens  $0.00043
```

Tek konuşma turn'ü < yarım sentin altında. Bu deterministic preprocessing + structured prompting sayesinde — LLM'e gereksiz iş yaptırılmadı.

---

## İlgili dokümantasyon

- **Implementation:**
  - [`domain/Model-Reasoning.md`](domain/Model-Reasoning.md) — ReasoningResult, ReasoningStep, SubTask, ReasoningIssue
  - [`domain/Model-Specialist.md`](domain/Model-Specialist.md) — PreToolCheck, PostToolReflection
  - [`domain/Services-Parsers.md`](domain/Services-Parsers.md) — LLM JSON parse mantığı
  - [`domain/Services-IdExtractor.md`](domain/Services-IdExtractor.md) — Regex ID çıkarımı
  - [`domain/Services-SessionStateExtractor.md`](domain/Services-SessionStateExtractor.md) — Deterministic state
  - [`application/ReasoningAgent`](application/README.md), [`application/ReplanService.md`](application/ReplanService.md)
  - [`adapters-ai/ChatClients.md`](adapters-ai/ChatClients.md) — ReasoningChatClient
- **Patterns daha geniş:**
  - [`agentic-patterns.md`](agentic-patterns.md) — Genel agentic design pattern reference
  - [`intelligence.md`](intelligence.md) — Semantic memory + self-improvement döngüsü
