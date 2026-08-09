# ToastContainer

## Ne İşe Yarar
Toast bildirimlerini ekranın sağ üst köşesinde render eden Blazor bileşenidir.

## Hangi Amaçla Kullanılır
`MainLayout` veya `AdminLayout` içinde yerleştirilir; [ToastService](../Services/ToastService.md)'ten gelen bildirimleri otomatik görüntüler ve zamanlayıcı ile kaldırır.

## Sorumlulukları
- `ToastService.OnShow` event'ini dinlemek.
- Toast mesajlarını stack halinde render etmek.
- 4 saniye sonra otomatik kaldırma (animasyonlu fade-out).
- Manuel kapatma butonu.
- Toast tipine göre ikon seçme (✓, ✕, ⚠, ℹ).
- `aria-live="polite"` ile erişilebilirlik.

## Diğer Katman ve Bileşenlerle İlişkileri
- **DI ile inject edilen**: [ToastService](../Services/ToastService.md).
- **Implements**: `IDisposable` — event aboneliğini temizler.

## Kullanılma Nedeni ve Tasarım Yaklaşımı
Observer pattern — ToastService event yayınlar, bu bileşen dinler ve render eder. `InvokeAsync` ile UI thread'e marshal edilir (Blazor thread safety). Çıkış animasyonu için `Removing` flag'i ve `RemoveAnimMs` delay kullanılır.

## Bağımlılıklar
- [ToastService](../Services/ToastService.md)
