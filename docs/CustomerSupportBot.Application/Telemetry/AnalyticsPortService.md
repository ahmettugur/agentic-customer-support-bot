# AnalyticsPortService

**Dosya:** `Services/Telemetry/AnalyticsPortService.cs`

## 1. Ne İşe Yarar

Admin panelinin analytics dashboard verilerini toplar — oturum istatistikleri, ortalama değerlendirme, niyet dağılımı, duygu analizi özeti.

## 2. Hangi Amaçla Kullanılır

Admin panelindeki "Analytics" sayfasının backend servisi. `AnalyticsDashboard` ve `SessionAnalytics` modellerini doldurur.

## Bağlantılar

- [../../CustomerSupportBot.Domain/Model/ConversationRating.md](../../CustomerSupportBot.Domain/Model/ConversationRating.md) — Rating modeli
- [../../CustomerSupportBot.Domain/Model/SessionAnalytics.md](../../CustomerSupportBot.Domain/Model/SessionAnalytics.md) — Oturum analytics
