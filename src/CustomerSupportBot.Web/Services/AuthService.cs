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
        return await ReadAuthResponseAsync(response, "Login başarısız", AuthScope.Staff);
    }

    public async Task<AuthTokenData> CustomerLoginAsync(string email, string password)
    {
        var response = await http.PostAsJsonAsync("/auth/customer/login", new { email, password });
        return await ReadAuthResponseAsync(response, "Giriş başarısız", AuthScope.Customer);
    }

    public async Task<AuthTokenData> CustomerRegisterAsync(string email, string password, string customerId)
    {
        var response = await http.PostAsJsonAsync("/auth/customer/register", new { email, password, customerId });
        return await ReadAuthResponseAsync(response, "Kayıt başarısız", AuthScope.Customer);
    }

    private async Task<AuthTokenData> ReadAuthResponseAsync(HttpResponseMessage response, string errorPrefix, AuthScope scope)
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

        await store.WriteAsync(scope, data);
        return data;
    }

    public async Task LogoutAsync(AuthScope scope)
    {
        var current = await store.ReadAsync(scope);
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

        await store.WriteAsync(scope, null);
    }

    // Scope başına en fazla bir refresh çağrısı — paralel istekler aynı Task'e biner.
    //
    // Kayıt İLK await'ten ÖNCE ve senkron yapılır. Eskiden önce localStorage okunuyor, kayıt
    // ancak ondan sonra yapılıyordu: okuması uçuştaki refresh'ten ÖNCE başlayıp SONRA biten
    // ikinci bir çağrı, kayıt temizlenmiş olduğu için eski (artık döndürülmüş) refresh
    // token'la ikinci bir istek atıyordu. Sunucu bunu reddedip (token yeniden kullanımı)
    // oturumu düşürüyordu.
    private readonly Dictionary<AuthScope, Task<AuthTokenData?>> _refreshTasks = new();
    private readonly object _refreshGate = new();

    public Task<AuthTokenData?> TryRefreshAsync(AuthScope scope)
    {
        lock (_refreshGate)
        {
            if (_refreshTasks.TryGetValue(scope, out var inFlight)) return inFlight;

            var flight = new TaskCompletionSource<AuthTokenData?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _refreshTasks[scope] = flight.Task;
            _ = RunRefreshAsync(scope, flight);
            return flight.Task;
        }
    }

    private async Task RunRefreshAsync(AuthScope scope, TaskCompletionSource<AuthTokenData?> flight)
    {
        AuthTokenData? result = null;
        try
        {
            result = await RefreshCoreAsync(scope);
        }
        catch
        {
            result = null;
        }
        finally
        {
            // Önce kayıt kaldırılır, sonra sonuç yayınlanır: sonuçtan SONRA gelen bir çağrı
            // (ör. yeni access token da süresini doldurduğunda) bitmiş uçuşa binmez, yeni
            // bir refresh başlatır.
            lock (_refreshGate) _refreshTasks.Remove(scope);
            flight.TrySetResult(result);
        }
    }

    private async Task<AuthTokenData?> RefreshCoreAsync(AuthScope scope)
    {
        var current = await store.ReadAsync(scope);
        if (current?.RefreshToken is null) return null;

        var response = await http.PostAsJsonAsync("/auth/refresh",
            new { current.RefreshToken });

        if (!response.IsSuccessStatusCode)
        {
            // Aynı localStorage'ı paylaşan başka bir sekme bu arada yenilemiş olabilir: o
            // sekme token'ı döndürdü, bizimki reddedildi. Depoyu silmek diğer sekmenin yeni
            // oturumunu da düşürürdü — depodaki token değiştiyse onu kullan.
            var latest = await store.ReadAsync(scope);
            if (latest?.RefreshToken is not null && latest.RefreshToken != current.RefreshToken)
                return latest;

            await store.WriteAsync(scope, null);
            return null;
        }

        var next = await response.Content.ReadFromJsonAsync<AuthTokenData>();
        await store.WriteAsync(scope, next);
        return next;
    }

    private sealed record ErrorBody(string? Error);
}
