# SessionStateService

**Dosya:** `Services/Chat/SessionStateService.cs`

## 1. Ne İşe Yarar

Session state güncellemelerini orkestre eder — TurnSignals → SessionState dönüşümü, sentiment tracking, collected info merge.

## 2. Hangi Amaçla Kullanılır

Her tur sonunda SessionStateExtractor (Domain) + TurnSignals ile session state güncellenir. Bu servis o koordinasyonu yapar.

## Bağlantılar

- [../../CustomerSupportBot.Domain/Model/TurnSignals.md](../../CustomerSupportBot.Domain/Model/TurnSignals.md) — Sinyal taşıyıcı
- [../../CustomerSupportBot.Domain/Services/SessionStateExtractor.md](../../CustomerSupportBot.Domain/Services/SessionStateExtractor.md) — State türetimi
