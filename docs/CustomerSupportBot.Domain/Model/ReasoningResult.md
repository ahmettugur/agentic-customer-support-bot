# ReasoningResult

**Dosya:** `Model/ReasoningResult.cs`  
**Tür:** `class` (mutable)

## 1. Ne İşe Yarar

Reasoning pipeline'ının nihai çıktısıdır — LLM'in kullanıcı mesajını analiz ederek ürettiği **yapılandırılmış düşünce süreci**. Intent (niyet), güven seviyesi, çözüm adımları, duygu analizi, varsayımlar ve sanity check sorunlarını içerir.

## 2. Hangi Amaçla Kullanılır

Her kullanıcı mesajında `ReasoningService` bir `ReasoningResult` üretir. Bu sonuç:

- `PlanningAgent`'a hint olarak verilir (hangi agent seçilmeli?)
- `ReasoningSanityChecker` ile tutarsızlık kontrolünden geçirilir
- UI'daki "Düşünce süreci" panelinde gösterilir
- Trace'e yazılır (audit/debug için)
- `TurnSignals.From()` ile session state'e taşınır

> 💡 **Analiz notu:** Bir doktorun hastayı muayene etmesi gibi düşün — semptomları analiz et (Analysis), adımları planla (Steps), tanı koy (Intent), eksik bilgileri belirle (RequiredInfo), ne kadar emin olduğunu söyle (ConfidenceScore). Bu class tüm bu analiz sonuçlarını bir arada tutar.

## 3. Sorumlulukları

- ✅ LLM reasoning çıktısının tüm boyutlarını yapılandırılmış olarak taşımak
- ✅ Legacy (eski format) ve yeni format alanlarını geriye dönük uyumlu tutmak
- ✅ Sanity check sonuçlarını (`SanityIssues`) taşımak
- ✅ Compound query decomposition'ı (`SubTasks`) taşımak
- ❌ Reasoning mantığını yürütmek — bu `ReasoningService`'in işi
- ❌ State'e doğrudan yazmak — `TurnSignals` üzerinden aktarılır

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- **Kim üretir:** `ReasoningResultParser.Parse()` — LLM JSON çıktısını bu modele çevirir
- **Kim tüketir:**
  - `ReasoningSanityChecker` — tutarsızlık tarar
  - `WorkflowMessageBuilder` — PlanningAgent'a hint enjekte eder
  - `TurnSignals.From()` — intent/sentiment sinyallerini çıkarır
  - UI Traces paneli — düşünce sürecini görüntüler

## 5. Neden Böyle Tasarlandı (Analiz notu)

> 💡 **Legacy + Yeni alanlar bir arada:** Proje evrim geçirdi. `Analysis`, `Steps`, `Intent`, `RequiredInfo`, `Confidence` orijinal alanlardır. `Rationale`, `Assumptions`, `NextAction`, `DecisionReason`, `ConfidenceScore` sonradan eklendi. Eski alanlar kaldırılmadı çünkü geriye dönük uyumluluk gerekiyor — mevcut prompt'lar ve parser'lar eski formatta da çalışabilir.

> 💡 **ConfidenceScore vs Confidence:** `Confidence` eski string ("yüksek/orta/düşük"), `ConfidenceScore` yeni sayısal (0.0-1.0). Yeni kod her zaman `ConfidenceScore` kullanmalı.

## 6. Metotlar / Üyeler

| Üye | Tip | Açıklama |
| ----- | ----- | ---------- |
| `Analysis` | `string` | Kısa analiz — kullanıcı ne istiyor? |
| `Steps` | `List<ReasoningStep>` | Çözüm adımları (yapılandırılmış) — bkz. [ReasoningStep.md](ReasoningStep.md) |
| `Intent` | `string` | Algılanan niyet (ör. "sipariş_sorgulama", "şikayet") |
| `RequiredInfo` | `List<string>` | Eksik bilgiler (ör. "müşteri_kimliği") |
| `Confidence` | `string` | Legacy güven seviyesi ("yüksek/orta/düşük") |
| `Rationale` | `string` | Niyet ve planın gerekçesi (1-3 cümle) |
| `Assumptions` | `List<string>` | Yapılan varsayımlar |
| `NextAction` | `string` | Bir sonraki somut aksiyon |
| `DecisionReason` | `string` | Bu aksiyonun seçilme gerekçesi |
| `ConfidenceScore` | `double` | Sayısal güven (0.0-1.0) |
| `SanityIssues` | `List<ReasoningIssue>` | Sanity checker tutarsızlıkları — bkz. [ReasoningIssue.md](ReasoningIssue.md) |
| `SubTasks` | `List<SubTask>` | Compound query alt görevleri — bkz. [SubTask.md](SubTask.md) |
| `Sentiment` | `string` | LLM'in algıladığı duygu etiketi |
| `SentimentScore` | `double` | Duygu skoru (0.0-1.0) |
| `VerifiedEntities` | `VerifiedEntities?` | EntityVerifier'ın güvenli kaynaklardan çözümlediği entity'ler; factual doğrulama tool'da |
| `ConfidenceLevel` | `ConfidenceLevel` | ConfidenceScore'un type-safe enum karşılığı (computed) |
| `IsFallback` | `bool` | 🐞 Bu sonuç gerçek bir LLM çıktısı mı yoksa timeout/hata sonrası üretilmiş bir yer tutucu mu (`ReasoningService`'in `catch` bloklarında set edilir, varsayılan `false`). `[JsonIgnore]` DEĞİLDİR — `ReasoningComplete` SSE event'iyle istemciye ulaşır. Eskiden tüketiciler bunu yalnızca düşük `ConfidenceScore`'dan dolaylı çıkarabiliyordu; ama düşük skor gerçek bir LLM sonucunda da oluşabildiği için "reasoning hiç çalışmadı" ile "çalıştı ama emin değildi" ayırt edilemiyordu. |

### Statik Metotlar

| Metot | Açıklama |
|-------|----------|
| `ScoreToString(double)` | ConfidenceScore → "yüksek/orta/düşük" string |
| `StringToScore(string?)` | Legacy "yüksek" → 0.85 numerik dönüşümü |

## 7. Constructor Bağımlılıkları

Yok — saf veri sınıfı, tüm property'ler varsayılan değerle başlatılır.

## Bağlantılar

- [ReasoningStep.md](ReasoningStep.md) — Çözüm adımları
- [ReasoningIssue.md](ReasoningIssue.md) — Sanity check tutarsızlıkları
- [SubTask.md](SubTask.md) — Compound query alt görevleri
- [ConfidenceLevel.md](ConfidenceLevel.md) — Güven seviyesi enum
- [TurnSignals.md](TurnSignals.md) — Session state'e sinyal aktarımı
- [VerifiedEntities.md](VerifiedEntities.md) — Entity resolution sonucu
- [../../CustomerSupportBot.Application/ReasoningPipeline.md](../../CustomerSupportBot.Application/Services/Reasoning/ReasoningService.md) — Bu modeli üreten pipeline
