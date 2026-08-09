# ThemeService

## Ne İşe Yarar
Dark/Light tema tercihini yönetir; JavaScript interop üzerinden tarayıcıda tema değişikliği uygular.

## Hangi Amaçla Kullanılır
Navigasyon menüsündeki tema toggle butonunda ve sayfa ilk yüklenirken mevcut temayı okumak için kullanılır.

## Sorumlulukları
- Sayfa ilk yüklendiğinde mevcut temayı JS'den okumak (`csbTheme.get`).
- Tema toggle isteğinde JS fonksiyonunu çağırmak (`csbTheme.toggle`).
- `OnChange` event'i ile tema değişikliğini dinleyen bileşenleri bilgilendirmek.

## Diğer Katman ve Bileşenlerle İlişkileri
- **DI ile inject edilen**: `IJSRuntime`.
- **Kullanan bileşenler**: `NavMenu.razor`, `AdminNavBar.razor`.
- **JS bağımlılığı**: `wwwroot` içindeki `csbTheme` JavaScript nesnesi.

## Kullanılma Nedeni ve Tasarım Yaklaşımı
Tema bilgisi tarayıcı DOM'unda yaşar (CSS class veya `data-theme` attribute). Blazor WASM'dan DOM'a doğrudan erişim olmadığı için JS interop kullanılır. `_initialized` flag'i ile gereksiz tekrar okuma önlenir.

## Metotlar / Üyeler

| Metot / Üye | Açıklama |
|-------------|----------|
| `IsDark` | Mevcut temanın dark olup olmadığı (readonly property). |
| `OnChange` | Tema değişikliğinde tetiklenen event. |
| `EnsureInitAsync()` | İlk çağrıda JS'den temayı okur; sonraki çağrılarda noop. |
| `ToggleAsync()` | Dark ↔ Light toggle; JS'de uygular, `OnChange` tetikler. |

## Bağımlılıklar
- `IJSRuntime` — `csbTheme.get`, `csbTheme.toggle` JS interop.
