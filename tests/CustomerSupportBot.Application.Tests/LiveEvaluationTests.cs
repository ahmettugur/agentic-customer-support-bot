// Canlı değerlendirme: üretim trafiğinden örneklenen yanıtlar LLM-hakemle puanlanır; puanlar prompt seti sürümüyle
// etiketli metrik olur — prompt değişikliklerinin gerçek etkisi sürümler arası karşılaştırılabilir.

using System.Diagnostics.Metrics;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Services.Evaluation;
using CustomerSupportBot.Application.Services.Memory;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Tests;

public class LiveEvaluationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ── Saf: ayrıştırma ve örnekleme ───────────────────────────────────────────

    [Fact]
    public void ParsesAWellFormedVerdict()
    {
        var s = JudgeVerdictParser.Parse("""{"helpfulness":4,"groundedness":5,"tone":3,"issue":"yok"}""");

        s.Should().BeEquivalentTo(new JudgeScores(4, 5, 3, "yok"));
    }

    [Fact]
    public void ToleratesCodeFencesAndSurroundingText()
    {
        var s = JudgeVerdictParser.Parse("İşte:\n```json\n{\"helpfulness\":2,\"groundedness\":2,\"tone\":5}\n```");

        s!.Helpfulness.Should().Be(2);
    }

    [Theory]
    [InlineData("""{"helpfulness":9,"groundedness":0,"tone":3}""", 5, 1)]
    [InlineData("""{"helpfulness":-4,"groundedness":7,"tone":3}""", 1, 5)]
    public void ClampsScoresIntoOneToFive(string json, int helpfulness, int groundedness)
    {
        var s = JudgeVerdictParser.Parse(json)!;

        s.Helpfulness.Should().Be(helpfulness);
        s.Groundedness.Should().Be(groundedness);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"helpfulness":"iyi"}""")]
    [InlineData("""{"tone":3}""")]
    public void AnUnusableVerdict_IsRejected(string raw) => JudgeVerdictParser.Parse(raw).Should().BeNull();

    [Fact]
    public void TheIssueText_IsTruncated() =>
        JudgeVerdictParser.Parse($$"""{"helpfulness":3,"groundedness":3,"tone":3,"issue":"{{new string('x', 900)}}"}""")!
            .Issue!.Length.Should().BeLessThanOrEqualTo(JudgeVerdictParser.MaxIssueLength);

    [Fact]
    public void Sampling_IsDeterministicPerKey_AndRoughlyProportional()
    {
        LiveEvaluationSampler.ShouldSample(0, "k").Should().BeFalse();
        LiveEvaluationSampler.ShouldSample(1, "k").Should().BeTrue();
        LiveEvaluationSampler.ShouldSample(0.3, "k").Should().Be(LiveEvaluationSampler.ShouldSample(0.3, "k"));

        var hits = Enumerable.Range(0, 5000).Count(i => LiveEvaluationSampler.ShouldSample(0.2, $"s-{i}:1"));
        hits.Should().BeInRange(800, 1200);
    }

    // ── Servis ─────────────────────────────────────────────────────────────────

    private sealed class Judge(string reply) : IGeneralChatClient
    {
        public List<IReadOnlyList<ConversationMessage>> Calls { get; } = [];
        public Task<string> CompleteAsync(IReadOnlyList<ConversationMessage> messages, CancellationToken ct = default)
        {
            Calls.Add(messages);
            return reply == "throw" ? throw new InvalidOperationException("llm") : Task.FromResult(reply);
        }
    }

    private static (LiveEvaluationService Svc, Judge Llm) Build(string reply, Action<LiveEvaluationOptions>? configure = null)
    {
        var llm = new Judge(reply);
        var prompts = Substitute.For<IPromptRepository>();
        prompts.Get(LiveEvaluationService.PromptKey).Returns("Sen bir hakemsin.");
        prompts.SetVersion.Returns("ps-abc123");
        var o = new LiveEvaluationOptions { Enabled = true, SampleRate = 1 };
        configure?.Invoke(o);
        return (new LiveEvaluationService(llm, prompts, new ContextSanitizer(), Options.Create(o),
            NullLogger<LiveEvaluationService>.Instance), llm);
    }

    private const string Verdict = """{"helpfulness":4,"groundedness":5,"tone":3}""";

    [Fact]
    public async Task ASampledTurn_IsScored_AndTheAnswerGoesToTheJudgeAsData()
    {
        var (svc, llm) = Build(Verdict);

        var s = await svc.EvaluateAsync("s1", 3, "siparişim nerede", "Kargoda.", Ct);

        s.Should().NotBeNull();
        var user = llm.Calls.Single().Last(m => m.Role == ConversationRoles.User).Text;
        user.Should().Contain("<retrieved_data").And.Contain("Kargoda.").And.Contain("siparişim nerede");
    }

    [Fact]
    public async Task WhenDisabledOrNotSampled_TheJudgeIsNeverCalled()
    {
        var (off, llmOff) = Build(Verdict, o => o.Enabled = false);
        var (zero, llmZero) = Build(Verdict, o => o.SampleRate = 0);

        (await off.EvaluateAsync("s1", 1, "q", "a", Ct)).Should().BeNull();
        (await zero.EvaluateAsync("s1", 1, "q", "a", Ct)).Should().BeNull();

        llmOff.Calls.Should().BeEmpty();
        llmZero.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData("throw")]
    [InlineData("saçma")]
    public async Task AJudgeFailureOrGarbage_ReturnsNull_WithoutThrowing(string reply)
    {
        var (svc, _) = Build(reply);

        (await svc.EvaluateAsync("s1", 1, "q", "a", Ct)).Should().BeNull();
    }

    [Fact]
    public async Task EmptyAnswers_AreNotJudged()
    {
        var (svc, llm) = Build(Verdict);

        (await svc.EvaluateAsync("s1", 1, "q", "  ", Ct)).Should().BeNull();
        llm.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task EachDimension_IsRecordedWithThePromptSetVersion()
    {
        var seen = new List<(double Value, string Dimension, string PromptSet)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (i, l) => { if (i.Name == "chat.live_eval.score") l.EnableMeasurementEvents(i); };
        listener.SetMeasurementEventCallback<double>((_, v, tags, _) =>
        {
            string? d = null, p = null;
            foreach (var t in tags) { if (t.Key == "dimension") d = t.Value as string; if (t.Key == "prompt_set") p = t.Value as string; }
            lock (seen) seen.Add((v, d!, p!));
        });
        listener.Start();
        var (svc, _) = Build(Verdict);

        await svc.EvaluateAsync("s1", 1, "q", "a", Ct);

        seen.Should().Contain([(4d, "helpfulness", "ps-abc123"), (5d, "groundedness", "ps-abc123"), (3d, "tone", "ps-abc123")]);
    }
}
