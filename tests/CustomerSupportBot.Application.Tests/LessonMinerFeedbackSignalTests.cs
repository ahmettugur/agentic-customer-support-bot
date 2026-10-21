// Müşterinin bir yanıta verdiği 👎, o turu ders çıkarma adayı yapar (yıldız puanı vermese bile) ve LLM'e nedeniyle
// birlikte gösterilir. 👍 ve başka turların oyları adaylığı DEĞİŞTİRMEZ.

using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Ports.Outbound.Observability;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Improvement;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Tests;

public class LessonMinerFeedbackSignalTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Rig
    {
        public InMemoryMessageFeedbackStore Feedback { get; } = new();
        public IReasoningTraceStore Traces { get; } = Substitute.For<IReasoningTraceStore>();
        public IGeneralChatClient Chat { get; } = Substitute.For<IGeneralChatClient>();
        public List<ConversationMessage>? Prompt { get; private set; }
        public LessonMiner Miner { get; }

        public Rig(bool withFeedbackStore = true)
        {
            Chat.CompleteAsync(Arg.Any<List<ConversationMessage>>(), Arg.Any<CancellationToken>())
                .Returns(call => { Prompt = call.Arg<List<ConversationMessage>>(); return Task.FromResult("{\"lessons\":[]}"); });
            Miner = new LessonMiner(Traces, Substitute.For<IRatingStore>(), Substitute.For<ILessonStore>(), Chat,
                Options.Create(new SelfImprovementOptions { Enabled = true, RecentTracesToScan = 50 }),
                NullLogger<LessonMiner>.Instance, memory: null, feedback: withFeedbackStore ? Feedback : null);
        }

        // Aday olmaması için temiz bir tur: hata yok, sorun yok.
        public ReasoningTrace CleanTrace(string id)
        {
            var t = new ReasoningTrace { TraceId = id, SessionId = "s-" + id, UserQuery = "soru", FinalResponse = "yanıt", TerminationReason = "completed" };
            Traces.GetRecent(Arg.Any<int>()).Returns(call => Existing.ToList());
            Existing.Add(t);
            return t;
        }

        public List<ReasoningTrace> Existing { get; } = [];

        public Task Vote(string trace, bool helpful, string? reason = null, string? comment = null) =>
            Feedback.UpsertAsync(new MessageFeedback
            {
                SessionId = "s-" + trace, MessageHash = MessageFeedback.HashOf("yanıt " + trace), Helpful = helpful, Reason = reason,
                Comment = comment, TraceId = trace
            }, Ct);
    }

    [Fact]
    public async Task ACleanTurn_IsNotACandidate_WithoutFeedback()
    {
        var rig = new Rig();
        rig.CleanTrace("t1");

        var report = await rig.Miner.MineAsync(Ct);

        report.Candidates.Should().Be(0);
    }

    [Fact]
    public async Task ADownvotedTurn_BecomesACandidate_AndTheReasonReachesThePrompt()
    {
        var rig = new Rig();
        rig.CleanTrace("t1");
        await rig.Vote("t1", helpful: false, reason: "wrong", comment: "fiyatı yanlış söyledi");

        var report = await rig.Miner.MineAsync(Ct);

        report.Candidates.Should().Be(1);
        var prompt = string.Join("\n", rig.Prompt!.Select(m => m.Text));
        prompt.Should().Contain("👎").And.Contain("wrong").And.Contain("fiyatı yanlış söyledi");
    }

    [Fact]
    public async Task AnUpvote_DoesNotMakeATurnACandidate()
    {
        var rig = new Rig();
        rig.CleanTrace("t1");
        await rig.Vote("t1", helpful: true);

        (await rig.Miner.MineAsync(Ct)).Candidates.Should().Be(0);
    }

    [Fact]
    public async Task ADownvoteOnAnotherTurn_DoesNotAffectThisOne()
    {
        var rig = new Rig();
        rig.CleanTrace("t1");
        await rig.Vote("baska-tur", helpful: false, reason: "wrong");

        (await rig.Miner.MineAsync(Ct)).Candidates.Should().Be(0);
    }

    [Fact]
    public async Task WithoutAFeedbackStore_MiningStillWorks()
    {
        var rig = new Rig(withFeedbackStore: false);
        rig.CleanTrace("t1");

        (await rig.Miner.MineAsync(Ct)).Candidates.Should().Be(0);
    }
}
