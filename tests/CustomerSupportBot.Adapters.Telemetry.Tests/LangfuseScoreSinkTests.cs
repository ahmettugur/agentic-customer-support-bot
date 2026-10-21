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

    // ═══ mesaj başına geri bildirim ═══

    private static MessageFeedback Vote(bool helpful, string? reason = null, string? comment = null) => new()
    {
        SessionId = "sess-42", MessageHash = "abc123def4567890", Helpful = helpful, Reason = reason, Comment = comment
    };

    [Fact]
    public async Task PublishMessageFeedback_PostsABooleanSessionScore_WithAStablePerMessageId()
    {
        var handler = new CapturingHandler();

        await Build(handler).PublishMessageFeedbackAsync(Vote(helpful: false, reason: "wrong"), Ct);

        var root = JsonDocument.Parse(handler.Body!).RootElement;
        Assert.Equal("message_feedback:sess-42:abc123def4567890", root.GetProperty("id").GetString());
        Assert.Equal("sess-42", root.GetProperty("sessionId").GetString());
        Assert.Equal("message_feedback", root.GetProperty("name").GetString());
        Assert.Equal("BOOLEAN", root.GetProperty("dataType").GetString());
        Assert.Equal(0, root.GetProperty("value").GetInt32());
        Assert.Equal("wrong", root.GetProperty("comment").GetString());
    }

    [Fact]
    public async Task PublishMessageFeedback_NeverSendsTheCustomersFreeText()
    {
        var handler = new CapturingHandler();

        await Build(handler).PublishMessageFeedbackAsync(Vote(false, "other", "adım Ali, telefonum 0532 000 00 00"), Ct);

        Assert.DoesNotContain("0532", handler.Body);
        Assert.DoesNotContain("Ali", handler.Body);
    }

    [Fact]
    public async Task PublishMessageFeedback_Upvote_SendsValueOne_AndAClearedComment()
    {
        var handler = new CapturingHandler();

        await Build(handler).PublishMessageFeedbackAsync(Vote(helpful: true), Ct);

        var root = JsonDocument.Parse(handler.Body!).RootElement;
        Assert.Equal(1, root.GetProperty("value").GetInt32());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("comment").ValueKind);
    }

    [Fact]
    public async Task RemoveMessageFeedback_DeletesTheScoreById()
    {
        var handler = new CapturingHandler();

        await Build(handler).RemoveMessageFeedbackAsync("sess-42", "abc123def4567890", Ct);

        Assert.Equal(HttpMethod.Delete, handler.Request!.Method);
        Assert.EndsWith("/api/public/scores/" + Uri.EscapeDataString("message_feedback:sess-42:abc123def4567890"),
            handler.Request.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task RemoveMessageFeedback_ServerError_Throws_SoKvkkErasureIsNotReportedAsDone()
    {
        var handler = new CapturingHandler(HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<HttpRequestException>(() => Build(handler).RemoveMessageFeedbackAsync("s", "h", Ct));
    }

    [Fact]
    public async Task RemoveMessageFeedback_NotFound_IsFine()
    {
        await Build(new CapturingHandler(HttpStatusCode.NotFound)).RemoveMessageFeedbackAsync("s", "h", Ct);
    }
}
