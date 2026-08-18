// Api/Endpoints/A2AAuthEndpoints.cs
// A2A token değişimi — partner makine kimliği → tek müşteriye kilitli özne token'ı.

using System.Security.Claims;
using CustomerSupportBot.Application.Services.A2A;

namespace CustomerSupportBot.Api.Endpoints;

public static class A2AAuthEndpoints
{
    /// <summary>Değişim isteği. Partner kimliği gövdeden DEĞİL, token'dan okunur.</summary>
    public sealed record ExchangeRequest(string CustomerId);

    public static IEndpointRouteBuilder MapA2AAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth/a2a");

        // Partner kendi makine kimliğiyle kimlik doğrular ve belirli bir müşteri adına hareket
        // etmek için kısa ömürlü bir özne token'ı ister (RFC 8693 deseni).
        group.MapPost("/token-exchange", async (
            ExchangeRequest req,
            ClaimsPrincipal caller,
            A2ATokenExchangeService exchange,
            CancellationToken ct) =>
        {
            // Partner kimliği ÇAĞIRANIN TOKEN'INDAN alınır, istek gövdesinden değil — aksi
            // halde herhangi bir partner kendini başka bir partner gibi tanıtabilirdi.
            //
            // KULLANICI ADI okunur, NameIdentifier/sub DEĞİL: ikincisi kullanıcı satırının
            // GUID'idir ve her yeniden seed'de/ortamda değişir. Partner kimliği yapılandırmaya
            // elle yazılıyor (A2A:Partners) ve rate-limit anahtarında görünüyor; bu yüzden
            // insan tarafından okunabilir ve ortamlar arası kararlı olmak zorunda.
            // (Bu ayrım örnek istemci gerçek uygulamaya karşı koşturulunca ortaya çıktı:
            //  yapılandırmada 'demo-partner' yazıyordu, token'dan GUID okunuyordu, eşleşmiyordu.)
            var partnerId = caller.FindFirst(ClaimTypes.Name)?.Value
                         ?? caller.FindFirst("unique_name")?.Value;

            if (string.IsNullOrWhiteSpace(partnerId))
                return Results.Forbid();

            var result = await exchange.ExchangeAsync(partnerId, req.CustomerId, ct);

            // Yetkisiz ile "müşteri yok" ayrımı BİLEREK yapılmıyor: ayrım yapılsaydı, dışarıdan
            // müşteri numarası taranarak hangi numaraların var olduğu öğrenilebilirdi.
            if (result is null) return Results.Forbid();

            return Results.Ok(new
            {
                accessToken = result.AccessToken,
                expiresAt = result.ExpiresAt,
                customerId = result.CustomerId,
                tokenType = "Bearer"
            });
        })
        .RequireAuthorization("Partner")
        .RequireRateLimiting("a2a");

        return app;
    }
}
