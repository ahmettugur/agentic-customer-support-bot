# Auth — Login, Token Store, State Provider

**Dosyalar:**
- `Services/AuthTokenStore.cs`
- `Services/AuthService.cs`
- `Services/AppAuthStateProvider.cs`
- `Services/AuthorizedHttpClientHandler.cs`

JWT + refresh token akışının client tarafı.

---

## Bileşen rolleri

| Bileşen | Sorumluluk |
|---|---|
| `AuthTokenStore` | localStorage R/W (tek doğruluk kaynağı) |
| `AuthService` | API ile login/refresh/logout |
| `AppAuthStateProvider` | Token → ClaimsPrincipal dönüşümü |
| `AuthorizedHttpClientHandler` | HTTP request'lere Bearer token ekle + 401 retry |

---

## AuthTokenStore

`localStorage`'da JSON olarak token saklar.

```csharp
public sealed class AuthTokenStore(IJSRuntime js)
{
    private const string StorageKey = "cs.auth";

    public async Task<AuthTokenData?> ReadAsync()
    {
        var raw = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return JsonSerializer.Deserialize<AuthTokenData>(raw, JsonOptions);
    }

    public async Task WriteAsync(AuthTokenData? data)
    {
        if (data is null)
            await js.InvokeVoidAsync("localStorage.removeItem", StorageKey);
        else
        {
            var json = JsonSerializer.Serialize(data, JsonOptions);
            await js.InvokeVoidAsync("localStorage.setItem", StorageKey, json);
        }
    }

    public async Task<string?> GetAccessTokenAsync()
        => (await ReadAsync())?.AccessToken;
}

public sealed record AuthTokenData(
    string AccessToken,
    string RefreshToken,
    string Username,
    string Role
);
```

JSON camelCase policy ile serialize/deserialize edilir. `WriteAsync(null)` → `removeItem`.

### localStorage neden?

| Storage | Kalıcılık | XSS riski |
|---|---|---|
| `localStorage` | Sürekli | Yüksek |
| `sessionStorage` | Tab kapanınca silinir | Yüksek |
| `HttpOnly cookie` | Server kontrolü | Düşük |
| In-memory | Sayfa reload'da kaybolur | Düşük |

Bu proje **localStorage** seçti:
- ✅ Refresh sonrası kullanıcı tekrar login olmaz
- ⚠️ XSS varsa token çalınabilir — Blazor default escape eder
- 🔒 Access token kısa ömürlü — risk sınırlı

---

## AuthService

API ile auth endpoint'lerine konuşur. **Raw HttpClient** kullanır (AuthorizedHttpClientHandler **yok**).

```csharp
public sealed class AuthService(HttpClient http, AuthTokenStore store)
```

### `LoginAsync`

```csharp
public async Task<AuthTokenData> LoginAsync(string username, string password)
{
    var response = await http.PostAsJsonAsync("/auth/login", new { username, password });

    if (!response.IsSuccessStatusCode)
    {
        var msg = await response.Content.ReadAsStringAsync();
        throw new InvalidOperationException(string.IsNullOrWhiteSpace(msg)
            ? $"Login başarısız (HTTP {(int)response.StatusCode})"
            : msg);
    }

    var data = await response.Content.ReadFromJsonAsync<AuthTokenData>()
        ?? throw new InvalidOperationException("Sunucudan geçersiz yanıt alındı.");

    await store.WriteAsync(data);
    return data;
}
```

Başarısız → `InvalidOperationException`. Sayfa katmanı yakalar, kullanıcıya gösterir.

### `LogoutAsync`

```csharp
public async Task LogoutAsync()
{
    var current = await store.ReadAsync();
    if (current?.RefreshToken is not null)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/logout");
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", current.AccessToken);
            request.Content = JsonContent.Create(new { current.RefreshToken });
            await http.SendAsync(request);
        }
        catch { /* ignore */ }
    }

    await store.WriteAsync(null);
}
```

Refresh token revoke edilir, localStorage temizlenir.

### `TryRefreshAsync` — parallel coalescing

```csharp
private Task? _refreshTask;

public async Task<AuthTokenData?> TryRefreshAsync()
{
    var current = await store.ReadAsync();
    if (current?.RefreshToken is null) return null;

    if (_refreshTask is not null)
    {
        await _refreshTask;
        return await store.ReadAsync();
    }

    var tcs = new TaskCompletionSource();
    _refreshTask = tcs.Task;
    try
    {
        var response = await http.PostAsJsonAsync("/auth/refresh",
            new { current.RefreshToken });

        if (!response.IsSuccessStatusCode)
        {
            await store.WriteAsync(null);
            return null;
        }

        var next = await response.Content.ReadFromJsonAsync<AuthTokenData>();
        await store.WriteAsync(next);
        return next;
    }
    catch { return null; }
    finally
    {
        _refreshTask = null;
        tcs.SetResult();
    }
}
```

