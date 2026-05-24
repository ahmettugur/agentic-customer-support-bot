# Auth — Login, Token Store, State Provider

**Dosyalar:**
- `Services/AuthService.cs`
- `Services/AppAuthStateProvider.cs`
- `Services/AuthTokenStore.cs`
- `Services/AuthorizedHttpClientHandler.cs`
- `Pages/Login.razor`
- `Layout/RedirectToLogin.razor`

JWT + refresh token akışının client tarafı.

---

## Bileşen rolleri

| Bileşen | Sorumluluk |
|---|---|
| `AuthTokenStore` | localStorage R/W (tek doğruluk) |
| `AuthService` | API ile login/refresh/logout |
| `AppAuthStateProvider` | Token → ClaimsPrincipal dönüşümü |
| `AuthorizedHttpClientHandler` | HTTP request'lere Bearer token ekle |
| `Login.razor` | UI |
| `RedirectToLogin.razor` | `[Authorize]` fail durumunda yönlendirme |

---

## AuthTokenStore

localStorage'da JSON olarak token saklar.

```csharp
public sealed class AuthTokenStore
{
    private const string Key = "cs.auth";

    public async Task<AuthTokenData?> ReadAsync(IJSRuntime js)
    {
        var json = await js.InvokeAsync<string?>("localStorage.getItem", Key);
        if (string.IsNullOrWhiteSpace(json)) return null;
        return JsonSerializer.Deserialize<AuthTokenData>(json);
    }

    public async Task WriteAsync(IJSRuntime js, AuthTokenData data)
    {
        var json = JsonSerializer.Serialize(data);
        await js.InvokeVoidAsync("localStorage.setItem", Key, json);
    }

    public async Task ClearAsync(IJSRuntime js)
    {
        await js.InvokeVoidAsync("localStorage.removeItem", Key);
    }
}

public sealed record AuthTokenData(
    string AccessToken,
    string RefreshToken,
    string Username,
    string Role);
```

### localStorage neden?

| Storage | Kalıcılık | XSS riski | CSRF riski |
|---|---|---|---|
| `localStorage` | Sürekli (kullanıcı temizleyene kadar) | Yüksek | Düşük |
| `sessionStorage` | Tab kapanınca silinir | Yüksek | Düşük |
| `HttpOnly cookie` | Server kontrolü | Düşük | Yüksek |
| In-memory | Sayfa reload'da kaybolur | Düşük | Düşük |

Bu proje **localStorage** seçti:
- ✅ Refresh sonrası kullanıcı tekrar login olmaz
- ⚠️ XSS varsa token çalınabilir — bu yüzden client kodunda XSS koruması kritik (Blazor default escape eder)
- 🔒 Access token kısa ömürlü (60 dakika) — risk sınırlı

---

## AuthService

API ile auth endpoint'lerine konuşur. **Raw HttpClient** kullanır (AuthorizedHttpClientHandler **yok**).

```csharp
public sealed class AuthService
{
    private readonly HttpClient _http;
    private readonly AuthTokenStore _store;
    private readonly AppAuthStateProvider _stateProvider;
    private readonly IJSRuntime _js;
    private Task<AuthTokenData?>? _refreshInflight;
    private readonly object _refreshLock = new();
}
```

### `LoginAsync`

```csharp
public async Task<bool> LoginAsync(string username, string password)
{
    var resp = await _http.PostAsJsonAsync("/auth/login", new { username, password });
    if (!resp.IsSuccessStatusCode) return false;

    var data = await resp.Content.ReadFromJsonAsync<LoginResponse>();
    if (data is null) return false;

    var token = new AuthTokenData(data.AccessToken, data.RefreshToken, data.User.Username, data.User.Role);
    await _store.WriteAsync(_js, token);
    _stateProvider.NotifyStateChanged();

    return true;
}
```

`NotifyStateChanged` → `CascadingAuthenticationState` tüm component'lere yeni auth state'i yayar → `[Authorize]` route'lar render edilir.

### `TryRefreshAsync` — parallel coalescing

Refresh işleminin **aynı anda iki kez** çağrılmasını önler:

