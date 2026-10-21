// Müşteri puanı Langfuse'a oturum bazlı skor olarak gider. Burada kilitlenen sözleşme: uç nokta, Basic Auth,
// gövde alanları ve idempotent skor kimliği (aynı oturumun yeniden puanlanması skoru günceller, çoğaltmaz).

using System.Net;
using System.Text;
using System.Text.Json;
using CustomerSupportBot.Adapters.Telemetry.Langfuse;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomerSupportBot.Adapters.Telemetry.Tests;

public class LangfuseScoreSinkTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class CapturingHandler(HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new StringContent("{}") };
        }
    }

    private static LangfuseScoreSink Build(CapturingHandler handler, string baseUrl = "http://langfuse.test:3000/") =>
        new(new HttpClient(handler),
            new LangfuseOptions { Enabled = true, BaseUrl = baseUrl, PublicKey = "pk-lf-1", SecretKey = "sk-lf-1" },
            NullLogger<LangfuseScoreSink>.Instance);

    private static ConversationRating Rating(int stars = 4, string? feedback = "hızlıydı") =>
        new() { SessionId = "sess-42", Stars = stars, Feedback = feedback };

    [Fact]
    public async Task PublishRating_PostsSessionScore_WithBasicAuth()
    {
        var handler = new CapturingHandler();

        await Build(handler).PublishRatingAsync(Rating(), Ct);

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("http://langfuse.test:3000/api/public/scores", handler.Request.RequestUri!.ToString());
        var expectedAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes("pk-lf-1:sk-lf-1"));
        Assert.Equal("Basic", handler.Request.Headers.Authorization!.Scheme);
        Assert.Equal(expectedAuth, handler.Request.Headers.Authorization.Parameter);

        using var json = JsonDocument.Parse(handler.Body!);
        var root = json.RootElement;
        Assert.Equal("sess-42", root.GetProperty("sessionId").GetString());
        Assert.Equal("customer_rating", root.GetProperty("name").GetString());
        Assert.Equal(4, root.GetProperty("value").GetDouble());
        Assert.Equal("NUMERIC", root.GetProperty("dataType").GetString());
        Assert.Equal("hızlıydı", root.GetProperty("comment").GetString());
    }

    [Fact]
    public async Task PublishRating_UsesStableIdPerSession_SoReRatingUpdatesInsteadOfDuplicating()
    {
        var first = new CapturingHandler();
        var second = new CapturingHandler();

        await Build(first).PublishRatingAsync(Rating(2), Ct);
        await Build(second).PublishRatingAsync(Rating(5), Ct);

        string IdOf(CapturingHandler h) => JsonDocument.Parse(h.Body!).RootElement.GetProperty("id").GetString()!;
        Assert.Equal(IdOf(first), IdOf(second));
        Assert.Contains("sess-42", IdOf(first));
    }

    // Langfuse aynı kimlikli skoru BİRLEŞTİRİR: alan atlanırsa önceki yorum kalır. Yorumsuz yeniden puanlamada
    // eski yorum yeni puanın yanında kalmasın diye alan açıkça null gönderilir (gerçek Langfuse'ta doğrulandı).
    [Fact]
    public async Task PublishRating_WithoutFeedback_SendsExplicitNullComment_SoStaleCommentIsCleared()
    {
        var handler = new CapturingHandler();

        await Build(handler).PublishRatingAsync(Rating(feedback: null), Ct);

        var root = JsonDocument.Parse(handler.Body!).RootElement;
        Assert.True(root.TryGetProperty("comment", out var comment));
        Assert.Equal(JsonValueKind.Null, comment.ValueKind);
    }

    [Fact]
    public async Task PublishRating_ServerError_DoesNotThrow()
    {
        var handler = new CapturingHandler(HttpStatusCode.InternalServerError);

        await Build(handler).PublishRatingAsync(Rating(), Ct);

        Assert.NotNull(handler.Request);
    }
}
