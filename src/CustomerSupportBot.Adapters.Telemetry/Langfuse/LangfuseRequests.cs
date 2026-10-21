// Adapters.Telemetry/Langfuse/LangfuseRequests.cs
// Langfuse public API istekleri için ortak: Basic Auth (public key : secret key).

using System.Net.Http.Headers;
using System.Text;

namespace CustomerSupportBot.Adapters.Telemetry.Langfuse;

internal static class LangfuseRequests
{
    public static HttpRequestMessage Create(HttpMethod method, LangfuseOptions options, string pathAndQuery, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, $"{options.BaseUrl.TrimEnd('/')}{pathAndQuery}") { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.PublicKey}:{options.SecretKey}")));
        return request;
    }
}
