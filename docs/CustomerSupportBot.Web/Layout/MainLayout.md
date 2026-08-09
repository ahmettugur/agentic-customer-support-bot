# MainLayout & NavMenu

## Ne İşe Yarar
Uygulamanın varsayılan layout yapısı ve sol navigasyon menüsünü sağlar.

## Hangi Amaçla Kullanılır
Admin olmayan sayfalar (müşteri chat vb.) için kullanılan genel layout'tur.

## Sorumlulukları

### MainLayout.razor
- `@Body` render alanını tanımlamak.
- `NavMenu` bileşenini sol menüde göstermek.

### NavMenu.razor
- Navigasyon linkleri.
- Responsive menü toggle (mobil).
- [ToastContainer](../Components/ToastContainer.md) entegrasyonu.

## Diğer Katman ve Bileşenlerle İlişkileri
- **CSS**: `MainLayout.razor.css`, `NavMenu.razor.css` (component-scoped).
- **Alt bileşenler**: [ToastContainer](../Components/ToastContainer.md).

## Bağımlılıklar
Minimal — temel Blazor layout bileşeni.