```csharp
public async Task<AuthTokenData?> TryRefreshAsync()
{
    lock (_refreshLock)
    {
        if (_refreshInflight is not null)
            return await _refreshInflight;   // Mevcut refresh'i bekle

        _refreshInflight = DoRefreshAsync();
    }

    try
    {
        return await _refreshInflight;
    }
    finally
    {
        lock (_refreshLock) { _refreshInflight = null; }
    }
}

private async Task<AuthTokenData?> DoRefreshAsync()
{
    var current = await _store.ReadAsync(_js);
    if (current is null) return null;

    var resp = await _http.PostAsJsonAsync("/auth/refresh", new { refreshToken = current.RefreshToken });
    if (!resp.IsSuccessStatusCode)
    {
        await _store.ClearAsync(_js);
        return null;
    }

    var data = await resp.Content.ReadFromJsonAsync<RefreshResponse>();
    var newToken = new AuthTokenData(data.AccessToken, data.RefreshToken, current.Username, current.Role);
    await _store.WriteAsync(_js, newToken);
    return newToken;
}
```

**Senaryo:** 3 API çağrısı paralel 401 alır:
- 3 çağrı `TryRefreshAsync` çağırır
- İlk çağrı `_refreshInflight = DoRefreshAsync()` set eder
- Sonraki 2 çağrı **aynı task**'ı bekler — tek refresh request
- Refresh bitince hepsi yeni token alır

Aksi halde 3 refresh request server'a giderdi → rotation chain bozulur.

### `LogoutAsync`

```csharp
public async Task LogoutAsync()
{
    var current = await _store.ReadAsync(_js);
    if (current is not null)
    {
        await _http.PostAsJsonAsync("/auth/logout", new { refreshToken = current.RefreshToken });
    }
    await _store.ClearAsync(_js);
    _stateProvider.NotifyStateChanged();
}
```

Refresh token revoke edilir, localStorage temizlenir, state güncellenir.

---

## AppAuthStateProvider

Blazor'un `AuthenticationStateProvider` base class'ını implement eder.

```csharp
public sealed class AppAuthStateProvider : AuthenticationStateProvider
{
    private readonly AuthTokenStore _store;
    private readonly IJSRuntime _js;

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var token = await _store.ReadAsync(_js);
        if (token is null)
            return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));   // Anonim

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, token.Username),
            new Claim(ClaimTypes.Role, token.Role),
        };
        var identity = new ClaimsIdentity(claims, "jwt");
        return new AuthenticationState(new ClaimsPrincipal(identity));
    }

    public void NotifyStateChanged()
    {
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }
}
```

### Token decode yapmıyor?

JWT'yi parse etmiyor, sadece `AuthTokenData.Username` ve `Role` field'larını okuyor. Bu alanları **login response'tan** gelir:

```json
{
  "accessToken": "eyJ...",
  "refreshToken": "...",
  "user": { "username": "admin", "role": "Admin" }
}
```

Token'ı manuel decode etmek yerine login response'a güvenir — server bu bilgileri JWT'ye de zaten koyuyor. Frontend için duplicate parsing gereksiz.

### `[Authorize(Roles="Admin")]` nasıl çalışır?

```razor
@page "/admin"
@attribute [Authorize(Roles = "Admin")]
```

`AuthorizeRouteView` `ClaimsPrincipal`'da `Role=Admin` claim'i arar. Yoksa `NotAuthorized` template'i (`RedirectToLogin`) çalışır.

---

## AuthorizedHttpClientHandler

DelegatingHandler — her HTTP request'e Bearer token ekler.

```csharp
public sealed class AuthorizedHttpClientHandler : DelegatingHandler
{
    private readonly AuthTokenStore _store;
    private readonly IJSRuntime _js;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        var token = await _store.ReadAsync(_js);
        if (token is not null && !string.IsNullOrWhiteSpace(token.AccessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        }

        return await base.SendAsync(request, ct);
    }
}
```

Tüm API service'leri (`AdminApiService`, `ChatApiService`, vb.) bu handler ile yapılandırılmıştır:

```csharp
builder.Services.AddHttpClient<AdminApiService>(c => c.BaseAddress = new Uri(apiBase))
    .AddHttpMessageHandler<AuthorizedHttpClientHandler>();
```

