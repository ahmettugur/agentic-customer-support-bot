// Tests/Evaluation/EvaluationRunnerRepetitionsAndQualityTests.cs
// K3 Faz 2B: numRepetitions (non-determinism ölçümü) ve MEAI quality check (relevance/coherence)
// entegrasyonu. Tam RunScenarioAsync akışı (reasoning + gerçek GroupChat workflow) mock IChatClient
// ile hang riski taşıdığından (bkz. ApprovalGateServiceToolBuilderTests.cs başındaki not),
// bu testler yalnızca saf/izole edilebilir parçaları (AggregateRepetitions, RunQualityCheckAsync)
// hedefliyor — ikisi de workflow'a hiç dokunmuyor.

using CustomerSupportBot.Adapters.Agents.Evaluation;
using CustomerSupportBot.Application.Ports.Outbound;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using ChatResponse = Microsoft.Extensions.AI.ChatResponse;

namespace CustomerSupportBot.Api.Tests.Evaluation;

public class EvaluationRunnerRepetitionsAndQualityTests
{
    // ─── AggregateRepetitions — saf, deterministik, LLM/I-O gerektirmez ───

    [Fact]
    public void AggregateRepetitions_AllPassed_PassRateIsOne()
    {
        var runs = new List<ScenarioResult>
        {
            new() { TotalCriteria = 1, PassedCriteria = 1 },
            new() { TotalCriteria = 1, PassedCriteria = 1 },
            new() { TotalCriteria = 1, PassedCriteria = 1 }
        };

        var result = EvaluationRunner.AggregateRepetitions(runs);

        result.Repetitions.Should().Be(3);
        result.RepetitionOutcomes.Should().Equal(true, true, true);
        result.RepetitionPassRate.Should().Be(1.0);
    }

    [Fact]
    public void AggregateRepetitions_MixedOutcomes_PassRateReflectsRatio()
    {
        var runs = new List<ScenarioResult>
        {
            new() { TotalCriteria = 1, PassedCriteria = 1 }, // pass
            new() { TotalCriteria = 1, PassedCriteria = 0 }, // fail
            new() { TotalCriteria = 1, PassedCriteria = 1 }, // pass
            new() { TotalCriteria = 1, PassedCriteria = 0 }  // fail
        };

        var result = EvaluationRunner.AggregateRepetitions(runs);

        result.Repetitions.Should().Be(4);
        result.RepetitionOutcomes.Should().Equal(true, false, true, false);
        result.RepetitionPassRate.Should().Be(0.5);
    }

    [Fact]
    public void AggregateRepetitions_PrimaryFieldsComeFromFirstRun()
    {
        var runs = new List<ScenarioResult>
        {
            new() { Response = "ilk koşu yanıtı", TotalCriteria = 1, PassedCriteria = 1 },
            new() { Response = "ikinci koşu yanıtı", TotalCriteria = 1, PassedCriteria = 0 }
        };

        var result = EvaluationRunner.AggregateRepetitions(runs);

        result.Response.Should().Be("ilk koşu yanıtı");
    }

    // ─── EvaluationScenario / ScenarioResult yeni alan varsayılanları ───

    [Fact]
    public void EvaluationScenario_Repetitions_DefaultsToOne()
    {
        new EvaluationScenario().Repetitions.Should().Be(1);
    }

    [Fact]
    public void EvaluationScenario_QualityChecks_DefaultsToEmpty()
    {
        new EvaluationScenario().QualityChecks.Should().BeEmpty();
    }

    [Fact]
    public void ScenarioResult_Repetitions_DefaultsToOneAndOutcomesNull()
    {
        var r = new ScenarioResult();
        r.Repetitions.Should().Be(1);
        r.RepetitionOutcomes.Should().BeNull();
        r.RepetitionPassRate.Should().BeNull();
    }

    [Fact]
    public void EvaluationQualityOptions_DefaultsToDisabled()
    {
        new EvaluationQualityOptions().Enabled.Should().BeFalse();
    }

    // ─── RunQualityCheckAsync — workflow'a hiç dokunmaz, yalnızca _chatClient'i çağırır ───

