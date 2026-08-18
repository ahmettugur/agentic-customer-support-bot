// Token'i her giden istege merkezi olarak ekleyen handler'lar.
//
// Neden handler: A2A SDK'si istegi kendi kurar; cagri yerinde header eklemek icin
// bir kancamiz yok. Handler, token'i HttpClient boru hattinda enjekte eder — hem
// yenileme tek yerde kalir, hem de "bir cagrida token eklemeyi unutmak" mumkun olmaz.

using System.Net.Http.Headers;

namespace CustomerSupportBot.A2AClient.Sample.A2A;

/// <summary>Partner token'i ekler — yalnizca urun ajani bunu kabul eder.</summary>
public sealed class PartnerTokenHandler(A2ATokenProvider tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetPartnerTokenAsync(ct));
        return await base.SendAsync(request, ct);
    }
}

/// <summary>
/// Ozne token'i ekler — sipariş/sikayet ajanlari bunu ister.
/// Her istekte saglayiciya sorulur; suresi dolmak uzereyse orada yenilenir.
/// </summary>
public sealed class SubjectTokenHandler(A2ATokenProvider tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetSubjectTokenAsync(ct));
        return await base.SendAsync(request, ct);
    }
}
