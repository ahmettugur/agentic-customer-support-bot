using System.Net.Http.Json;

namespace CustomerSupportBot.Web.Services;

/// <summary>
/// Backend /auth endpoint'leriyle iletişim kurar.
/// auth.js'teki login / logout / tryRefresh fonksiyonlarının C# karşılığı.
/// </summary>
public sealed class AuthService(HttpClient http, AuthTokenStore store)
{
    public async Task<AuthTokenData> LoginAsync(string username, string password)
    {
        var response = await http.PostAsJsonAsync("/auth/login", new { username, password });
        return await ReadAuthResponseAsync(response, "Login başarısız");
    }

    public async Task<AuthTokenData> CustomerLoginAsync(string email, string password)
    {
        var response = await http.PostAsJsonAsync("/auth/customer/login", new { email, password });
        return await ReadAuthResponseAsync(response, "Giriş başarısız");
    }

    public async Task<AuthTokenData> CustomerRegisterAsync(string email, string password, string customerId)
    {
        var response = await http.PostAsJsonAsync("/auth/customer/register", new { email, password, customerId });
        return await ReadAuthResponseAsync(response, "Kayıt başarısız");
    }

    private async Task<AuthTokenData> ReadAuthResponseAsync(HttpResponseMessage response, string errorPrefix)
    {
        if (!response.IsSuccessStatusCode)
        {
            string? msg = null;
            try
            {
                var errorBody = await response.Content.ReadFromJsonAsync<ErrorBody>();
                msg = errorBody?.Error;
            }
            catch { /* body JSON değilse (ör. boş 401) alttaki generic mesaja düş */ }

            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(msg)
                    ? $"{errorPrefix} (HTTP {(int)response.StatusCode})"
                    : msg);
        }

        var data = await response.Content.ReadFromJsonAsync<AuthTokenData>()
            ?? throw new InvalidOperationException("Sunucudan geçersiz yanıt alındı.");

        await store.WriteAsync(data);
        return data;
    }

    public async Task LogoutAsync()
    {
        var current = await store.ReadAsync();
        if (current?.RefreshToken is not null)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/logout");
                request.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", current.AccessToken);
                request.Content = JsonContent.Create(new { current.RefreshToken });
                await http.SendAsync(request);
            }
            catch { /* ignore */ }
        }

        await store.WriteAsync(null);
    }

    private Task? _refreshTask;

    public async Task<AuthTokenData?> TryRefreshAsync()
    {
        var current = await store.ReadAsync();
        if (current?.RefreshToken is null) return null;

        // Paralel çağrıları birleştir
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
        catch
        {
            return null;
        }
        finally
        {
            _refreshTask = null;
            tcs.SetResult();
        }
    }

    private sealed record ErrorBody(string? Error);
}
