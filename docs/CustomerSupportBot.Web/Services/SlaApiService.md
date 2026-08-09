# SlaApiService

## Ne İşe Yarar
SLA (Service Level Agreement) dashboard verilerini backend'den çeken HTTP istemci servisidir.

## Hangi Amaçla Kullanılır
`Sla.razor` sayfasında SLA durum özeti ve olay geçmişi gösterilirken kullanılır.

## Sorumlulukları
- SLA genel durum bilgisini çekmek (`/sla/status`): approval/escalation bekleyen sayılar, ihlal süreleri.
- SLA olay geçmişini çekmek (`/sla/events`): uyarı, ihlal, otomatik aksiyon kayıtları.

## Diğer Katman ve Bileşenlerle İlişkileri
- **DI ile inject edilen**: `HttpClient`.
- **Kullanan bileşen**: `Pages/Sla.razor`.
- **Backend karşılığı**: `CustomerSupportBot.Api` → `SlaEndpoints`.
- **Model bağımlılığı**: [AdminModels](../Models/AdminModels.md) — `SlaStatus`, `SlaEvent`, `SlaEventsResponse`.

## Kullanılma Nedeni ve Tasarım Yaklaşımı
Hata yutan okuma deseni. SLA verileri operasyonel metriklerdir; erişilemezse UI "veri yok" gösterir.

## Metotlar / Üyeler

| Metot | Açıklama |
|-------|----------|
| `GetStatusAsync()` | SLA genel durumunu döner (enabled, approval/escalation istatistikleri). |
| `GetEventsAsync(count)` | Son N SLA olayını listeler. |

## Bağımlılıklar
- `HttpClient`
- [AdminModels](../Models/AdminModels.md)
