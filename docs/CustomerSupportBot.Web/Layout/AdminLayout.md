# AdminLayout & AdminNavBar

## Ne İşe Yarar
Admin sayfaları için layout yapısı ve üst navigasyon barını sağlar.

## Hangi Amaçla Kullanılır
`/admin/*` yolundaki tüm sayfalar bu layout'u kullanır. Üst barda navigasyon linkleri, tema toggle ve kullanıcı bilgisi gösterilir.

## Sorumlulukları

### AdminLayout.razor
- `@Body` render alanını tanımlamak.
- `AdminNavBar` bileşenini üst barda göstermek.

### AdminNavBar.razor
- Admin navigasyon menüsü: Admin Panel, Traces, Replay, SLA, Knowledge.
- Aktif sayfayı vurgulamak.
- Dark/Light tema toggle butonu.
- Kullanıcı rolü ve adı gösterme.
- Logout butonu.

## Diğer Katman ve Bileşenlerle İlişkileri
- **DI ile inject edilen**: `NavigationManager`, [AuthService](../Services/AuthService.md), [AppAuthStateProvider](../Services/AppAuthStateProvider.md), [ThemeService](../Services/ThemeService.md).
- **Kullanan sayfalar**: [Admin](../Pages/Admin.md), [Traces](../Pages/Traces.md), [Sla](../Pages/Sla.md), [Knowledge](../Pages/Knowledge.md).
- **CSS**: `AdminNavBar.razor.css` (component-scoped).

## Bağımlılıklar
- [AuthService](../Services/AuthService.md) — Logout.
- [AppAuthStateProvider](../Services/AppAuthStateProvider.md) — Kullanıcı bilgisi.
- [ThemeService](../Services/ThemeService.md) — Tema toggle.
