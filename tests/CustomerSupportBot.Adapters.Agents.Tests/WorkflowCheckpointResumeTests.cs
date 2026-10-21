// Checkpoint'ten devam: GERÇEK MAF GroupChat workflow'u, betikli LLM ve KALICI repository'ye bağlı checkpoint deposu.
//
// Buradaki iki varsayım tüm özelliğin dayanağıdır ve ikisi de ancak çalıştırılınca doğrulanabildi:
//   1. Checkpoint'ten devam, ancak başka bir süreçte kurulan workflow'un yapısı ÖZDEŞSE mümkündür. Executor adı
//      "<Ajan>_<Id>" ve Id varsayılan olarak rastgele GUID: sabit Id olmadan MAF devamı "checkpoint uyumsuz" diye reddeder.
//   2. Devam, tamamlanmış adımları tekrarlamaz (planlama yeniden koşmaz) ve turu bitirir.

using CustomerSupportBot.Adapters.Agents.Checkpointing;
using CustomerSupportBot.Adapters.Persistence.InMemory;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Adapters.Agents.Tests;

public class WorkflowCheckpointResumeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void WorkflowStructure_IsIdenticalAcrossFreshInstances()
    {
        var a = CheckpointTestRig.Factory(new CheckpointTestRig.ScriptedLlm()).CreateWorkflow().ToMermaidString();
        var b = CheckpointTestRig.Factory(new CheckpointTestRig.ScriptedLlm()).CreateWorkflow().ToMermaidString();

        a.Should().Be(b, "aksi halde başka bir süreç eski checkpoint'i uyumsuz bulur");
    }

    [Fact]
    public async Task ResumeInAFreshWorkflow_FinishesTheTurn_WithoutRepeatingCompletedSteps()
    {
        var repository = new InMemoryWorkflowCheckpointRepository();
        var manager = CheckpointManager.CreateJson(new RepositoryCheckpointStore(repository));
        const string turnId = "turn-1";
        await repository.BeginTurnAsync(new(turnId, "s", "kategoriler neler", null, null, 0, DateTime.UtcNow), Ct);

        // ── Süreç 1: 3. LLM çağrısında "çöker" (iptal)
        using var crash = new CancellationTokenSource();
        var first = new CheckpointTestRig.ScriptedLlm(n => { if (n == 3) crash.Cancel(); });
        await using (var run = await InProcessExecution.RunStreamingAsync(
            CheckpointTestRig.Factory(first).CreateWorkflow(), new List<ChatMessage> { new(ChatRole.User, "kategoriler neler") },
            manager, turnId, crash.Token))
        {
            await run.TrySendMessageAsync(new TurnToken(emitEvents: true));
            try { await foreach (var _ in run.WatchStreamAsync().WithCancellation(crash.Token)) { } }
            catch (OperationCanceledException) { }
        }
        first.Instructions.Should().Contain("agents/planning-agent", "çökmeden önce planlama tamamlandı");
        (await repository.ListCheckpointsAsync(turnId, Ct)).Should().NotBeEmpty("her adım kalıcı checkpoint yazar");

        // ── Süreç 2: sıfırdan kurulmuş workflow, aynı kalıcı depo
        var latest = await manager.GetLatestCheckpointAsync(turnId);
        latest.Should().NotBeNull();
        var second = new CheckpointTestRig.ScriptedLlm();
        string? answer = null;
        await using (var resumed = await InProcessExecution.ResumeStreamingAsync(
            CheckpointTestRig.Factory(second).CreateWorkflow(), latest!, manager))
        {
            using var guard = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            guard.CancelAfter(TimeSpan.FromSeconds(30));
            await foreach (var evt in resumed.WatchStreamAsync().WithCancellation(guard.Token))
                if (evt is WorkflowOutputEvent { Data: IEnumerable<ChatMessage> messages })
                    answer = string.Join("\n", messages.Select(m => m.Text));
        }

        second.Instructions.Should().NotContain("agents/planning-agent", "tamamlanmış adım yeniden koşmamalı");
        second.Instructions.Should().Contain("agents/response-agent");
        answer.Should().Contain("Fresh answer");
    }
}
