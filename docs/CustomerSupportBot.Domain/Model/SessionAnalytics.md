# SessionAnalytics

**Dosya:** `Model/SessionAnalytics.cs`  
**Tür:** `class` (mutable)  
**İlişkili:** `SentimentTimelineEntry`, `SessionRatingInfo`, `ApprovalSummary`, `EscalationSummary` (aynı dosyada)

## 1. Ne İşe Yarar

Tek bir oturum için detaylı analytics verilerini taşıyan **DTO**'dur. Admin panelinin oturum detay sayfasında görüntülenir.

## 2. Hangi Amaçla Kullanılır

`AnalyticsPortService` bu modeli doldurur. Oturum bazında mesaj sayısı, duygu zaman çizelgesi, değerlendirme, onay ve eskalasyon detaylarını içerir.

## 3. Metotlar / Üyeler

| Üye | Tip | Açıklama |
|-----|-----|----------|
| `SessionId` | `string` | Oturum ID |
| `CreatedAt` | `DateTime` | Oluşturulma |
| `LastActivity` | `DateTime` | Son aktivite |
| `MessageCount` | `int` | Mesaj sayısı |
| `TurnCount` | `int` | Tur sayısı |
| `CurrentIntent` | `string?` | Mevcut niyet |
| `Phase` | `string?` | Mevcut faz |
| `CustomerId` | `string?` | Müşteri ID |
| `Sentiment` | `string?` | Mevcut duygu |
| `SentimentScore` | `double` | Duygu skoru |
| `SentimentTimeline` | `List<SentimentTimelineEntry>` | Duygu zaman çizelgesi |
| `Rating` | `SessionRatingInfo?` | Değerlendirme bilgisi |
| `ApprovalDetails` | `List<ApprovalSummary>` | Onay detayları |
| `EscalationDetails` | `List<EscalationSummary>` | Eskalasyon detayları |

## Bağlantılar

- [ConversationRating.md](ConversationRating.md) — Değerlendirme modeli
- [ApprovalRequest.md](ApprovalRequest.md) — Onay modeli
- [EscalationRequest.md](EscalationRequest.md) — Eskalasyon modeli
