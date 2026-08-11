using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Routing;
using System.Security.Claims;

namespace CustomerSupportBot.Web.Services;

/// <summary>
/// localStorage'daki JWT token'ından Blazor için ClaimsPrincipal üretir.
/// AuthorizeRouteView ve [Authorize] attribute'u buna bağlıdır. Hangi token'ın
/// (Staff/Customer) kullanılacağı geçerli route'a göre belirlenir — böylece aynı
/// tarayıcıda admin ve müşteri oturumu aynı anda, birbirini etkilemeden var olabilir.
/// </summary>
public sealed class AppAuthStateProvider : AuthenticationStateProvider, IDisposable
{
    private static readonly AuthenticationState Anonymous =
        new(new ClaimsPrincipal(new ClaimsIdentity()));

    private readonly AuthTokenStore _store;
    private readonly AuthService _authService;
    private readonly NavigationManager _nav;

    public AppAuthStateProvider(AuthTokenStore store, AuthService authService, NavigationManager nav)
    {
        _store = store;
        _authService = authService;
        _nav = nav;
        // Route staff<->customer sınırını geçtiğinde (ör. NavLink ile) cascade'i
        // yeniden tetikle — aksi halde eski scope'un ClaimsPrincipal'ı cache'de kalır.
        _nav.LocationChanged += OnLocationChanged;
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e) => NotifyStateChanged();

    /// <summary>
    /// Bir token'ın süresi dolmuş sayılması için kalan pay — clock skew ve "tam bu anda
    /// sayfa render olurken süresi doldu" yarış durumunu tolere eder.
    /// </summary>
    private static readonly TimeSpan ExpiryBuffer = TimeSpan.FromSeconds(30);

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var scope = AuthScopeRouter.Resolve(_nav.ToBaseRelativePath(_nav.Uri));
        var token = await _store.ReadAsync(scope);
        if (token is null || string.IsNullOrWhiteSpace(token.AccessToken))
            return Anonymous;

        // localStorage'da bir token BULUNMASI onun GEÇERLİ olduğu anlamına gelmez — eskiden
        // burada yalnızca varlığı kontrol ediliyordu, süresi dolmuş bir token da "giriş yapılmış"
        // sayılıp AuthorizeRouteView korumalı sayfalara alıyordu. Kullanıcı sayfayı görüyor ama
        // ilk API çağrısında 401 alıyordu — üstelik chat akışı bu 401'i sessizce yutuyordu.
        // Burada, sayfa açılışında (ve her navigasyonda, bkz. OnLocationChanged) süre kontrolü
        // yapılıp mümkünse sessizce refresh edilir; refresh token da geçersizse Anonymous
        // dönülür ve AuthorizeRouteView kullanıcıyı RedirectToLogin'e düşürür.
        var expiry = JwtUtils.TryGetExpiryUtc(token.AccessToken);
        if (expiry is not null && expiry.Value <= DateTimeOffset.UtcNow.Add(ExpiryBuffer))
        {
            var refreshed = await _authService.TryRefreshAsync(scope);
            if (refreshed is null)
                return Anonymous;
            token = refreshed;
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, token.Username),
            new(ClaimTypes.Role, token.Role)
        };
        // Username e-posta; kullanıcıya hitap için gösterilebilir ad ayrı claim olarak taşınır.
        if (!string.IsNullOrWhiteSpace(token.FullName))
            claims.Add(new Claim(ClaimTypes.GivenName, token.FullName));

        var identity = new ClaimsIdentity(claims, "jwt");

        return new AuthenticationState(new ClaimsPrincipal(identity));
    }

    /// <summary>
    /// Login/Logout sonrası Blazor cascade'ini tetikler.
    /// </summary>
    public void NotifyStateChanged()
        => NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());

    public void Dispose() => _nav.LocationChanged -= OnLocationChanged;
}
