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
    private readonly NavigationManager _nav;

    public AppAuthStateProvider(AuthTokenStore store, NavigationManager nav)
    {
        _store = store;
        _nav = nav;
        // Route staff<->customer sınırını geçtiğinde (ör. NavLink ile) cascade'i
        // yeniden tetikle — aksi halde eski scope'un ClaimsPrincipal'ı cache'de kalır.
        _nav.LocationChanged += OnLocationChanged;
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e) => NotifyStateChanged();

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var scope = AuthScopeRouter.Resolve(_nav.ToBaseRelativePath(_nav.Uri));
        var token = await _store.ReadAsync(scope);
        if (token is null || string.IsNullOrWhiteSpace(token.AccessToken))
            return Anonymous;

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, token.Username),
            new Claim(ClaimTypes.Role, token.Role)
        ], "jwt");

        return new AuthenticationState(new ClaimsPrincipal(identity));
    }

    /// <summary>
    /// Login/Logout sonrası Blazor cascade'ini tetikler.
    /// </summary>
    public void NotifyStateChanged()
        => NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());

    public void Dispose() => _nav.LocationChanged -= OnLocationChanged;
}
