// Oturum bazlı A/B: her oturum bir deneyin tek koluna atanır (sabit, ağırlıklı); kolun operatör metni bağlama eklenir ve
// canlı değerlendirme puanları deney/kol etiketiyle ayrıştırılır.

using System.Diagnostics.Metrics;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Services.Evaluation;
using CustomerSupportBot.Application.Services.Memory;
using CustomerSupportBot.Application.Services.Providers;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SessionState = CustomerSupportBot.Domain.Model.SessionState;

namespace CustomerSupportBot.Application.Tests;

public class ExperimentTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ExperimentDefinition Tone(double a = 1, double b = 1) => new()
    {
        Name = "tone",
        Arms = [new ExperimentArm { Name = "A", Weight = a }, new ExperimentArm { Name = "B", Weight = b, Hint = "Yanıtları iki cümleyi geçmeyecek kadar kısa tut." }]
    };

    [Fact]
    public void TheSameSession_AlwaysGetsTheSameArm() =>
        ExperimentAssigner.Assign("s-1", Tone())!.Name.Should().Be(ExperimentAssigner.Assign("s-1", Tone())!.Name);

    [Fact]
    public void AssignmentFollowsTheWeights()
    {
        var arms = Enumerable.Range(0, 4000).Select(i => ExperimentAssigner.Assign($"s-{i}", Tone(a: 3, b: 1))!.Name).ToList();

        arms.Count(a => a == "A").Should().BeInRange(2800, 3200);
    }

    [Fact]
    public void DifferentExperiments_AssignIndependently()
    {
        var other = Tone(); other.Name = "greeting";
        var differing = Enumerable.Range(0, 400).Count(i =>
            ExperimentAssigner.Assign($"s-{i}", Tone())!.Name != ExperimentAssigner.Assign($"s-{i}", other)!.Name);

        differing.Should().BeGreaterThan(100, "aynı oturum her deneyde aynı kola düşmemeli");
    }

    [Fact]
    public void ZeroWeightArms_AreNeverAssigned_AndAnEmptyExperimentAssignsNothing()
    {
        Enumerable.Range(0, 200).Select(i => ExperimentAssigner.Assign($"s-{i}", Tone(a: 0, b: 1))!.Name).Should().OnlyContain(n => n == "B");
        ExperimentAssigner.Assign("s", new ExperimentDefinition { Name = "x", Arms = [] }).Should().BeNull();
        ExperimentAssigner.Assign("s", new ExperimentDefinition { Name = "x", Arms = [new ExperimentArm { Name = "A", Weight = 0 }] }).Should().BeNull();
    }

    // ── Bağlam sağlayıcısı ─────────────────────────────────────────────────────

    private static ExperimentContextProvider Provider(bool enabled, params ExperimentDefinition[] defs) =>
        new(Options.Create(new ExperimentOptions { Enabled = enabled, Experiments = [.. defs] }));

    private static AgentSession Session(string id) => new() { SessionId = id, State = new SessionState() };

    [Fact]
    public async Task TheAssignedArmsHint_IsAddedToTheContext()
    {
        var sid = Enumerable.Range(0, 100).Select(i => $"s-{i}").First(s => ExperimentAssigner.Assign(s, Tone())!.Name == "B");

        var ctx = await Provider(true, Tone()).GetContextAsync(Session(sid), "q", Ct);

        ctx.Should().Contain("iki cümleyi geçmeyecek");
    }

    [Fact]
    public async Task AnArmWithoutAHint_AddsNothing()
    {
        var sid = Enumerable.Range(0, 100).Select(i => $"s-{i}").First(s => ExperimentAssigner.Assign(s, Tone())!.Name == "A");

        (await Provider(true, Tone()).GetContextAsync(Session(sid), "q", Ct)).Should().BeNull();
    }

    [Fact]
    public async Task WhenDisabled_NothingIsAdded() =>
        (await Provider(false, Tone()).GetContextAsync(Session("s-1"), "q", Ct)).Should().BeNull();

    [Fact]
    public async Task TheHint_IsNotUserControlled_AndNeverEchoesTheQuery()
    {
        var sid = Enumerable.Range(0, 100).Select(i => $"s-{i}").First(s => ExperimentAssigner.Assign(s, Tone())!.Name == "B");

        var ctx = await Provider(true, Tone()).GetContextAsync(Session(sid), "KULLANICI METNİ", Ct);

        ctx.Should().NotContain("KULLANICI METNİ");
    }

    // ── Canlı değerlendirme etiketi ────────────────────────────────────────────

    private sealed class Judge : IGeneralChatClient
    {
        public Task<string> CompleteAsync(IReadOnlyList<ConversationMessage> messages, CancellationToken ct = default) =>
            Task.FromResult("""{"helpfulness":4,"groundedness":5,"tone":3}""");
    }

    [Fact]
    public async Task LiveEvaluationScores_AreTaggedWithTheExperimentAndArm()
    {
        var seen = new List<(string Experiment, string Arm, string Dimension)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (i, l) => { if (i.Name == "chat.live_eval.experiment_score") l.EnableMeasurementEvents(i); };
        listener.SetMeasurementEventCallback<double>((_, _, tags, _) =>
        {
            string? e = null, a = null, d = null;
            foreach (var t in tags) { if (t.Key == "experiment") e = t.Value as string; if (t.Key == "arm") a = t.Value as string; if (t.Key == "dimension") d = t.Value as string; }
            lock (seen) seen.Add((e!, a!, d!));
        });
        listener.Start();

        var prompts = Substitute.For<IPromptRepository>();
        prompts.Get(LiveEvaluationService.PromptKey).Returns("hakem");
        prompts.SetVersion.Returns("ps-1");
        var svc = new LiveEvaluationService(new Judge(), prompts, new ContextSanitizer(),
            Options.Create(new LiveEvaluationOptions { Enabled = true, SampleRate = 1 }), NullLogger<LiveEvaluationService>.Instance,
            Options.Create(new ExperimentOptions { Enabled = true, Experiments = [Tone()] }));

        await svc.EvaluateAsync("s-77", 1, "q", "a", Ct);

        var arm = ExperimentAssigner.Assign("s-77", Tone())!.Name;
        seen.Should().Contain(("tone", arm, "helpfulness"));
        seen.Should().HaveCount(3);
    }
}
