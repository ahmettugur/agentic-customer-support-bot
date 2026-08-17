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
    AppAuthStateProvider authState,
    ToastService toast) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // /auth/* endpoint'lerine token ekleme (login/refresh döngüsünü önler)
        if (request.RequestUri?.AbsolutePath.StartsWith("/auth/") == true)
            return await base.SendAsync(request, cancellationToken);

        // İsteği başlatan sayfanın (geçerli route) hangi kimlik alanına ait olduğu —
        // staff ve customer token'ları ayrı tutulduğu için bu, hangisinin ekleneceğini belirler.
        var scope = AuthScopeRouter.Resolve(nav.ToBaseRelativePath(nav.Uri));

        var token = await store.GetAccessTokenAsync(scope);
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await base.SendAsync(request, cancellationToken);

        // 403 — token geçerli ama rol yetmiyor. Servisler bu durumu genelde "boş liste"ye
        // çeviriyor (bkz. TracesApiService, SlaApiService), yani kullanıcıya bir şey
        // göstermezsek yetki hatası "veri yok" gibi görünür. Toast ile ayırt edilebilir kılınır.
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            toast.ShowError("Bu işlem için yetkiniz yok.");

        if (response.StatusCode != System.Net.HttpStatusCode.Unauthorized)
            return response;

        // 401 — refresh dene
        var refreshed = await authService.TryRefreshAsync(scope);
        if (refreshed is null)
        {
            authState.NotifyStateChanged();
            var returnTo = Uri.EscapeDataString(nav.Uri);
            var loginPage = scope == AuthScope.Customer ? "/customer-login" : "/login";
            nav.NavigateTo($"{loginPage}?return={returnTo}", forceLoad: false);
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
