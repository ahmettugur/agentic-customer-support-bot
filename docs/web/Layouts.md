# Layouts

**Klasör:** `Layout/`

Razor layout'ları — sayfa "kabukları". Her sayfa `@layout XxxLayout` direktifi ile bunlardan birini seçer.

| Layout | Sayfalar | Görünüm |
|---|---|---|
| `EmptyLayout` | Login, Chat | Boş — nav yok |
| `AdminLayout` | Admin, Traces, Replay, Sla, WorkflowDesigner | Top nav (AdminNavBar) |
| `MainLayout` | `App.razor`'ın `DefaultLayout`'u; NotFound bunu kullanır | Sol sidebar (NavMenu) + içerik |

---

## EmptyLayout

```razor
@inherits LayoutComponentBase

<div class="empty-layout">
    @Body
</div>
```

Hiçbir UI elementi yok — sadece sayfa içeriği render edilir. Login ve Chat için tercih edilir:
- **Login:** Tam ekran centered form
- **Chat:** Müşteri sohbet alanı — nav görünmesin (admin paneli olduğu sezdirilmesin)

---

## AdminLayout

```razor
@inherits LayoutComponentBase

<link href="css/admin.css" rel="stylesheet" />

<div class="admin-layout">
    <AdminNavBar />
    <main class="admin-main">
        @Body
    </main>
</div>
```

Üstte nav bar, altta sayfa içeriği. `admin.css` global olarak yüklenir — tüm admin sayfaları aynı stil.

### CSS scope

Blazor component-scoped CSS:
- `AdminLayout.razor.css` — sadece bu layout'a uygulanır
- `wwwroot/css/admin.css` — global (Blazor css isolation atlamalı)

`<link>` runtime'da DOM'a eklenir — global CSS dahil edilir.

---

## AdminNavBar

Admin sayfalarının üst nav'ı. `ThemeService`'e bağlı bir açık/koyu mod düğmesi ve `AuthorizeView` ile aktif kullanıcı adını gösterir.

```razor
@inject NavigationManager Nav
@inject AuthService AuthSvc
@inject AppAuthStateProvider AuthState
@inject ThemeService ThemeSvc
@implements IDisposable

<nav class="csb-topnav">
    <span class="csb-topnav-brand">CSB Admin</span>

    <NavLink href="/" Match="NavLinkMatch.All">💬 Sohbet</NavLink>
    <NavLink href="/admin">🛡️ HITL Panel</NavLink>
    <NavLink href="/traces">🧠 Trace Dashboard</NavLink>
    <NavLink href="/replay">🎬 Replay</NavLink>
    <NavLink href="/workflow-designer">🔧 Workflow Designer</NavLink>
    <NavLink href="/sla">⏱️ SLA</NavLink>

    <button @onclick="ToggleAsync" class="csb-theme-toggle"><!-- güneş/ay ikonu, ThemeSvc.IsDark'a göre --></button>

    <AuthorizeView>
        <Authorized>
            <span class="csb-topnav-user">👤 @context.User.Identity?.Name</span>
            <button @onclick="LogoutAsync" class="csb-topnav-logout">Çıkış</button>
        </Authorized>
    </AuthorizeView>
</nav>

@code {
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        await ThemeSvc.EnsureInitAsync();
        ThemeSvc.OnChange += StateHasChanged;
        StateHasChanged();
    }

    private async Task ToggleAsync() => await ThemeSvc.ToggleAsync();

    private async Task LogoutAsync()
    {
        await AuthSvc.LogoutAsync();
        AuthState.NotifyStateChanged();
        Nav.NavigateTo("/login", forceLoad: false);
    }

    public void Dispose() => ThemeSvc.OnChange -= StateHasChanged;
}
```

### `NavLink` component

Blazor built-in — aktif route ise `active` class ekler. CSS ile vurgulanır:

