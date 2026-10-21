// KVKK: müşteri verisi silinince Langfuse'taki trace'ler de silinir. Langfuse v4'te trace'ler oturuma/kullanıcıya
// göre doğrudan aranamaz; gözlemler (v2) süzülür, traceId'ler toplanır, trace'ler toplu silinir.

using System.Net;
using System.Text;
using System.Text.Json;
using CustomerSupportBot.Adapters.Telemetry.Langfuse;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomerSupportBot.Adapters.Telemetry.Tests;

public class LangfuseDataEraserTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Call(HttpMethod Method, string Url, string? Body);

    /// <summary>Gözlem listesi isteklerine sırayla verilen sayfaları döner; diğer her isteğe 200 yanıt verir.</summary>
    private sealed class ScriptedHandler(params string[] observationPages) : HttpMessageHandler
    {
        private int _page;
        public List<Call> Calls { get; } = [];
        public HttpStatusCode ObservationStatus { get; init; } = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            Calls.Add(new Call(request.Method, request.RequestUri!.AbsoluteUri, body));

            if (request.Method == HttpMethod.Get && request.RequestUri.AbsolutePath == "/api/public/v2/observations")
            {
                if (ObservationStatus != HttpStatusCode.OK) return new HttpResponseMessage(ObservationStatus);
                var page = _page < observationPages.Length ? observationPages[_page++] : Page([]);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(page, Encoding.UTF8, "application/json") };
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }
    }

    private static string Page(string[] traceIds, string? cursor = null) =>
        JsonSerializer.Serialize(new
        {
            data = traceIds.Select(t => new { id = Guid.NewGuid().ToString("N")[..16], traceId = t }),
            meta = cursor is null ? new { } : (object)new { cursor }
        });

    private static LangfuseDataEraser Build(ScriptedHandler handler) =>
        new(new HttpClient(handler),
            new LangfuseOptions { Enabled = true, BaseUrl = "http://langfuse.test:3000", PublicKey = "pk", SecretKey = "sk" },
            NullLogger<LangfuseDataEraser>.Instance);

    private static string[] DeletedTraceIds(ScriptedHandler h) =>
        h.Calls.Where(c => c.Method == HttpMethod.Delete && c.Url.EndsWith("/api/public/traces"))
            .SelectMany(c => JsonDocument.Parse(c.Body!).RootElement.GetProperty("traceIds").EnumerateArray().Select(e => e.GetString()!))
            .ToArray();

    [Fact]
    public async Task EraseSessions_FiltersObservationsBySession_AndDeletesDistinctTraces()
    {
        var handler = new ScriptedHandler(Page(["t1", "t1", "t2"]));

        var erased = await Build(handler).EraseSessionsAsync(["sess-1"], Ct);

        var query = handler.Calls.First(c => c.Method == HttpMethod.Get).Url;
        Assert.Contains("sessionId", Uri.UnescapeDataString(query));
        Assert.Contains("sess-1", Uri.UnescapeDataString(query));
        Assert.Equal(["t1", "t2"], DeletedTraceIds(handler).Order());
        Assert.Equal(2, erased);
    }

    [Fact]
    public async Task EraseSessions_FollowsCursorAcrossPages()
    {
        var handler = new ScriptedHandler(Page(["t1"], cursor: "next-page"), Page(["t2"]));

        var erased = await Build(handler).EraseSessionsAsync(["sess-1"], Ct);

        var observationCalls = handler.Calls.Where(c => c.Method == HttpMethod.Get).ToList();
        Assert.Equal(2, observationCalls.Count);
        Assert.Contains("cursor=next-page", observationCalls[1].Url);
        Assert.Equal(["t1", "t2"], DeletedTraceIds(handler).Order());
        Assert.Equal(2, erased);
    }

    [Fact]
    public async Task EraseSessions_AlsoDeletesTheSessionRatingScore()
    {
        var handler = new ScriptedHandler(Page([]));

        await Build(handler).EraseSessionsAsync(["sess-9"], Ct);

        Assert.Contains(handler.Calls, c => c.Method == HttpMethod.Delete
            && c.Url == "http://langfuse.test:3000/api/public/scores/" + Uri.EscapeDataString("customer_rating:sess-9"));
    }

    [Fact]
    public async Task EraseSessions_WithNoTraces_DeletesNoTraces_AndReturnsZero()
    {
        var handler = new ScriptedHandler(Page([]));

        var erased = await Build(handler).EraseSessionsAsync(["sess-1"], Ct);

        Assert.Empty(DeletedTraceIds(handler));
        Assert.Equal(0, erased);
    }

    [Fact]
    public async Task EraseSessions_BatchesLargeTraceLists()
    {
        var ids = Enumerable.Range(0, 250).Select(i => $"t{i}").ToArray();
        var handler = new ScriptedHandler(Page(ids));

        var erased = await Build(handler).EraseSessionsAsync(["sess-1"], Ct);

        var deleteCalls = handler.Calls.Count(c => c.Method == HttpMethod.Delete && c.Url.EndsWith("/api/public/traces"));
        Assert.Equal(3, deleteCalls);
        Assert.Equal(250, DeletedTraceIds(handler).Length);
        Assert.Equal(250, erased);
    }

    [Fact]
    public async Task EraseCustomer_FiltersObservationsByUserId()
    {
        var handler = new ScriptedHandler(Page(["t7"]));

        var erased = await Build(handler).EraseCustomerAsync("1027", Ct);

        var query = Uri.UnescapeDataString(handler.Calls.First(c => c.Method == HttpMethod.Get).Url);
        Assert.Contains("userId", query);
        Assert.Contains("1027", query);
        Assert.Equal(["t7"], DeletedTraceIds(handler));
        Assert.Equal(1, erased);
    }

    [Fact]
    public async Task EraseSessions_WhenLangfuseFails_Throws_SoPrivacyServiceKeepsSessionForRetry()
    {
        var handler = new ScriptedHandler { ObservationStatus = HttpStatusCode.InternalServerError };

        await Assert.ThrowsAsync<HttpRequestException>(() => Build(handler).EraseSessionsAsync(["sess-1"], Ct));
    }
}
