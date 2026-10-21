// Bot yanıtına 👍/👎: yalnızca müşterinin kendi oturumundaki GERÇEK bot mesajına verilebilir, neden kümesi kapalıdır,
// trace bulunabilirse bağlanır, gözlem aracına yazım en iyi gayrettir (hata geri bildirimi kaybettirmez).

using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Adapters.Redis;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound.Observability;
using CustomerSupportBot.Application.Services.Feedback;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute.ExceptionExtensions;

namespace CustomerSupportBot.Application.Tests;

public class MessageFeedbackServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string Answer = "Siparişiniz kargoda, yarın teslim edilir.";

    private sealed class Rig
    {
        public InMemoryMessageFeedbackStore Store { get; } = new();
        public InMemoryReasoningTraceStore Traces { get; } = new();
        public InMemorySessionManager Sessions { get; } =
            new(new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 10 })));
        public IConversationScoreSink Sink { get; } = Substitute.For<IConversationScoreSink>();
        public MessageFeedbackService Sut { get; }
        public string SessionId { get; private set; } = "";

        public Rig(bool withSink = true) =>
            Sut = new(Store, Sessions, Traces, NullLogger<MessageFeedbackService>.Instance, withSink ? Sink : null);

        public async Task SeedAsync(string user = "siparişim nerede", string bot = Answer)
        {
            var session = await Sessions.GetOrCreateAsync(null, Ct);
            SessionId = session.SessionId;
            await Sessions.AddExchangeAsync(SessionId, user, bot, ct: Ct);
        }

        public async Task<ReasoningTrace> SeedTraceAsync(string finalResponse)
        {
            var trace = await Traces.StartTraceAsync(SessionId, "q");
            await Traces.CompleteAsync(trace.TraceId, "completed", finalResponse);
            return trace;
        }
    }

    // ═══ kaydetme ═══

    [Fact]
    public async Task Downvote_WithReason_IsSaved_AndLinkedToTheTurnThatProducedTheAnswer()
    {
        var rig = new Rig();
        await rig.SeedAsync();
        var trace = await rig.SeedTraceAsync(Answer);

        var result = await rig.Sut.SubmitAsync(rig.SessionId, "1027", new(Answer, false, "wrong", "fiyat yanlış"), Ct);

        Assert.Equal(MessageFeedbackOutcome.Saved, result.Outcome);
        var saved = (await rig.Store.GetForSessionAsync(rig.SessionId, Ct)).Single();
        Assert.False(saved.Helpful);
        Assert.Equal("wrong", saved.Reason);
        Assert.Equal("fiyat yanlış", saved.Comment);
        Assert.Equal(trace.TraceId, saved.TraceId);
        Assert.Equal("1027", saved.CustomerId);
    }

    [Fact]
    public async Task Upvote_DropsAnyReasonOrComment()
    {
        var rig = new Rig();
        await rig.SeedAsync();

        await rig.Sut.SubmitAsync(rig.SessionId, "1027", new(Answer, true, "wrong", "x"), Ct);

        var saved = (await rig.Store.GetForSessionAsync(rig.SessionId, Ct)).Single();
        Assert.True(saved.Helpful);
        Assert.Null(saved.Reason);
        Assert.Null(saved.Comment);
    }

    [Fact]
    public async Task ChangingTheVote_UpdatesTheSameRow()
    {
        var rig = new Rig();
        await rig.SeedAsync();
        await rig.Sut.SubmitAsync(rig.SessionId, "1027", new(Answer, true, null, null), Ct);

        await rig.Sut.SubmitAsync(rig.SessionId, "1027", new(Answer, false, "unclear", null), Ct);

        var saved = (await rig.Store.GetForSessionAsync(rig.SessionId, Ct)).Single();
        Assert.False(saved.Helpful);
        Assert.Equal("unclear", saved.Reason);
    }

    [Fact]
    public async Task NullHelpful_ClearsTheVote_AndRemovesTheObservabilityScore()
    {
        var rig = new Rig();
        await rig.SeedAsync();
        await rig.Sut.SubmitAsync(rig.SessionId, "1027", new(Answer, true, null, null), Ct);

        var result = await rig.Sut.SubmitAsync(rig.SessionId, "1027", new(Answer, null, null, null), Ct);

        Assert.Equal(MessageFeedbackOutcome.Cleared, result.Outcome);
        Assert.Empty(await rig.Store.GetForSessionAsync(rig.SessionId, Ct));
        await rig.Sink.Received(1).RemoveMessageFeedbackAsync(rig.SessionId, MessageFeedback.HashOf(Answer), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithoutAMatchingTrace_StillSaves_WithNoTraceId()
    {
        var rig = new Rig();
        await rig.SeedAsync();

        await rig.Sut.SubmitAsync(rig.SessionId, "1027", new(Answer, false, null, null), Ct);

        Assert.Null((await rig.Store.GetForSessionAsync(rig.SessionId, Ct)).Single().TraceId);
    }

    // ═══ doğrulama ═══

    [Fact]
    public async Task TextThatIsNotABotMessageOfThisSession_IsRejected()
    {
        var rig = new Rig();
        await rig.SeedAsync();

        var invented = await rig.Sut.SubmitAsync(rig.SessionId, "1027", new("hiç söylenmemiş bir cümle", true, null, null), Ct);
        var userOwn = await rig.Sut.SubmitAsync(rig.SessionId, "1027", new("siparişim nerede", true, null, null), Ct);   // kullanıcının KENDİ mesajı

        Assert.Equal(MessageFeedbackOutcome.MessageNotFound, invented.Outcome);
        Assert.Equal(MessageFeedbackOutcome.MessageNotFound, userOwn.Outcome);
        Assert.Empty(await rig.Store.GetForSessionAsync(rig.SessionId, Ct));
    }

    [Fact]
    public async Task UnknownSession_IsMessageNotFound()
    {
        var result = await new Rig().Sut.SubmitAsync("yok", "1027", new(Answer, true, null, null), Ct);

        Assert.Equal(MessageFeedbackOutcome.MessageNotFound, result.Outcome);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BlankText_IsInvalid(string text)
    {
        var rig = new Rig();
        await rig.SeedAsync();

        Assert.Equal(MessageFeedbackOutcome.Invalid, (await rig.Sut.SubmitAsync(rig.SessionId, "1027", new(text, true, null, null), Ct)).Outcome);
    }

    [Fact]
    public async Task OversizedText_IsInvalid()
    {
        var rig = new Rig();
        await rig.SeedAsync();

        var result = await rig.Sut.SubmitAsync(rig.SessionId, "1027", new(new string('x', MessageFeedbackService.MaxTextLength + 1), true, null, null), Ct);

        Assert.Equal(MessageFeedbackOutcome.Invalid, result.Outcome);
    }

    [Fact]
    public async Task UnknownReason_IsInvalid()
    {
        var rig = new Rig();
        await rig.SeedAsync();

        var result = await rig.Sut.SubmitAsync(rig.SessionId, "1027", new(Answer, false, "<script>", null), Ct);

        Assert.Equal(MessageFeedbackOutcome.Invalid, result.Outcome);
    }

    [Fact]
    public async Task TooLongComment_IsInvalid()
    {
        var rig = new Rig();
        await rig.SeedAsync();

        var result = await rig.Sut.SubmitAsync(rig.SessionId, "1027",
            new(Answer, false, "other", new string('y', MessageFeedback.MaxCommentLength + 1)), Ct);

        Assert.Equal(MessageFeedbackOutcome.Invalid, result.Outcome);
    }

    // ═══ gözlem aracı ═══

    [Fact]
    public async Task PublishesTheFeedbackToTheObservabilityTool()
    {
        var rig = new Rig();
        await rig.SeedAsync();

        await rig.Sut.SubmitAsync(rig.SessionId, "1027", new(Answer, false, "wrong", null), Ct);

        await rig.Sink.Received(1).PublishMessageFeedbackAsync(
            Arg.Is<MessageFeedback>(f => f.SessionId == rig.SessionId && !f.Helpful && f.Reason == "wrong"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhenTheToolIsDown_TheFeedbackIsStillSaved()
    {
        var rig = new Rig();
        rig.Sink.PublishMessageFeedbackAsync(Arg.Any<MessageFeedback>(), Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException("down"));
        await rig.SeedAsync();

        var result = await rig.Sut.SubmitAsync(rig.SessionId, "1027", new(Answer, true, null, null), Ct);

        Assert.Equal(MessageFeedbackOutcome.Saved, result.Outcome);
        Assert.Single(await rig.Store.GetForSessionAsync(rig.SessionId, Ct));
    }

    [Fact]
    public async Task WithoutAnObservabilityTool_Works()
    {
        var rig = new Rig(withSink: false);
        await rig.SeedAsync();

        Assert.Equal(MessageFeedbackOutcome.Saved,
            (await rig.Sut.SubmitAsync(rig.SessionId, "1027", new(Answer, true, null, null), Ct)).Outcome);
    }

    // ═══ okuma ═══

    [Fact]
    public async Task GetForSession_ReturnsTheVotesKeyedByMessageHash()
    {
        var rig = new Rig();
        await rig.SeedAsync();
        await rig.Sut.SubmitAsync(rig.SessionId, "1027", new(Answer, false, "unclear", null), Ct);

        var votes = await rig.Sut.GetForSessionAsync(rig.SessionId, Ct);

        Assert.False(votes[MessageFeedback.HashOf(Answer)].Helpful);
    }

    [Fact]
    public async Task EachSavedVote_IsCounted_WithHelpfulAndReasonTags()
    {
        var seen = new List<(long Value, string? Helpful, string? Reason)>();
        using var listener = new System.Diagnostics.Metrics.MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == "chat.message.feedback") l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            string? helpful = null, reason = null;
            foreach (var t in tags) { if (t.Key == "helpful") helpful = t.Value as string; if (t.Key == "reason") reason = t.Value as string; }
            lock (seen) seen.Add((value, helpful, reason));
        });
        listener.Start();
        var rig = new Rig();
        await rig.SeedAsync();

        await rig.Sut.SubmitAsync(rig.SessionId, "1027", new(Answer, false, "wrong", null), Ct);

        Assert.Contains((1L, "false", "wrong"), seen);
    }
}
