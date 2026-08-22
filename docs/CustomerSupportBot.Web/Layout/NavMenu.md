# NavMenu

**Dosya:** `Layout/NavMenu.razor`

## Ne İşe Yarar

[`MainLayout`](MainLayout.md) için basit bir navigasyon barı: marka logosu, "Sohbet"/"Admin Panel"
linkleri ve dark/light tema toggle butonu içerir.

## Hangi Amaçla Kullanılır

`MainLayout` fiilen bir fallback layout olduğundan (bkz. [`MainLayout.md`](MainLayout.md)),
`NavMenu` de pratikte yalnızca `@layout` direktifi unutulmuş/eksik bir sayfa render edildiğinde
görünür — projedeki asıl sayfalar (`Chat`, `Admin` vb.) kendi özel layout'larını kullanır.

## Sorumlulukları

- Marka logosu ve "Müşteri Destek Botu" başlığını göstermek.
- `/` (Sohbet) ve `/admin` (Admin Panel) linklerini sunmak.
- Dark/light tema toggle butonu; ilk render'da [`ThemeService.EnsureInitAsync`](../Services/ThemeService.md)
  ile tema tercihini yükler ve `ThemeSvc.OnChange` olayına abone olur.

> 🐞 **Doküman düzeltmesi:** Bu dosya için daha önce yazılan doküman "responsive mobil menü
> toggle" ve "ToastContainer entegrasyonu" iddia ediyordu — gerçek kodda **ikisi de yok**.
> `NavMenu.razor`'da mobil hamburger menü mantığı veya `ToastContainer` referansı bulunmuyor;
> bu satırlar muhtemelen [`AdminNavBar`](AdminNavBar.md)/[`AdminLayout`](AdminLayout.md) ile
> karıştırılmıştı (o ikisi de kendi bağlamında farklı özellikler taşıyor). Aşağıdaki içerik
> doğrudan `NavMenu.razor` kaynağından çıkarılmıştır.

## Diğer Katman ve Bileşenlerle İlişkileri

- **`@inject` ile alınan**: [`ThemeService`](../Services/ThemeService.md).
- `IDisposable` uygular — `Dispose()` içinde `ThemeSvc.OnChange` aboneliğini kaldırır.
- CSS: `NavMenu.razor.css` (component-scoped).
- Barındıran bileşen: [`MainLayout`](MainLayout.md).

## Kullanılma Nedeni ve Tasarım Yaklaşımı

[`AdminNavBar`](AdminNavBar.md) ile neredeyse aynı tema-toggle mantığını tekrar eder (aynı
`OnAfterRenderAsync`/`ToggleAsync`/`Dispose` üçlüsü) — ortak bir base class veya paylaşılan
bileşene çıkarılmamış olması, `MainLayout`'un fiilen kullanılmayan bir fallback olmasıyla
açıklanabilir: iki ayrı navbar'ı birleştirmenin getirisi, aktif olarak kullanılmayan bir
yol için düşük önceliklidir.

## Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `OnAfterRenderAsync(bool firstRender)` | İlk render'da tema servisini başlatır ve `OnChange` olayına abone olur. |
| `ToggleAsync()` | `ThemeSvc.ToggleAsync()` çağırarak dark/light temayı değiştirir. |
| `Dispose()` | `ThemeSvc.OnChange` aboneliğini kaldırır. |

## Bağımlılıklar

- [`ThemeService`](../Services/ThemeService.md) — Tema okuma/değiştirme.