```css
.admin-nav .nav-links a.active {
    color: var(--primary);
    border-bottom: 2px solid var(--primary);
}
```

### Logout akışı

1. `AuthService.LogoutAsync()` — POST `/auth/logout`, localStorage clear
2. `NavigationManager.NavigateTo("/login")` — login sayfasına yönlendir
3. `AppAuthStateProvider` zaten `NotifyStateChanged` çağırdığı için Auth state güncel

---

## RedirectToLogin

`[Authorize]` fail olduğunda otomatik render edilir.

```razor
@inject NavigationManager Nav

@code {
    protected override void OnInitialized()
    {
        var returnTo = Uri.EscapeDataString(Nav.Uri);
        Nav.NavigateTo($"/login?return={returnTo}", forceLoad: false);
    }
}
```

Mevcut URL (tam URI, `Uri.EscapeDataString` ile encode edilmiş) `?return=` query param'a kopyalanır. `Login.razor` başarılı login sonrası `return` query'sini okur, oraya yönlendirir.

`forceLoad: false` — normal Blazor router navigasyonu kullanılır (tam sayfa reload yok); `AppAuthStateProvider.NotifyStateChanged()` zaten çağrıldığı için auth state güncel kalır.

---

## MainLayout

`App.razor`'daki `<AuthorizeRouteView DefaultLayout="@typeof(MainLayout)">` bu layout'u varsayılan yapar. `@layout` direktifiyle başka bir layout seçmeyen sayfalar (örn. `NotFound.razor`) buraya düşer.

```razor
@inherits LayoutComponentBase

<div class="main-layout-fallback">
    <NavMenu />
    <main class="main-layout-content">
        @Body
    </main>
</div>
```

Admin sayfaları (`Admin`, `Traces`, `Replay`, `Sla`, `WorkflowDesigner`) kendi `@layout AdminLayout` direktifleriyle bunu ezer — top nav (AdminNavBar) tercih ediyorlar. `MainLayout`/`NavMenu` fiilen sadece `NotFound` sayfasında ve DefaultLayout fallback'i olarak devrede.

---

## CSS organizasyonu

| Dosya | Kapsam |
|---|---|
| `wwwroot/css/app.css` | Global reset, font, color tokens |
| `wwwroot/css/admin.css` | Admin sayfaları ortak |
| `wwwroot/css/login.css` | Login için (Login.razor.css'e ek) |
| `wwwroot/css/traces.css` | Traces sayfası |
| `wwwroot/css/replay.css` | Replay |
| `wwwroot/css/sla.css` | SLA |
| `wwwroot/css/workflow.css` | Workflow Designer |
| `Layout/*.razor.css` | Component-scoped (Blazor CSS isolation) |
| `Pages/*.razor.css` | Aynı |

### Blazor CSS isolation

`Foo.razor.css` ⇒ build sırasında otomatik scope attribute eklenir:

```html
<div b-xyz123 class="card">...</div>
```

```css
.card[b-xyz123] {
    background: white;
}
```

Bu sayede component'lerin CSS'i birbirine sızmaz — global pollution yok.

---

## Routing ve layout seçimi

```razor
@page "/admin"
@attribute [Authorize]
@layout AdminLayout
```

3 direktif:
- `@page "/admin"` — URL match
- `@attribute [Authorize]` — auth requirement (rol kısıtı yok — herhangi bir authenticated kullanıcı: Admin veya Agent)
- `@layout AdminLayout` — kabuk

`@layout` direktifi verilmeyen sayfalarda `App.razor`'daki `AuthorizeRouteView DefaultLayout="@typeof(MainLayout)"` devreye girer.

---

## Bağlantılar

- [Pages-Admin.md](Pages-Admin.md) — AdminLayout kullanımı
- [Pages-Chat.md](Pages-Chat.md) — EmptyLayout kullanımı
- [Auth.md](Auth.md) — `[Authorize]` ve `RedirectToLogin`
