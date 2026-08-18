// Iki adimli kimlik: partner girisi -> token degisimi -> ozne token'i.
//
// Bu iki cagri A2A protokolune AIT DEGILDIR. A2A token dagitimini tanimlamaz; kart
// yalnizca "bearer gerekli" diye ilan eder, token'in nasil alinacagi banda disi birakilir.
// Bu yuzden burasi duz HTTP'dir — protokol cagrilari (kart kesfi, mesaj gonderimi)
// A2A SDK tipleriyle yapilir.

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.A2AClient.Sample.A2A;

/// <summary>
/// Partner ve ozne token'larini alir, suresi dolmadan yeniler.
///
/// <para>
/// <b>Yenileme neden sart:</b> ozne token'i sunucu tarafinda kisa omurludur
/// (<c>A2A:SubjectTokenMinutes</c>, varsayilan 5 dakika). Etkilesimli bir oturumda
/// kullanici bu sureden uzun konusur; token bir kez alinip saklansaydi, sohbetin
/// ortasinda cagrilar 401'e duser ve bu, ajanin "cevap veremiyorum" demesi olarak
/// gorunurdu — yani altyapi eksigi YANLIS OLGUYA donusurdu.
/// </para>
/// </summary>
public sealed class A2ATokenProvider(
    IHttpClientFactory httpFactory,
    IConfiguration config,
    ILogger<A2ATokenProvider> logger)
{
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _partnerToken;
    private string? _subjectToken;
    private DateTimeOffset _subjectExpiresAt = DateTimeOffset.MinValue;

    public string CustomerId => config["A2A:CustomerId"] ?? "1027";

    /// <summary>Partner token'i — "hangi SISTEM ariyor" sorusunun cevabi.</summary>
    public async Task<string> GetPartnerTokenAsync(CancellationToken ct = default)
    {
        if (_partnerToken is not null) return _partnerToken;

        await _lock.WaitAsync(ct);
        try
        {
            if (_partnerToken is not null) return _partnerToken;

            var http = httpFactory.CreateClient("auth");
            var user = config["A2A:Partner:Username"] ?? "demo-partner";
            var pass = config["A2A:Partner:Password"] ?? "Partner123!";

            var resp = await http.PostAsJsonAsync("/auth/login", new { username = user, password = pass }, ct);
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"Partner girisi basarisiz ({(int)resp.StatusCode}). Kanal acik mi (A2A:Enabled=true)? "
                  + "Partner hesabi YALNIZCA kanal acikken seed edilir.");

            var body = await resp.Content.ReadFromJsonAsync<JsonElement>(Json, ct);
            _partnerToken = body.GetProperty("accessToken").GetString()
                ?? throw new InvalidOperationException("Yanitta accessToken yok.");

            logger.LogInformation("Partner token alindi (kullanici={User}).", user);
            return _partnerToken;
        }
        finally { _lock.Release(); }
    }

    /// <summary>
    /// Ozne token'i — "hangi MUSTERI adina" sorusunun cevabi. Tek musteriye kilitli.
    /// Suresi dolmak uzereyse seffaf sekilde yenilenir.
    /// </summary>
    public async Task<string> GetSubjectTokenAsync(CancellationToken ct = default)
    {
        if (_subjectToken is not null && DateTimeOffset.UtcNow + RefreshMargin < _subjectExpiresAt)
            return _subjectToken;

        await _lock.WaitAsync(ct);
        try
        {
            if (_subjectToken is not null && DateTimeOffset.UtcNow + RefreshMargin < _subjectExpiresAt)
                return _subjectToken;

            var partner = await GetPartnerTokenAsyncNoLock(ct);
            var http = httpFactory.CreateClient("auth");
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", partner);

            var resp = await http.PostAsJsonAsync("/auth/a2a/token-exchange", new { customerId = CustomerId }, ct);
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"Token degisimi reddedildi ({(int)resp.StatusCode}). "
                  + $"A2A:Partners altinda bu partner icin '{CustomerId}' izinli mi?");

            var body = await resp.Content.ReadFromJsonAsync<JsonElement>(Json, ct);
            _subjectToken = body.GetProperty("accessToken").GetString()
                ?? throw new InvalidOperationException("Yanitta accessToken yok.");
            _subjectExpiresAt = body.TryGetProperty("expiresAt", out var exp) && exp.TryGetDateTimeOffset(out var at)
                ? at
                : DateTimeOffset.UtcNow.AddMinutes(5);

            logger.LogInformation(
                "Ozne token'i alindi (musteri={Customer}, gecerlilik={Expiry:HH:mm:ss}).",
                CustomerId, _subjectExpiresAt.ToLocalTime());

            return _subjectToken;
        }
        finally { _lock.Release(); }
    }

    private async Task<string> GetPartnerTokenAsyncNoLock(CancellationToken ct)
    {
        if (_partnerToken is not null) return _partnerToken;

        var http = httpFactory.CreateClient("auth");
        var user = config["A2A:Partner:Username"] ?? "demo-partner";
        var pass = config["A2A:Partner:Password"] ?? "Partner123!";
        var resp = await http.PostAsJsonAsync("/auth/login", new { username = user, password = pass }, ct);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(Json, ct);
        _partnerToken = body.GetProperty("accessToken").GetString();
        return _partnerToken!;
    }
}
