# SlaPortService

**Dosya:** `Services/Sla/SlaPortService.cs`

## 1. Ne İşe Yarar

SLA Guardian'ı orkestre eder — bekleyen onay ve eskalasyonları periyodik tarar, konfigüre edilen süre aşılırsa SlaEvent oluşturur ve otomatik aksiyon alır (auto-reject, auto-escalate).

## 2. Hangi Amaçla Kullanılır

Background hosted service olarak çalışır. `SlaOptions` konfigürasyonundaki thresholds'a göre warn/breach olayları üretir.

> 💡 **Analiz notu:** Bir pizzacının "30 dakikada gelmezse bedava" sistemi — zaman aşımı kontrolü.

## Bağlantılar

- [SlaPolicyEvaluator.md](SlaPolicyEvaluator.md) — SLA kural değerlendirmesi
- [../../CustomerSupportBot.Domain/Model/SlaEvent.md](../../CustomerSupportBot.Domain/Model/SlaEvent.md) — SLA olay modeli