    private EvaluationRunner BuildRunnerWithChatClient(IChatClient chatClient, bool qualityEnabled)
    {
        // team/reasoningService/sessionManager/traceStore bu testlerde hiç invoke edilmiyor —
        // RunQualityCheckAsync bunlara dokunmuyor, sadece constructor'ın dolu olması gerekiyor.
        var team = Substitute.For<CustomerSupportBot.Application.Ports.Outbound.IAgentTeamPort>();
        var reasoningService = Substitute.For<CustomerSupportBot.Application.Ports.Inbound.IReasoningPort>();
        var sessionManager = Substitute.For<CustomerSupportBot.Application.Ports.Outbound.Persistence.ISessionManager>();
        var traceStore = Substitute.For<CustomerSupportBot.Application.Ports.Outbound.Observability.IReasoningTraceStore>();

        return new EvaluationRunner(
            team, reasoningService, sessionManager, traceStore,
            chatClient, Options.Create(new EvaluationQualityOptions { Enabled = qualityEnabled }));
    }

    [Fact]
    public async Task RunQualityCheckAsync_GloballyDisabled_SkipsWithoutCallingChatClient()
    {
        var chatClient = Substitute.For<IChatClient>();
        var runner = BuildRunnerWithChatClient(chatClient, qualityEnabled: false);

        var result = await runner.RunQualityCheckAsync(
            "relevance", "sorgu", "yanıt", TestContext.Current.CancellationToken);

        result.Skipped.Should().Be("quality_checks_disabled");
        result.Passed.Should().BeFalse();
        await chatClient.DidNotReceiveWithAnyArgs()
            .GetResponseAsync(default!, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RunQualityCheckAsync_Enabled_HighScore_Passes()
    {
        var chatClient = Substitute.For<IChatClient>();
        chatClient
            .GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                "<S0>Let's think step by step: yanıt soruyu tam karşılıyor</S0><S1>tam ve doğru</S1><S2>5</S2>"))));

        var runner = BuildRunnerWithChatClient(chatClient, qualityEnabled: true);

        var result = await runner.RunQualityCheckAsync(
            "relevance", "Dizüstü bilgisayarınız var mı?", "Evet, Dell XPS 15 mevcut.",
            TestContext.Current.CancellationToken);

        result.Skipped.Should().BeNull();
        result.Passed.Should().BeTrue();
        result.Criterion.Should().Be("quality_relevance");
    }

    [Fact]
    public async Task RunQualityCheckAsync_Enabled_LowScore_Fails()
    {
        var chatClient = Substitute.For<IChatClient>();
        chatClient
            .GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                "<S0>Let's think step by step: yanıt konuyla ilgisiz</S0><S1>alakasız</S1><S2>1</S2>"))));

        var runner = BuildRunnerWithChatClient(chatClient, qualityEnabled: true);

        var result = await runner.RunQualityCheckAsync(
            "relevance", "Dizüstü bilgisayarınız var mı?", "Hava bugün güzel.",
            TestContext.Current.CancellationToken);

        result.Skipped.Should().BeNull();
        result.Passed.Should().BeFalse();
    }

    [Fact]
    public async Task RunQualityCheckAsync_Coherence_UsesCoherenceEvaluator()
    {
        var chatClient = Substitute.For<IChatClient>();
        chatClient
            .GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                "<S0>düzgün akış</S0><S1>tutarlı</S1><S2>4</S2>"))));

        var runner = BuildRunnerWithChatClient(chatClient, qualityEnabled: true);

        var result = await runner.RunQualityCheckAsync(
            "coherence", "soru", "tutarlı bir yanıt", TestContext.Current.CancellationToken);

        result.Criterion.Should().Be("quality_coherence");
        result.Passed.Should().BeTrue();
    }

    [Fact]
    public async Task RunQualityCheckAsync_UnknownCheckName_Throws()
    {
        var chatClient = Substitute.For<IChatClient>();
        var runner = BuildRunnerWithChatClient(chatClient, qualityEnabled: true);

        var act = () => runner.RunQualityCheckAsync(
            "not_a_real_check", "soru", "yanıt", TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }
}
