# ConversationRating

**Dosya:** `Model/ConversationRating.cs`  
**Tür:** `class` (mutable)  
**İlişkili:** `AnalyticsDashboard` class (aynı dosyada)

## 1. Ne İşe Yarar

Konuşma sonunda müşterinin bıraktığı **1-5 yıldız değerlendirmesini** ve opsiyonel yorumunu tutar.

## 2. Hangi Amaçla Kullanılır

Chat sona erdiğinde kullanıcıya değerlendirme formu gösterilir. Gönderilen veri bu modelle kaydedilir. Admin panelindeki Analytics dashboard'da ortalama puan, dağılım ve son yorumlar bu veriden türetilir.

> 💡 **Analiz notu:** Müşteri memnuniyeti ölçümü — "Görüşmenizi nasıl değerlendirirsiniz? ⭐⭐⭐⭐⭐" sorusunun cevabı.

## 3. Metotlar / Üyeler

| Üye | Tip | Açıklama |
| ----- | ----- | ---------- |
| `Id` | `string` | Benzersiz kimlik |
| `SessionId` | `string` | Hangi oturuma ait |
| `Stars` | `int` | 1–5 arası yıldız puanı |
| `Feedback` | `string?` | Opsiyonel kullanıcı yorumu |
| `RatedAt` | `DateTime` | Değerlendirme zamanı (UTC) |

---

# AnalyticsDashboard

**Tür:** `class`

Admin panelindeki analytics ekranının özet veri modeli. Tüm oturumlar üzerinden hesaplanan istatistikleri taşır.

| Üye | Tip | Açıklama |
| ----- | ----- | ---------- |
| `TotalSessions` | `int` | Toplam oturum sayısı |
| `TotalMessages` | `int` | Toplam mesaj sayısı |
| `AverageSessionMessages` | `double` | Oturum başına ortalama mesaj |
| `AverageRating` | `double` | Ortalama yıldız puanı |
| `TotalRatings` | `int` | Toplam değerlendirme sayısı |
| `RatingDistribution` | `Dictionary<int, int>` | Yıldız dağılımı (ör. {5: 42, 4: 28, ...}) |
| `RecentRatings` | `List<ConversationRating>` | Son değerlendirmeler |
| `TotalApprovals` | `int` | Toplam onay sayısı |
| `ApprovedCount` | `int` | Onaylanan |
| `RejectedCount` | `int` | Reddedilen |
| `ExpiredCount` | `int` | Süresi dolan |
| `PendingCount` | `int` | Bekleyen |
| `TotalEscalations` | `int` | Toplam eskalasyon |
| `IntentDistribution` | `Dictionary<string, int>` | Niyet dağılımı |
| `PhaseDistribution` | `Dictionary<string, int>` | Faz dağılımı |
| `AverageSentimentScore` | `double` | Ortalama duygu skoru |
| `SentimentDistribution` | `Dictionary<string, int>` | Duygu dağılımı |

## Bağlantılar

- [../../CustomerSupportBot.Application/AnalyticsPortService.md](../../CustomerSupportBot.Application/Telemetry/AnalyticsPortService.md) — Bu modeli dolduran servis
