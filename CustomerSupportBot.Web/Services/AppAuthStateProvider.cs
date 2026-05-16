using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace CustomerSupportBot.Web.Services;

/// <summary>
/// localStorage'daki JWT token'ından Blazor için ClaimsPrincipal üretir.
/// AuthorizeRouteView ve [Authorize] attribute'u buna bağlıdır.
/// </summary>
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

    /// <summary>
    /// Login/Logout sonrası Blazor cascade'ini tetikler.
    /// </summary>
    public void NotifyStateChanged()
        => NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
}
