using Microsoft.JSInterop;
using System.Text.Json;

namespace CustomerSupportBot.Web.Services;

/// <summary>
/// localStorage'da saklanan JWT token verilerini yönetir.
/// auth.js'teki STORAGE_KEY = 'cs.auth' ile uyumludur.
/// </summary>
public sealed class AuthTokenStore(IJSRuntime js)
{
    private const string StorageKey = "cs.auth";

    public async Task<AuthTokenData?> ReadAsync()
    {
        try
        {
            var raw = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            if (string.IsNullOrWhiteSpace(raw)) return null;
            return JsonSerializer.Deserialize<AuthTokenData>(raw, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public async Task WriteAsync(AuthTokenData? data)
    {
        if (data is null)
        {
            await js.InvokeVoidAsync("localStorage.removeItem", StorageKey);
        }
        else
        {
            var json = JsonSerializer.Serialize(data, JsonOptions);
            await js.InvokeVoidAsync("localStorage.setItem", StorageKey, json);
        }
    }

    public async Task<string?> GetAccessTokenAsync()
        => (await ReadAsync())?.AccessToken;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}

public sealed record AuthTokenData(
    string AccessToken,
    string RefreshToken,
    string Username,
    string Role
);
