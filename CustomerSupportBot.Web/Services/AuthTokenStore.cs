using Microsoft.JSInterop;
using System.Text.Json;

namespace CustomerSupportBot.Web.Services;

/// <summary>
/// localStorage'da saklanan JWT token verilerini yönetir. Staff (Admin/Agent) ve
/// Customer token'ları ayrı anahtarlarda tutulur, böylece aynı tarayıcıda ikisi de
/// aynı anda aktif olabilir ve birbirinin oturumunu ezmez.
/// </summary>
public sealed class AuthTokenStore(IJSRuntime js)
{
    private static string KeyFor(AuthScope scope) =>
        scope == AuthScope.Customer ? "cs.auth.customer" : "cs.auth.staff";

    public async Task<AuthTokenData?> ReadAsync(AuthScope scope)
    {
        try
        {
            var raw = await js.InvokeAsync<string?>("localStorage.getItem", KeyFor(scope));
            if (string.IsNullOrWhiteSpace(raw)) return null;
            return JsonSerializer.Deserialize<AuthTokenData>(raw, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public async Task WriteAsync(AuthScope scope, AuthTokenData? data)
    {
        if (data is null)
        {
            await js.InvokeVoidAsync("localStorage.removeItem", KeyFor(scope));
        }
        else
        {
            var json = JsonSerializer.Serialize(data, JsonOptions);
            await js.InvokeVoidAsync("localStorage.setItem", KeyFor(scope), json);
        }
    }

    public async Task<string?> GetAccessTokenAsync(AuthScope scope)
        => (await ReadAsync(scope))?.AccessToken;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}

public sealed record AuthTokenData(
    string AccessToken,
    string RefreshToken,
    string Username,
    string Role,
    // FullName: müşterinin adı soyadı — yalnızca Customer rolünde dolu (staff'ta null).
    // Username e-posta olduğu için kullanıcıya gösterime uygun tek alan budur.
    string? FullName = null
);
