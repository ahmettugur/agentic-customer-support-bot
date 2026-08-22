# IAnalyticsPort

**Dosya:** `Ports/Inbound/IAnalyticsPort.cs`
**Tür:** `interface`
**Namespace:** `CustomerSupportBot.Application.Ports.Inbound`

## 1. Ne işe yarar?

Admin panelinin analitik/derecelendirme (rating) verilerini okumak ve yazmak için kullandığı primary (driving) port.

## 2. Hangi amaçla kullanılır?

Api katmanındaki analytics endpoint'leri, kullanıcıların oturum sonunda verdiği yıldız derecelendirmelerini kaydetmek ve admin dashboard'un özet istatistiklerini/oturum bazlı analitikleri göstermek için bu portu çağırır.

## 3. Sorumlulukları

- **Üstlendiği:** Derecelendirme CRUD'u, dashboard özeti, oturum bazlı analitik sorgulama sözleşmesini tanımlamak.
- **Üstlenmediği:** Verinin nasıl saklandığı (bellek içi mi, Postgres mi) — bu implementasyonun (`Services/Chat` altındaki servis) işidir, port sadece sözleşmedir.

## 4. Diğer katman/bileşenlerle ilişkileri

- Implementasyonu Application/Services altında bulunur ve DI ile kaydedilir.
- Api katmanındaki analytics/admin endpoint'leri bu arayüze bağımlıdır, somut sınıfa değil.

## 5. Kullanılma nedeni ve tasarım yaklaşımı

Hexagonal mimaride "primary/driving port" deseni: dış dünya (Api) Application katmanına bu arayüz üzerinden girer, somut implementasyonu bilmez. Bu, implementasyonun (ör. bellek-içi'den Postgres'e) değişmesi durumunda Api katmanının hiç etkilenmemesini sağlar.

## 6. Metotlar / Üyeler

| Metot | Açıklama |
|---|---|
| `ConversationRating Rate(string sessionId, int stars, string? comment = null)` | Belirtilen oturum için derecelendirme kaydeder. |
| `ConversationRating? GetRating(string sessionId)` | Tek bir oturumun derecelendirmesini döner. |
| `IReadOnlyList<ConversationRating> GetRecentRatings(int count = 20)` | Son N derecelendirmeyi döner. |
| `IReadOnlyList<ConversationRating> GetAllRatings()` | Tüm derecelendirmeleri döner. |
| `Task<object> GetSummaryAsync(CancellationToken ct = default)` | Özet istatistikler (ortalama puan, toplam sayı vb.) döner. |
| `Task<AnalyticsDashboard> GetDashboardAsync(CancellationToken ct = default)` | Tüm dashboard istatistiklerini döner. |
| `Task<SessionAnalytics?> GetSessionAnalyticsAsync(string sessionId, CancellationToken ct = default)` | Tek bir oturum için detaylı analitik döner. |

## 7. Bağımlılıklar

`CustomerSupportBot.Domain.Model` (`ConversationRating`, `AnalyticsDashboard`, `SessionAnalytics`).

## Bağlantılar

- Implementasyon: Application/Services/Chat altındaki analytics servisi.
