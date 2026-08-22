# TaskCompletionStatus

- **Kaynak:** `CustomerSupportBot.Domain/Model/TaskCompletionStatus.cs`
- **Tür:** `public enum`
- **Namespace:** `CustomerSupportBot.Domain.Model`

## 1. Ne İşe Yarar

Bir specialist agent'ın tool çağrısı sonrası görevinin tamamlanma durumunu **type-safe** biçimde
temsil eder — eski string tabanlı `"done"`/`"needs_followup"`/`"needs_escalation"`/`"failed"`/`"partial"`
karşılaştırmalarının yerine geçer.

## 2. Hangi Amaçla Kullanılır

`SpecialistReasoning.PostToolReflection.Status` bu enum tipindedir. `SpecialistReasoningParser.NormalizeStatus`,
LLM'in ürettiği çeşitli string varyasyonlarını (`"completed"`, `"success"`, `"escalate"` vb. — bkz.
`WellKnown.TaskStatuses`) bu enum'a normalize eder. `WorkflowRunner` ve trace/analiz kodu, string
karşılaştırması yerine bu enum üzerinden switch/if yaparak yazım hatalarına karşı derleme zamanı
güvenliği kazanır.

## 3. Sorumlulukları

- ✅ Görev tamamlanma durumunu sonlu ve type-safe bir kümeyle temsil etmek
- ❌ String → enum normalizasyonunu yapmak — bu `SpecialistReasoningParser.NormalizeStatus`'un işi

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- **Kim üretir:** `SpecialistReasoningParser.NormalizeStatus` (LLM'in ham string çıktısından)
- **Kim tüketir:** `SpecialistReasoning.PostToolReflection`, `WorkflowRunner` (trace/karar mantığı)
- **İlişkili sabitler:** `WellKnown.TaskStatuses` (LLM'in üretebileceği ham string alias'ları)

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

> 💡 **`PendingApproval` değeri, bloklamayan HITL modeli için eklendi.** Yan etkili bir tool onay
> kuyruğuna eklendiğinde ama admin henüz karar vermediğinde iş HENÜZ yapılmamış olur.
> `ToolResult.Success=true` olsa bile bu durum `Done` ile karıştırılırsa, sistem "iş bitti" sanıp
> yanlış bir kapanış/takip mantığı işletebilir (bkz. [ToolResult](ToolResult.md)'taki
> `PendingApproval` alanı ile aynı ayrım).

## 6. Metotlar / Üyeler

| Değer | Anlamı |
|---|---|
| `Done` | Görev başarıyla tamamlandı |
| `NeedsFollowUp` | Ek bilgi veya takip gerekiyor |
| `NeedsEscalation` | İnsan temsilciye devredilmeli |
| `Failed` | Tool çağrısı başarısız oldu |
| `Partial` | Kısmen tamamlandı, eksik var |
| `PendingApproval` | Yan etkili tool onay kuyruğuna eklendi, admin kararı verilmedi — iş henüz yapılmadı |

## 7. Bağımlılıklar

Yok — saf domain modeli, dış bağımlılığı yok.

## Bağlantılar

- [ToolResult.md](ToolResult.md) — `PendingApproval` ayrımının paralel örneği
- [SpecialistReasoning.md](SpecialistReasoning.md) — Bu enum'u taşıyan `PostToolReflection`
- [../Services/SpecialistReasoningParser.md](../Services/SpecialistReasoningParser.md) — Normalizasyon mantığı
