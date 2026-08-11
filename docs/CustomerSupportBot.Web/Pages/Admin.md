# Admin.razor

## Ne İşe Yarar
Admin ve Agent panelinin ana sayfasıdır. Approvals, escalations, aktif chat oturumları, analytics dashboard, improvement yönetimi ve agent listesini sekme tabanlı arayüzde sunar.

## Hangi Amaçla Kullanılır
`/admin` route'unda, `Admin` veya `Agent` rolüyle erişilir. Tüm yönetim işlemlerinin tek noktadan yapıldığı kapsamlı dashboard'dur.

## Sorumlulukları
- Sekme navigasyonu: Approvals, Escalations, Chat Sessions, Analytics, Improvements, Agents, Sessions.
- Approval onay/red işlemleri (gerekçe zorunluluğu dahil). Onay Kuyruğu sekmesindeki kayıt, admin
  karar verene ya da çok uzun süre (varsayılan 72 saat, `ApprovalOptions.StalePendingHours`)
  yanıtsız kalırsa arka planda otomatik reddedilene kadar kuyrukta bekler — sabit bir saniye
  sayacı yoktur (bkz. [`SlaPortService.md`](../../CustomerSupportBot.Application/Sla/SlaPortService.md#slaapprovalsonbreach-varsayılanı--autoreject--none), eskiden burada yanlışlıkla 60 saniyede otomatik reddeden bir SLA config'i vardı).
- Escalation yönetimi (acknowledge, resolve, dismiss, replan).
- Aktif chat oturumlarını izleme, mesaj geçmişi görme, takeover/release, mesaj gönderme.
- Analytics dashboard ve oturum bazlı analitik (sentiment timeline, grafikler).
- Improvement mining, lesson onay/red.
- Agent listesi.
- Oturum listesi.
- Otomatik veri yenileme (polling).

## Diğer Katman ve Bileşenlerle İlişkileri
- **DI ile inject edilen**: [AdminApiService](../Services/AdminApiService.md), [AnalyticsApiService](../Services/AnalyticsApiService.md), [ToastService](../Services/ToastService.md), `NavigationManager`.
- **Layout**: `AdminLayout` (yan navigasyon barı dahil).
- **Authorization**: `[Authorize(Roles = "Admin, Agent")]`.
- **Code-behind**: `Admin.razor.cs` — C# iş mantığı ayrı dosyada.

## Kullanılma Nedeni ve Tasarım Yaklaşımı
Tüm yönetim işlemleri tek sayfada toplanmıştır (SPA yaklaşımı). Code-behind pattern'i (`Admin.razor.cs`) kullanılır çünkü sayfa çok büyüktür (~60K+ satır markup + ~28K logic).

## Bağımlılıklar
- [AdminApiService](../Services/AdminApiService.md), [AnalyticsApiService](../Services/AnalyticsApiService.md), [ToastService](../Services/ToastService.md).
- [AdminModels](../Models/AdminModels.md) — Tüm DTO'lar.