### 401 handling neden burada yok?

Sadece **token inject** eder; 401 dönerse service kodu retry/refresh karar verir. Çünkü:

- Bazı endpoint'ler 401 dönmesi normal (eval scenarios anonim ama bazıları admin)
- Otomatik retry tüm endpoint'ler için uygun değil
- Service-level handling daha okunaklı

Pratik: service çağrısı 401 alırsa `AuthService.TryRefreshAsync` çağırır, başarılı olursa retry, başarısızsa `/login`'e yönlendirir.

---

## Login.razor

Login form sayfası.

```razor
@page "/login"
@layout EmptyLayout
@inject AuthService AuthSvc
@inject NavigationManager Nav

<EditForm Model="@_input" OnValidSubmit="HandleLogin">
    <DataAnnotationsValidator />

    <InputText @bind-Value="_input.Username" placeholder="admin" />
    <InputText @bind-Value="_input.Password" type="password" />

    <button type="submit" disabled="@_loading">
        @(_loading ? "Giriş yapılıyor…" : "Giriş Yap")
    </button>

    @if (!string.IsNullOrEmpty(_error))
    {
        <div class="error">@_error</div>
    }
</EditForm>

@code {
    private LoginInput _input = new();
    private bool _loading;
    private string? _error;

    private async Task HandleLogin()
    {
        _loading = true;
        _error = null;
        try
        {
            var ok = await AuthSvc.LoginAsync(_input.Username, _input.Password);
            if (ok)
            {
                var returnUrl = Nav.QueryString("return") ?? "/admin";
                Nav.NavigateTo(returnUrl);
            }
            else
            {
                _error = "Hatalı kullanıcı adı veya şifre";
            }
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
        finally
        {
            _loading = false;
        }
    }

    public sealed class LoginInput
    {
        [Required] public string Username { get; set; } = "admin";
        [Required] public string Password { get; set; } = "Admin123!";
    }
}
```

### Return URL deseni

```
1. Kullanıcı /admin'e gider (login yok)
2. RedirectToLogin → Nav.NavigateTo("/login?return=/admin")
3. Login başarılı → Nav.QueryString("return") = "/admin"
4. Kullanıcı geri /admin'e döner
```

Bu sayede user nereden geldiğini hatırlar — UX kaybı yok.

---

## RedirectToLogin.razor

`[Authorize]` başarısız olursa render edilir.

```razor
@inject NavigationManager Nav

@code {
    protected override void OnInitialized()
    {
        var current = Nav.ToBaseRelativePath(Nav.Uri);
        Nav.NavigateTo($"/login?return=/{current}", forceLoad: true);
    }
}
```

Mevcut URL'i `?return=` query param olarak login'e taşır.

---

## Token akış diyagramı

```
[Browser açılır]
   ↓
Program.cs.RunAsync()
   ↓
App.razor → CascadingAuthenticationState
   ↓
AppAuthStateProvider.GetAuthenticationStateAsync()
   - AuthTokenStore.ReadAsync(localStorage)
   - Token varsa → ClaimsPrincipal döner
   - Yoksa → Anonim
   ↓
Router → /admin
   - [Authorize] check
   - Anonim ise → RedirectToLogin
   ↓
Login.razor → AuthService.LoginAsync
   - POST /auth/login
   - AuthTokenStore.WriteAsync
   - AppAuthStateProvider.NotifyStateChanged
   ↓
/admin yeniden render → [Authorize] geçer
   ↓
AdminApiService.GetPendingApprovalsAsync()
   - HttpClient → AuthorizedHttpClientHandler
   - Authorization: Bearer <token>
   - 200 OK
   ↓
[Token expire — 60 dakika sonra]
   ↓
AdminApiService → 401
   - AuthService.TryRefreshAsync() (parallel coalescing)
   - POST /auth/refresh
   - Yeni token store'a yazılır
   - Retry → 200 OK
```

---

## Bağlantılar

- [Api Endpoints-Auth](../api/Endpoints-Auth.md) — server tarafı
- [Adapters.Persistence AuthAdapters](../adapters-persistence/AuthAdapters.md) — BCrypt + JWT detayları
- [Services.md](Services.md) — API service'lerin token kullanımı
