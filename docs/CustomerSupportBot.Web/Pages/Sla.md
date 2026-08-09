# Sla.razor

## Ne İşe Yarar
SLA (Service Level Agreement) izleme dashboard'udur. Approval ve escalation'ların SLA durumlarını, ihlalleri ve uyarıları görüntüler.

## Hangi Amaçla Kullanılır
Admin panelinde SLA metriklerini izlemek ve geçmiş SLA olaylarını incelemek için kullanılır.

## Sorumlulukları
- SLA genel durumunu göstermek (pending counts, en eski bekleyen, ihlal sayıları).
- Approval ve escalation SLA ayrı kartlarda göstermek.
- SLA olay geçmişini tablo olarak listelemek (timestamp, kind, severity, action).
- Otomatik yenileme (polling).

## Diğer Katman ve Bileşenlerle İlişkileri
- **DI ile inject edilen**: [SlaApiService](../Services/SlaApiService.md).
- **Model bağımlılığı**: [AdminModels](../Models/AdminModels.md) — `SlaStatus`, `SlaEvent`.
- **Backend karşılığı**: `SlaEndpoints`.

## Bağımlılıklar
- [SlaApiService](../Services/SlaApiService.md).
- `Sla.razor.css` — Component-scoped stiller.
