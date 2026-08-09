# AnalyticsApiService

## Ne İşe Yarar
Admin analytics dashboard ve oturum bazlı analitik verilerini backend'den çeken HTTP istemci servisidir.

## Hangi Amaçla Kullanılır
`Admin.razor` sayfasındaki Analytics sekmesinde dashboard istatistikleri ve oturum detay analitiği gösterilirken kullanılır. JavaScript'teki `analyticsDashboard` ve `sessionAnalytics` çağrılarının C# karşılığıdır.

## Sorumlulukları
- Genel analytics dashboard verilerini (`/analytics/dashboard`) çekmek.
- Oturum bazlı analitik verilerini (`/analytics/session/{sessionId}`) çekmek.

## Diğer Katman ve Bileşenlerle İlişkileri
- **DI ile inject edilen**: `HttpClient` (Bearer token zincirli).
- **Kullanan bileşen**: `Pages/Admin.razor`.
- **Backend karşılığı**: `CustomerSupportBot.Api` → `AnalyticsEndpoints`.

## Kullanılma Nedeni ve Tasarım Yaklaşımı
Hata yutan (exception-swallowing) okuma deseni; başarısız API çağrılarında `null` döner, UI tarafı bunu "veri yok" olarak işler.

## Metotlar / Üyeler

| Metot | Açıklama |
|-------|----------|
| `GetDashboardAsync()` | Toplam oturum, mesaj, ortalama rating, sentiment dağılımı vb. döner. |
| `GetSessionAnalyticsAsync(sessionId)` | Belirli oturum için detaylı analitik (sentiment timeline, approval/escalation istatistikleri). |

## Bağımlılıklar
- `HttpClient`
- [AdminModels](../Models/AdminModels.md) — `AnalyticsDashboard`, `SessionAnalyticsModel` record'ları.
