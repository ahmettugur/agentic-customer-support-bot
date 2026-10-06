# AdminLayout

**Dosya:** `Layout/AdminLayout.razor`

## Ne İşe Yarar

Personel sayfaları (`/admin`, `/traces`, `/replay`, `/sla`, `/knowledge`) için kabuk (shell) layout'udur:
solda [`StaffSidebar`](StaffSidebar.md), sağda sayfa içeriği (`@Body`) ve sayfa altında toast alanı.

## Hangi Amaçla Kullanılır

Her personel sayfasının `@layout AdminLayout` direktifiyle bu kabuğa sarıldığı yerdir. Sayfa değiştiğinde
yalnızca `@Body` yeniden render edilir; kenar çubuğu ve toast alanı sabit kalır.

## Sorumlulukları

- `admin.css` stylesheet'ini yüklemek (ortak sayfa başlığı `.page-header`, `.live-toggle`, `.segmented` gibi
  paylaşılan stiller burada).
- `.staff-shell` (flex) içinde [`StaffSidebar`](StaffSidebar.md) ve `.staff-main` içinde `@Body`'yi yerleştirmek.
- [`ToastContainer`](../Components/ToastContainer.md) bileşenini sayfa altında tutmak.

## Diğer Katman ve Bileşenlerle İlişkileri

- `LayoutComponentBase`'den türer.
- Alt bileşenler: [`StaffSidebar`](StaffSidebar.md), [`ToastContainer`](../Components/ToastContainer.md).
- Kullanan sayfalar: [Admin](../Pages/Admin.md), [Traces](../Pages/Traces.md), [Replay](../Pages/Replay.md), [Sla](../Pages/Sla.md), [Knowledge](../Pages/Knowledge.md).

## Kullanılma Nedeni ve Tasarım Yaklaşımı

Kod içermeyen saf bir kompozisyon dosyasıdır; davranış (gezinme, tema, çıkış) [`StaffSidebar`](StaffSidebar.md)'dadır.

## Metotlar / Üyeler

Yok — yalnızca markup kompozisyonu.
