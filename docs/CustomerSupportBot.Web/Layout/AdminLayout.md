# AdminLayout

**Dosya:** `Layout/AdminLayout.razor`

## Ne İşe Yarar

Admin/Agent paneli sayfaları (`/admin`, `/traces`, `/replay`, `/sla`, `/knowledge`) için
kabuk (shell) layout'udur: üst navigasyon barını ve toast bildirim alanını sabit tutar,
sayfa içeriğini `@Body` ile ortasına basar.

## Hangi Amaçla Kullanılır

Her admin/agent sayfasının `@layout AdminLayout` direktifiyle bu kabuğa sarıldığı yerdir.
Sayfa değiştiğinde yalnızca `@Body` yeniden render edilir, üst bar ve toast alanı sabit kalır.

## Sorumlulukları

- `admin.css` stylesheet'ini yüklemek.
- [`AdminNavBar`](AdminNavBar.md) bileşenini üstte sabit göstermek.
- `@Body` ile aktif sayfa içeriğini render etmek.
- [`ToastContainer`](../Components/ToastContainer.md) bileşenini sayfa altında tutmak — böylece
  hangi admin sayfasında olunursa olunsun toast bildirimleri (ör. "Onay kaydedildi") aynı yerde çıkar.

## Diğer Katman ve Bileşenlerle İlişkileri

- `LayoutComponentBase`'den türer (`@inherits`), Blazor'un standart layout mekanizmasını kullanır.
- Alt bileşenler: [`AdminNavBar`](AdminNavBar.md), [`ToastContainer`](../Components/ToastContainer.md).
- Kullanan sayfalar: [Admin](../Pages/Admin.md), [Traces](../Pages/Traces.md), [Replay](../Pages/Replay.md), [Sla](../Pages/Sla.md), [Knowledge](../Pages/Knowledge.md).

## Kullanılma Nedeni ve Tasarım Yaklaşımı

Kod içermeyen saf bir kompozisyon dosyasıdır — kendi state'i veya `@code` bloğu yoktur. Tüm
mantık (tema, logout, navigasyon) [`AdminNavBar`](AdminNavBar.md)'a devredilmiştir; bu ayrım
tek-sorumluluk ilkesine hizmet eder: layout yalnızca "hangi bileşenler nerede duracak"ı
belirler, davranışı barındırmaz.

## Metotlar / Üyeler

Yok — yalnızca markup kompozisyonu.

## Bağımlılıklar

Yok (constructor injection / `@inject` kullanılmaz).