**Senaryo:** 3 API çağrısı paralel 401 alır:
- İlk çağrı `_refreshTask = tcs.Task` set eder, refresh isteği gönderir
- Sonraki 2 çağrı `await _refreshTask` ile bekler — tek refresh request
- Refresh bitince hepsi `store.ReadAsync()` ile yeni token alır

Aksi halde 3 refresh request server'a giderdi → rotation chain bozulur.

---

## AppAuthStateProvider

Blazor'un `AuthenticationStateProvider` base class'ını implement eder.

```csharp
public sealed class AppAuthStateProvider(AuthTokenStore store) : AuthenticationStateProvider
{
    private static readonly AuthenticationState Anonymous =
        new(new ClaimsPrincipal(new ClaimsIdentity()));

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var token = await store.ReadAsync();
        if (token is null || string.IsNullOrWhiteSpace(token.AccessToken))
            return Anonymous;

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, token.Username),
            new Claim(ClaimTypes.Role, token.Role)
        ], "jwt");

        return new AuthenticationState(new ClaimsPrincipal(identity));
    }

    public void NotifyStateChanged()
        => NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
}
```

### Token decode yapmıyor?

JWT'yi parse etmiyor — login response'taki `Username`/`Role` field'larını kullanır. Server bu bilgileri JWT'ye de zaten koyuyor; frontend için duplicate parsing gereksiz.

### `[Authorize(Roles="Admin")]` nasıl çalışır?

```razor
@page "/admin"
@attribute [Authorize(Roles = "Admin")]
```

`AuthorizeRouteView` `ClaimsPrincipal`'da `Role=Admin` claim'i arar. Yoksa `NotAuthorized` template'i (`RedirectToLogin`) çalışır.

### Role-based prefix (AdminApiService)

```csharp
var role = state.User.FindFirst(ClaimTypes.Role)?.Value;
return role == "Agent" ? "/agent" : string.Empty;
```

Agent rolü `/agent/*` prefix'ini kullanır; Admin rolü direkt endpoint'leri kullanır.

---

## AuthorizedHttpClientHandler

DelegatingHandler — her HTTP request'e Bearer token ekler ve 401 → refresh → retry yapar.

```csharp
public sealed class AuthorizedHttpClientHandler(
    AuthTokenStore store,
    AuthService authService,
    NavigationManager nav,
    AppAuthStateProvider authState) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // /auth/* endpoint'lerine token ekleme (login/refresh döngüsünü önler)
        if (request.RequestUri?.AbsolutePath.StartsWith("/auth/") == true)
            return await base.SendAsync(request, cancellationToken);

        var token = await store.GetAccessTokenAsync();
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode != System.Net.HttpStatusCode.Unauthorized)
            return response;

        // 401 — refresh dene
        var refreshed = await authService.TryRefreshAsync();
        if (refreshed is null)
        {
            authState.NotifyStateChanged();
            var returnTo = Uri.EscapeDataString(nav.Uri);
            nav.NavigateTo($"/login?return={returnTo}", forceLoad: false);
            return response;
        }

        // Yeni token ile tekrar dene (request clone)
        var retry = await CloneRequestAsync(request);
        retry.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", refreshed.AccessToken);
        return await base.SendAsync(retry, cancellationToken);
    }
}
```

### Request clone

HttpRequestMessage bir kez gönderildikten sonra tekrar gönderilemez. `CloneRequestAsync` headers + content'i kopyalar:

```csharp
private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage original)
{
    var clone = new HttpRequestMessage(original.Method, original.RequestUri);
    foreach (var header in original.Headers)
        clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
    if (original.Content is not null)
    {
        var bytes = await original.Content.ReadAsByteArrayAsync();
        clone.Content = new ByteArrayContent(bytes);
        foreach (var header in original.Content.Headers)
            clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
    }
    return clone;
}
```

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
   - Yoksa → Anonymous
   ↓
Router → /admin
   - [Authorize] check
   - Anonymous ise → RedirectToLogin → /login?return=/admin
   ↓
Login.razor → AuthService.LoginAsync
   - POST /auth/login
   - AuthTokenStore.WriteAsync(data)
   - AppAuthStateProvider.NotifyStateChanged
   ↓
/admin yeniden render → [Authorize] geçer
   ↓
AdminApiService.GetPendingApprovalsAsync()
   - HttpClient → AuthorizedHttpClientHandler
   - Authorization: Bearer <token>
   - 200 OK
   ↓
[Token expire sonra]
   ↓
AdminApiService → 401
   - AuthorizedHttpClientHandler → TryRefreshAsync() (parallel coalescing)
   - POST /auth/refresh
   - Yeni token store'a yazılır
   - Request clone + retry → 200 OK
```

---

## Bağlantılar

- [Api Endpoints-Auth](../api/Endpoints-Auth.md) — server tarafı
- [Adapters.Persistence AuthAdapters](../adapters-persistence/AuthAdapters.md) — BCrypt + JWT detayları
- [Services.md](Services.md) — API service'lerin token kullanımı
- [Program.md](Program.md) — DI kayıtları
