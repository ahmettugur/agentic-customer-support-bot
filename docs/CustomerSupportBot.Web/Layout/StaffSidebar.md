# StaffSidebar

**Dosya:** `Layout/StaffSidebar.razor` (+ `StaffSidebar.razor.css`)

## Ne İşe Yarar

Personel yüzeyinin (yönetici/temsilci) sol kenar çubuğudur. Eski üst menünün (`AdminNavBar`) yerini aldı:
gruplu gezinme, kuyruk rozetleri, kullanıcı bilgisi, tema düğmesi ve çıkış tek yerde.

## Hangi Amaçla Kullanılır

[`AdminLayout`](AdminLayout.md) her personel sayfasında (`/admin`, `/traces`, `/replay`, `/sla`, `/knowledge`)
bu bileşeni sayfanın solunda gösterir. Admin panelinin bölümleri adreslidir (`/admin?tab=…`), bu yüzden
menüdeki her bağlantı başka bir sayfadan da doğrudan ilgili bölümü açar.

## Sorumlulukları

- Gruplu menü: **İş kuyruğu** (Onaylar, Eskalasyonlar, Canlı sohbetler), **Kayıtlar** (Konuşmalar,
  Siparişler, Karar geçmişi), **Raporlar** (Analiz, SLA), **Yönetim** (Hazır yanıtlar, İyileştirme
  önerileri, Bilgi tabanı), **Geliştirici** (İzler, Tekrar oynatma, Müşteri sohbeti — yeni sekmede).
- Role göre süzme: temsilci yalnızca Eskalasyonlar ve Canlı sohbetler'i görür (sayfa tarafındaki kısıt:
  [`AdminTabs`](../Helpers/AdminTabs.md)).
- Etkin bağlantıyı adresten hesaplamak (`NavLink` sorgu dizesini ayırt etmediği için elle; `/admin`
  sorgusuz açıldığında rolün varsayılan bölümü etkin sayılır).
- Rozetler: [`StaffBadgeState`](../Services/StaffBadgeState.md)'ten okunur — kenar çubuğu kendisi istek atmaz.
- Alt kısım: baş harfler, ad (varsa `GivenName`), rol, tema düğmesi ([`ThemeService`](../Services/ThemeService.md))
  ve çıkış ([`AuthService.LogoutAsync(AuthScope.Staff)`](../Services/AuthService.md) →
  [`AppAuthStateProvider.NotifyStateChanged()`](../Services/AppAuthStateProvider.md) → `/login`).
- Mobil (≤ 860px): kenar çubuğu soldan açılan çekmeceye döner; ekranın altında en sık kullanılan kuyruklar ve
  "Menü" düğmesi olan bir hızlı erişim çubuğu çıkar. Üst menü yoktur. Gezinince çekmece kapanır.

## Diğer Katman ve Bileşenlerle İlişkileri

- **`@inject`**: `NavigationManager`, [`AuthService`](../Services/AuthService.md),
  [`AppAuthStateProvider`](../Services/AppAuthStateProvider.md), [`ThemeService`](../Services/ThemeService.md),
  [`StaffBadgeState`](../Services/StaffBadgeState.md).
- İkonlar: [`Icon`](../Components/Icon.md).
- Barındıran bileşen: [`AdminLayout`](AdminLayout.md).

## Kullanılma Nedeni ve Tasarım Yaklaşımı

Eski yapıda iki yatay menü vardı (üst bar + Admin sayfasının 12 sekmelik şeridi); Replay ve Traces ikisinde de
tekrar ediyordu ve içerik ~200px aşağıdan başlıyordu. Bölümler artık adresli olduğundan tek bir gruplu menü
her ikisinin yerini alıyor. Renkler yalnızca tema değişkenlerinden gelir; karanlık tema ayrı kural gerektirmez.

`IDisposable` uygular: `LocationChanged`, `StaffBadgeState.OnChange`, `ThemeService.OnChange` ve
`AuthenticationStateChanged` abonelikleri kaldırılır.
