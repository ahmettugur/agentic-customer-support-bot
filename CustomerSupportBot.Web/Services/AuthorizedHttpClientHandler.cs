using Microsoft.AspNetCore.Components;
using System.Net.Http.Headers;

namespace CustomerSupportBot.Web.Services;

/// <summary>
/// Tüm API isteklerine otomatik Bearer token ekler.
/// 401 alınırsa refresh dener; başarısız olursa /login'e yönlendirir.
/// auth.js'teki authFetch mantığının C# karşılığı.
/// </summary>
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

        // Yeni token ile tekrar dene
        var retry = await CloneRequestAsync(request);
        retry.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", refreshed.AccessToken);

        return await base.SendAsync(retry, cancellationToken);
    }

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
}
