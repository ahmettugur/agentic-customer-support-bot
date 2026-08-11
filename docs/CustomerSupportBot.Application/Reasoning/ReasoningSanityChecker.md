# ReasoningSanityChecker

**Dosya:** `Services/Reasoning/ReasoningSanityChecker.cs`  
**Yaşam döngüsü:** Singleton (DI)

## 1. Ne İşe Yarar

LLM'in reasoning çıktısını **8 deterministik kural** ile tararak mantık tutarsızlıklarını yakalar. **Hiçbir LLM çağrısı yapmaz** — saf kural tabanlıdır.

## 2. Hangi Amaçla Kullanılır

Reasoning Pipeline'ın **L3 (son) katmanı**dır. ReasoningService LLM çıktısını parse ettikten sonra SanityChecker'ı çağırır. Bulunan issue'lar `ReasoningResult.SanityIssues`'a eklenir ve trace'e/debug paneline yansır.

> 💡 **Analiz notu:** Bir muhasebecinin bilanço kontrolü gibi — "gelir 100 ama gider 200 yazıyorsun, bu tutarsız" diye bakar. Modelin karar tutarsızlıklarını yakalar.

## 3. Kural Listesi (8 adet)

| Kural | Kod | Ne Kontrol Eder |
| ------- | ----- | ----------------- |
| OverconfidentClarification | `overconfident_clarification` | Güven yüksek ama clarification istiyor — çelişki |
| RedundantRequiredInfo | `redundant_required_info` | requiredInfo'da zaten verified olan alan var |
| IntentActionMismatch | `intent_action_mismatch` | Intent ile nextAction çelişiyor |
| LowConfidenceNoMissing | `low_confidence_no_missing` | Güven düşük ama eksik bilgi belirtilmemiş |
| AssumptionHeavySteps | `assumption_heavy_steps` | Adımların çoğunda grounding yok (dayanak belirtilmemiş) |
| OverconfidentAssumptions | `overconfident_assumptions` | Çok varsayım var ama güven çok yüksek |
| NotFoundIgnored | `not_found_ignored` | DB'de bulunamayan entity nextAction'da yok sayılmış |
| SubTasksIgnored | `subtasks_ignored` | SubTasks var ama nextAction tek agent'a yönlendiriyor |

## 4. Mimari: Strategy Pattern

Her kural `IReasoningSanityRule` interface'ini implement eder. Yeni kural eklemek için sadece bir class yazıp `_rules` listesine eklemek yeterli — mevcut kodu değiştirmeye gerek yok (Open/Closed Principle).

## 5. Metotlar

| Metot | Açıklama |
|-------|----------|
| `Check(result, verified)` | Tüm kuralları çalıştırır, bulunan issue'ları döner |

## Bağlantılar

- [ReasoningService.md](ReasoningService.md) — Bu checker'ı çağıran servis
- [EntityVerifier.md](EntityVerifier.md) — Verified entities kaynağı
- [../../CustomerSupportBot.Domain/Model/ReasoningIssue.md](../../CustomerSupportBot.Domain/Model/ReasoningIssue.md) — Çıktı modeli
