# StaffBadgeState

**Dosya:** `Services/StaffBadgeState.cs` (scoped)

## Ne İşe Yarar

Kenar çubuğundaki rozet sayılarını (bekleyen onay, açık eskalasyon, canlı sohbet, ders önerisi) taşıyan
küçük paylaşılan durumdur.

## Hangi Amaçla Kullanılır

Sayıları [Admin](../Pages/Admin.md) sayfası zaten 15 saniyede bir çekiyor; her güncellemede `SetQueues` /
`SetProposedLessons` ile buraya yazar. [`StaffSidebar`](../Layout/StaffSidebar.md) `OnChange` olayına abone
olup rozetleri yeniler.

## Kullanılma Nedeni ve Tasarım Yaklaşımı

Kenar çubuğu her sayfada görünür; kendi yoklamasını yapsaydı her personel sayfası ek istek atar ve "general"
hız sınırı kotasını boşa tüketirdi. Bunun yerine son bilinen değerler gösterilir; hiç yüklenmemişse (ör. doğrudan
`/traces` açıldı) değer `null`'dır ve rozet gizlenir. Değer değişmediyse olay tetiklenmez.

## Metotlar / Üyeler

| Üye | Açıklama |
|-----|----------|
| `PendingApprovals`, `OpenEscalations`, `ActiveChats`, `ProposedLessons` | Son bilinen sayılar (`int?`). |
| `SetQueues(pending, escalations, chats)` | Üç kuyruk sayısını yazar; değiştiyse `OnChange` tetiklenir. |
| `SetProposedLessons(count)` | İyileştirme önerisi sayısı (İyileştirme önerileri bölümü yüklenince). |
| `OnChange` | Sayılardan biri değişince tetiklenir. |
