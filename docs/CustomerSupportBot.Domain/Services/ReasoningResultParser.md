# ReasoningResultParser

**Dosya:** `Services/ReasoningResultParser.cs`  
**Tür:** `static class` — LLM çağrısı yapmaz, tamamen deterministik

## 1. Ne İşe Yarar

ReasoningService'in LLM çağrısından dönen JSON çıktısını `ReasoningResult` nesnesine dönüştürür.

## 2. Hangi Amaçla Kullanılır

Reasoning LLM'i yapılandırılmış JSON üretir. Bu parser JSON'u çıkarır, tüm alanları (Analysis, Steps, Intent, RequiredInfo, Confidence, SubTasks, Sentiment vb.) deserialize eder. Legacy format (steps düz string listesi) otomatik wrap edilir — geriye dönük uyumlu.

> 💡 **Analiz notu:** `ConfidenceScore` hem sayısal (0.7) hem string ("yüksek") formatta gelebilir — parser ikisini de handle eder.

## 3. Metotlar

| Metot | Açıklama |
|-------|----------|
| `Parse(string?)` | JSON → ReasoningResult. Parse edilemezse varsayılan döner. |

## Bağlantılar

- [../Model/ReasoningResult.md](../Model/ReasoningResult.md) — Üretilen model
- [PlanningResultParser.md](PlanningResultParser.md) — Benzer parser
