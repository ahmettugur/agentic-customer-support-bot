# LLM JSON Parser'lar

**Dosyalar:**
- `Services/PlanningResultParser.cs`
- `Services/ReasoningResultParser.cs`
- `Services/SpecialistReasoningParser.cs`

Bu üç parser, LLM'in **yapılandırılmış JSON yanıtını** Domain modeline çevirir. Hepsi static class, dış bağımlılığı yok.

---

## Ortak desen

Üç parser da aynı sorunu çözer:

```
LLM çıktı (Raw JSON String)                Domain modeli (C# DTO)
───────────────────────────                ─────────────────────
{                                     →    PlanningResult
  "detectedIntent": "OrderInquiry",        {
  "intentConfidence": 0.85,                    DetectedIntent = "OrderInquiry",
  ...                                          IntentConfidence = 0.85,
}                                          }
```

**Zorluklar:**
1. JSON, ` ```json ... ``` ` fence içinde veya doğrudan gelebilir
2. Field'lar opsiyonel olabilir (LLM bazılarını atlar)
3. Schema bozulabilir (LLM yanlış format üretir)
4. Tip dönüşümleri (string `"yüksek"` → 0.85, sayı `0.85` → 0.85)

Parser'lar **defensive** yazılmıştır — bozuk girdi exception fırlatmaz, en kötü ihtimalle minimum field'larla DTO döner.

---

## 1. PlanningResultParser

**Girdi:** `PlanningAgent` JSON çıktısı  
**Çıktı:** `PlanningResult`

### Parse edilen alanlar

| JSON alanı | Domain field |
|---|---|
| `detectedIntent` (string) | `DetectedIntent` |
| `intentConfidence` (number veya string) | `IntentConfidence` |
| `supportingEvidence` (string[]) | `SupportingEvidence` |
| `selectedAgent` (string) | `SelectedAgent` |
| `rationale` (string) | `Rationale` |
| `alternativesRejected` (array) | `AlternativesRejected` |
| `needsClarification` (bool) | `NeedsClarification` |
| `clarificationQuestion` (string) | `ClarificationQuestion` |
| `taskDescription` (string) | `TaskDescription` |

### AlternativesRejected yapısı

```json
"alternativesRejected": [
  { "agent": "OrderAgent", "reason": "Kullanıcı yeni sipariş istemiyor" },
  { "agent": "ComplaintAgent", "reason": "Şikayet ifadesi yok" }
]
```

→ `List<RejectedAlternative>` ile `Rationale` zenginleştirilir.

---

## 2. ReasoningResultParser

**Girdi:** `ReasoningAgent` JSON çıktısı  
**Çıktı:** `ReasoningResult`

### Parse edilen alanlar (geniş schema)

| JSON alanı | Domain field | Not |
|---|---|---|
| `analysis` (string) | `Analysis` | Kullanıcının niyet özeti |
| `steps` (array) | `Steps` | Liste; **legacy string array veya yeni object array** desteklenir |
| `intent` (string) | `Intent` | |
| `requiredInfo` (string[]) | `RequiredInfo` | Eksik bilgiler |
| `confidence` (mixed) | `Confidence`, `ConfidenceScore` | String veya number |
| `rationale` (string) | `Rationale` | |
| `assumptions` (string[]) | `Assumptions` | |
| `nextAction` (string) | `NextAction` | |
| `decisionReason` (string) | `DecisionReason` | |
| `sanityIssues` (array) | `SanityIssues` | İç tutarsızlık tespiti |
| `subTasks` (array) | `SubTasks` | Compound query decomposition |
| `sentiment` (string) | `Sentiment` | |
| `sentimentScore` (number) | `SentimentScore` | 0.0-1.0 |

### Confidence dönüşümü

| Girdi | ConfidenceScore |
|---|---|
| `0.85` (number) | 0.85 |
| `"yüksek"`, `"high"` | 0.85 |
| `"orta"`, `"medium"` | 0.6 |
| `"düşük"`, `"low"` | 0.3 |
| Yoksa | 0.5 (default) |

### Steps backwards-compat

Eski format:
```json
"steps": ["Kullanıcı sipariş soruyor", "1'i bul"]
```

Yeni format:
```json
"steps": [
  { "order": 1, "description": "...", "action": "extract", "grounding": "regex", "confidence": 0.9 }
]
```

Parser iki formatı da kabul eder — geriye uyumluluk korunur.

### SanitizeAnalysis

LLM bazen `analysis` field'ına gömülü JSON veya fence işareti sızdırır:

```
"analysis": "Düşünüyorum... ```json{...}``` sonuç bu"
```

`SanitizeAnalysis()` bu gürültüyü temizler — kullanıcıya gösterilecek temiz metin döner.

---

## 3. SpecialistReasoningParser

**Girdi:** Specialist agent'ın pre-tool check ve post-tool reflection JSON'u  
**Çıktı:** `SpecialistReasoning`

### Validasyon

Bu parser ilk olarak JSON'un **specialist çıktısı** olduğunu doğrular:
- `preToolCheck`, `resultConfidence` veya `postToolReflection` anahtarlarından biri olmalı
- Yoksa `null` döner (PlanningAgent JSON yanlışlıkla buraya gelmiş olabilir)

### PreToolCheck

Specialist tool çağırmadan **önce** ne biliyor?

```json
"preToolCheck": {
  "requiredParams": ["order_id"],
  "collectedParams": ["order_id"],
  "missingParams": [],
  "canProceed": true,
  "reasoning": "1 mevcut, sorgu net",
  "confidence": 0.9
}
```

### PostToolReflection

Specialist tool çağırdıktan **sonra** sonucu yorumlar:

```json
"postToolReflection": {
  "taskComplete": true,
  "status": "done",
  "handoffSuggestion": null,
  "missingContext": [],
  "summary": "Sipariş 1 'Kargoda' durumunda"
}
```

### Status normalizasyonu

LLM farklı kelimeler kullanabilir — `NormalizeStatus()` standardize eder:

| Girdi | Normalize |
|---|---|
| `done`, `completed`, `success` | `done` |
| `needs_followup`, `followup` | `needs_followup` |
| `needs_escalation`, `escalate` | `needs_escalation` |
| `failed`, `error` | `failed` |
| `partial`, `incomplete` | `partial` |

### Handoff suggestion

Specialist görevi tamamlayamazsa **başka bir agent öner** olabilir:

```json
"handoffSuggestion": "ComplaintAgent",
"handoffReason": "Kullanıcı iade istiyor ama tool sadece sipariş statüsü gösteriyor"
```

`AgentTeamCoordinator` bunu görür ve dinamik routing yapar.

---

## Helper metodlar

Üç parser da aynı yardımcıları kullanır:

| Helper | Açıklama |
|---|---|
| `ExtractJsonBlock(text)` | ` ```json ... ``` ` fence veya bare JSON döner |
| `GetString(elem, name, default)` | Property yoksa default döner |
| `GetStringArray(elem, name)` | Array yoksa boş liste |
| `GetBool(elem, name, default)` | Property yoksa default |
| `GetDouble(elem, name, default)` | Number veya string parse |

Bu helper'lar parser kodunu kısa ve okunabilir tutar.

---

## Hata toleransı

Parser **asla** kullanıcıya hata yansıtmaz:
- JSON parse fail → minimum field'larla DTO döner
- Field eksik → default değer kullanılır
- Tip uyumsuz → tip dönüştürme veya default

Bu sayede LLM rare bir formatta yanıt verse bile sistem akmaya devam eder. Logging katmanı parse warning'lerini kaydeder; ileride prompt iyileştirilebilir.
