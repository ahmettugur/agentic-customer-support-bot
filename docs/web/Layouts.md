# Layouts

**Klasör:** `Layout/`

Razor layout'ları — sayfa "kabukları". Her sayfa `@layout XxxLayout` direktifi ile bunlardan birini seçer.

| Layout | Sayfalar | Görünüm |
|---|---|---|
| `EmptyLayout` | Login, Chat, NotFound | Boş — nav yok |
| `AdminLayout` | Admin, Traces, Replay, Sla, WorkflowDesigner | Top nav (AdminNavBar) |
| `MainLayout` | (legacy, kullanılmıyor) | Sol sidebar + içerik |

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

Admin sayfalarının üst nav'ı.

```razor
@inject AuthService AuthSvc
@inject NavigationManager Nav

<nav class="admin-nav">
    <div class="brand">🤖 Müşteri Destek</div>
    <ul class="nav-links">
        <li><NavLink href="/" Match="NavLinkMatch.All">💬 Chat</NavLink></li>
        <li><NavLink href="/admin">🛡️ HITL Panel</NavLink></li>
        <li><NavLink href="/traces">🧠 Traces</NavLink></li>
        <li><NavLink href="/replay">🎬 Replay</NavLink></li>
        <li><NavLink href="/workflow-designer">🔧 Workflow</NavLink></li>
        <li><NavLink href="/sla">⏱️ SLA</NavLink></li>
    </ul>
    <button @onclick="LogoutAsync" class="logout-btn">Çıkış</button>
</nav>

@code {
    private async Task LogoutAsync()
    {
        await AuthSvc.LogoutAsync();
        Nav.NavigateTo("/login");
    }
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
        var currentUri = Nav.ToBaseRelativePath(Nav.Uri);
        Nav.NavigateTo($"/login?return=/{currentUri}", forceLoad: true);
    }
}
```

Mevcut URL `?return=` query param'a kopyalanır:

```
/admin → /login?return=/admin
/traces → /login?return=/traces
```

`Login.razor` başarılı login sonrası `return` query'sini okur, oraya yönlendirir.

### `forceLoad: true` neden?

Normal navigation Blazor router üzerinden gider — auth state aynı kalır. `forceLoad` ile **tam sayfa reload** yapılır:
- Yeni HTTP request
- `Program.cs` baştan başlar
- `AppAuthStateProvider` token'ı tekrar okur
- Login formu temiz görünür

---

## MainLayout (legacy)

`MainLayout.razor` ve `NavMenu.razor` projede mevcut ama **aktif route'larda kullanılmıyor**. Eski sol-sidebar tasarımının kalıntıları.

```razor
@inherits LayoutComponentBase

<div class="page">
    <div class="sidebar">
        <NavMenu />
    </div>
    <main>
        @Body
    </main>
</div>
```

Yeni sayfalar `AdminLayout` (top nav) tercih ediyor — modern dashboard hissi için.

⚠️ Cleanup gerekirse silinebilir.

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
@layout AdminLayout
@attribute [Authorize(Roles = "Admin,Agent")]
```

3 direktif:
- `@page "/admin"` — URL match
- `@layout AdminLayout` — kabuk
- `@attribute [Authorize]` — auth requirement

Aksi halde `App.razor` `DefaultLayout="@typeof(EmptyLayout)"` kullanır.

---

## Bağlantılar

- [Pages-Admin.md](Pages-Admin.md) — AdminLayout kullanımı
- [Pages-Chat.md](Pages-Chat.md) — EmptyLayout kullanımı
- [Auth.md](Auth.md) — `[Authorize]` ve `RedirectToLogin`
