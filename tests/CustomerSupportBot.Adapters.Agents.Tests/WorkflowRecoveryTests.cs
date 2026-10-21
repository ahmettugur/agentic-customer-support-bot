// Çökme sonrası devam — GERÇEK MAF workflow'u, betikli LLM, bellek içi kalıcı depo.
//
// "Süreç ölümü" iki yoldan simüle edilir: (a) host kapanırken (deploy) tur iptal edilir ve checkpoint'ler KALIR,
// (b) istemci vazgeçince (durdur düğmesi) checkpoint'ler SİLİNİR — kullanıcının iptal ettiği tur asla diriltilmemeli.
// "Yeni süreç" = sıfırdan kurulmuş takım (yeni ajanlar, yeni workflow), aynı depolar.

using CustomerSupportBot.Adapters.Agents.Checkpointing;
using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Adapters.Redis;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Locking;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Chat;
using CustomerSupportBot.Application.Services.Providers;
using CustomerSupportBot.Tests.Shared;
using CustomerSupportBot.Application.Services.Tools;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using AgentSession = CustomerSupportBot.Domain.Model.AgentSession;

namespace CustomerSupportBot.Adapters.Agents.Tests;

public class WorkflowRecoveryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class FakeShutdown : IShutdownSignal
    {
        public bool IsStopping { get; set; }
    }

    private sealed class Rig
    {
        public InMemoryWorkflowCheckpointRepository Repo { get; } = new();
        public InMemoryReasoningTraceStore Traces { get; } = new();
        public FakeShutdown Shutdown { get; } = new();
        public InMemorySessionManager Sessions { get; } =
            new(new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 10 })));
        public WorkflowCheckpointingOptions CheckpointOptions { get; } = new() { Enabled = true, GraceSeconds = 60, MaxAgeMinutes = 30 };
        public WorkflowCheckpointing Checkpointing { get; }

        public Rig() => Checkpointing = new(Repo, Microsoft.Extensions.Options.Options.Create(CheckpointOptions),
            NullLogger<WorkflowCheckpointing>.Instance, Shutdown);

        /// <summary>Sıfırdan kurulmuş bir takım = "yeni süreç".</summary>
        public CustomerSupportTeam NewTeam(CheckpointTestRig.ScriptedLlm llm, bool checkpointing = true)
        {
            var (tools, gate, ctx, prompts, emitter) = CheckpointTestRig.Dependencies();
            return new CustomerSupportTeam(llm,
                new ContextPipeline([], Microsoft.Extensions.Options.Options.Create(new ContextPipelineOptions()), NullLogger<ContextPipeline>.Instance),
                Microsoft.Extensions.Options.Options.Create(new WorkflowGuardOptions { MaxIterations = 10, TimeoutSeconds = 30 }),
                Microsoft.Extensions.Options.Options.Create(new ParallelExecutionOptions { Enabled = true }),
                Traces, prompts, gate, tools, emitter, ctx, NullLoggerFactory.Instance,
                new CustomerIdentityHintBuilder(Substitute.For<ICustomerRepository>()),
                checkpointing: checkpointing ? Checkpointing : null);
        }

        public WorkflowRecoveryService Recovery(CustomerSupportTeam team, IAppDistributedLock? distributedLock = null) =>
            new(Repo, Checkpointing, team, Sessions,
                Microsoft.Extensions.Options.Options.Create(CheckpointOptions),
                Microsoft.Extensions.Options.Options.Create(new WorkflowGuardOptions { TimeoutSeconds = 30 }),
                NullLogger<WorkflowRecoveryService>.Instance, distributedLock);

        public async Task<AgentSession> SessionAsync(string id = "s1") => await Sessions.GetOrCreateAsync(id, Ct);

        /// <summary>Turu 3. LLM çağrısında keser (ajanlar zaten checkpoint yazmış olur).</summary>
        public async Task<string> CrashedTurnAsync(string query = "kategoriler neler", bool hostStopping = true)
        {
            var session = await SessionAsync();
            var history = await Sessions.GetHistoryAsync(session.SessionId, Ct);
            using var cts = new CancellationTokenSource();
            var llm = new CheckpointTestRig.ScriptedLlm(n => { if (n == 3) { Shutdown.IsStopping = hostStopping; cts.Cancel(); } });
            try { await NewTeam(llm).RunAsync(query, history, session, null, cts.Token); }
            catch (OperationCanceledException) { }
            Shutdown.IsStopping = false;
            return Traces.GetBySession(session.SessionId).OrderByDescending(t => t.StartedAt).First().TraceId;
        }
    }

    private static DateTime FarFuture => DateTime.UtcNow.AddHours(2);

    /// <summary>Sahibi ölmüş sayılacak kadar sonra (zaman aşımı + pay = 90 sn) ama azami yaştan (30 dk) önce.</summary>
    private static DateTime Stale => DateTime.UtcNow.AddMinutes(5);

    // ═══ checkpoint yaşam döngüsü ═══

    [Fact]
    public async Task CompletedTurn_LeavesNoCheckpointsBehind()
    {
        var rig = new Rig();
        var session = await rig.SessionAsync();

        var answer = await rig.NewTeam(new CheckpointTestRig.ScriptedLlm()).RunAsync("kategoriler neler",
            await rig.Sessions.GetHistoryAsync(session.SessionId, Ct), session, null, Ct);

        answer.Should().Contain("Fresh answer");
        (await rig.Repo.ListTurnsIdleSinceAsync(FarFuture, 100, Ct)).Should().BeEmpty("biten turun checkpoint'i silinir");
    }

    [Fact]
    public async Task TurnWithoutSessionOrHistory_IsNotCheckpointed()
    {
        var rig = new Rig();

        await rig.NewTeam(new CheckpointTestRig.ScriptedLlm()).RunAsync("kategoriler neler", ct: Ct);

        // Eval/A2A gibi oturumsuz koşuların yanıtı yazılacak bir geçmiş yoktur: devam ettirmenin anlamı olmaz.
        (await rig.Repo.ListTurnsIdleSinceAsync(FarFuture, 100, Ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task ClientCancellation_DeletesCheckpoints_SoACancelledTurnIsNeverResurrected()
    {
        var rig = new Rig();

        await rig.CrashedTurnAsync(hostStopping: false);

        (await rig.Repo.ListTurnsIdleSinceAsync(FarFuture, 100, Ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task HostShutdownMidTurn_KeepsTheCheckpoints()
    {
        var rig = new Rig();

        var traceId = await rig.CrashedTurnAsync(hostStopping: true);

        (await rig.Repo.GetTurnAsync(traceId, Ct)).Should().NotBeNull();
        (await rig.Repo.ListCheckpointsAsync(traceId, Ct)).Should().NotBeEmpty();
    }

    [Fact]
    public async Task CheckpointingDisabled_WritesNothing()
    {
        var rig = new Rig();
        rig.CheckpointOptions.Enabled = false;
        var session = await rig.SessionAsync();
        using var cts = new CancellationTokenSource();
        var llm = new CheckpointTestRig.ScriptedLlm(n => { if (n == 3) { rig.Shutdown.IsStopping = true; cts.Cancel(); } });

        try { await rig.NewTeam(llm).RunAsync("q", await rig.Sessions.GetHistoryAsync("s1", Ct), session, null, cts.Token); }
        catch (OperationCanceledException) { }

        (await rig.Repo.ListTurnsIdleSinceAsync(FarFuture, 100, Ct)).Should().BeEmpty();
    }

    // ═══ devam ═══

    [Fact]
    public async Task Recovery_ResumesTheInterruptedTurn_AndWritesTheAnswerToTheHistory()
    {
        var rig = new Rig();
        var traceId = await rig.CrashedTurnAsync();
        var resumedLlm = new CheckpointTestRig.ScriptedLlm();

        var result = await rig.Recovery(rig.NewTeam(resumedLlm)).RecoverStaleTurnsAsync(Stale, Ct);

        result.Resumed.Should().Be(1);
        resumedLlm.Instructions.Should().NotContain("agents/planning-agent", "tamamlanmış adım tekrarlanmaz");
        var history = await rig.Sessions.GetHistoryAsync("s1", Ct);
        history.Select(m => m.Text).Should().Contain(c => c.Contains("kategoriler neler"));
        history.Select(m => m.Text).Should().Contain(c => c.Contains("Fresh answer"));
        (await rig.Repo.GetTurnAsync(traceId, Ct)).Should().BeNull("devam eden tur temizlenir");
        var trace = rig.Traces.Get(traceId)!;
        trace.FinalResponse.Should().Contain("Fresh answer");
        trace.TerminationReason.Should().NotBe("process_restart");
    }

    [Fact]
    public async Task Recovery_DoesNotTouchTurnsThatMightStillBeRunning()
    {
        var rig = new Rig();
        var traceId = await rig.CrashedTurnAsync();
        var llm = new CheckpointTestRig.ScriptedLlm();

        // Şimdi: son checkpoint az önce yazıldı; zaman aşımı + pay dolmadı → sahibi hâlâ yaşıyor olabilir.
        var result = await rig.Recovery(rig.NewTeam(llm)).RecoverStaleTurnsAsync(DateTime.UtcNow, Ct);

        result.Resumed.Should().Be(0);
        llm.Calls.Should().Be(0);
        (await rig.Repo.GetTurnAsync(traceId, Ct)).Should().NotBeNull();
    }

    [Fact]
    public async Task Recovery_DiscardsTheTurn_WhenTheUserHasMovedOn()
    {
        var rig = new Rig();
        var traceId = await rig.CrashedTurnAsync();
        await rig.Sessions.AddExchangeAsync("s1", "yeniden sordum", "yanıt", ct: Ct);   // kullanıcı bu arada yeniden denedi
        var llm = new CheckpointTestRig.ScriptedLlm();

        var result = await rig.Recovery(rig.NewTeam(llm)).RecoverStaleTurnsAsync(Stale, Ct);

        result.Discarded.Should().Be(1);
        llm.Calls.Should().Be(0, "eski turun yanıtı, yeni sorunun yanıtından sonra gelmemeli");
        (await rig.Repo.GetTurnAsync(traceId, Ct)).Should().BeNull();
    }

    [Fact]
    public async Task Recovery_DiscardsTooOldTurns()
    {
        var rig = new Rig();
        await rig.CrashedTurnAsync();
        var llm = new CheckpointTestRig.ScriptedLlm();

        var result = await rig.Recovery(rig.NewTeam(llm)).RecoverStaleTurnsAsync(DateTime.UtcNow.AddHours(5), Ct);

        result.Discarded.Should().Be(1);
        llm.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Recovery_DiscardsSubTaskTurns_BecauseTheParentCompoundTurnCannotBeRebuilt()
    {
        var rig = new Rig();
        var traceId = Guid.NewGuid().ToString("N");
        await rig.Repo.BeginTurnAsync(new(traceId, "s1", "alt görev", null,
            new ReasoningResult { ConstrainedTargetAgent = WellKnown.AgentNames.Product }, 0, DateTime.UtcNow.AddMinutes(-10)), Ct);
        await rig.Repo.AddCheckpointAsync(traceId, "c1", null, "{}", Ct);
        await rig.SessionAsync();

        var result = await rig.Recovery(rig.NewTeam(new CheckpointTestRig.ScriptedLlm())).RecoverStaleTurnsAsync(Stale, Ct);

        result.Discarded.Should().Be(1);
        (await rig.Repo.GetTurnAsync(traceId, Ct)).Should().BeNull();
    }

    [Fact]
    public async Task Recovery_SkipsATurnAnotherPodIsAlreadyRecovering()
    {
        var rig = new Rig();
        var traceId = await rig.CrashedTurnAsync();
        var otherPodHoldsTheLock = Substitute.For<IAppDistributedLock>();
        otherPodHoldsTheLock.TryAcquireAsync(Arg.Any<string>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IAsyncDisposable?>(null));
        var llm = new CheckpointTestRig.ScriptedLlm();

        var result = await rig.Recovery(rig.NewTeam(llm), otherPodHoldsTheLock).RecoverStaleTurnsAsync(Stale, Ct);

        result.Skipped.Should().Be(1);
        llm.Calls.Should().Be(0);
        (await rig.Repo.GetTurnAsync(traceId, Ct)).Should().NotBeNull("diğer pod devam ettirecek");
    }

    [Fact]
    public async Task Recovery_WhenResumeFails_DropsTheTurn_InsteadOfRetryingForever()
    {
        var rig = new Rig();
        var traceId = await rig.CrashedTurnAsync();
        var failing = new CheckpointTestRig.ScriptedLlm(n => throw new InvalidOperationException("LLM down"));

        var result = await rig.Recovery(rig.NewTeam(failing)).RecoverStaleTurnsAsync(Stale, Ct);

        result.Failed.Should().Be(1);
        (await rig.Repo.GetTurnAsync(traceId, Ct)).Should().BeNull();
        (await rig.Sessions.GetHistoryAsync("s1", Ct)).Should().BeEmpty("başarısız devam geçmişe yanıt yazmaz");
    }

    [Fact]
    public async Task Recovery_HonorsMaxTurnsPerSweep()
    {
        var rig = new Rig();
        rig.CheckpointOptions.MaxTurnsPerSweep = 1;
        for (var i = 0; i < 2; i++)
        {
            var session = await rig.SessionAsync($"s{i}");
            using var cts = new CancellationTokenSource();
            var llm = new CheckpointTestRig.ScriptedLlm(n => { if (n == 3) { rig.Shutdown.IsStopping = true; cts.Cancel(); } });
            try { await rig.NewTeam(llm).RunAsync("q", await rig.Sessions.GetHistoryAsync(session.SessionId, Ct), session, null, cts.Token); }
            catch (OperationCanceledException) { }
            rig.Shutdown.IsStopping = false;
        }

        var result = await rig.Recovery(rig.NewTeam(new CheckpointTestRig.ScriptedLlm())).RecoverStaleTurnsAsync(Stale, Ct);

        result.Resumed.Should().Be(1);
        (await rig.Repo.ListTurnsIdleSinceAsync(FarFuture, 10, Ct)).Should().HaveCount(1, "kalan tur sonraki taramada");
    }
}
