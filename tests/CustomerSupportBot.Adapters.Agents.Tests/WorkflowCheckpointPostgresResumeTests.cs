// Gerçek Postgres (jsonb) üzerinden checkpoint → devam.
//
// jsonb, nesne anahtarlarını yeniden sıralar ve boşlukları normalleştirir. MAF'ın polimorfik JSON'u "$type" üyesinin
// ilk sırada olmasını bekleyebilir; bellek içi depo ham metni aynen saklar, yani bu riski HİÇ sınamaz. Bu test aynı
// çökme/devam senaryosunu kalıcı depoyla çalıştırır.

using CustomerSupportBot.Adapters.Agents.Checkpointing;
using CustomerSupportBot.Adapters.Persistence.Postgres;
using CustomerSupportBot.Tests.Shared;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Adapters.Agents.Tests;

[Collection("PostgresCatalog")]
public class WorkflowCheckpointPostgresResumeTests(PostgresCatalogFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ResumeFromJsonbBackedCheckpoint_FinishesTheTurn()
    {
        var repository = new PostgresWorkflowCheckpointRepository(fixture.DbFactory);
        var manager = CheckpointManager.CreateJson(new RepositoryCheckpointStore(repository));
        var turnId = Guid.NewGuid().ToString("N");
        await repository.BeginTurnAsync(new(turnId, "s", "kategoriler neler", null, null, 0, DateTime.UtcNow), Ct);

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

        second.Instructions.Should().NotContain("agents/planning-agent");
        answer.Should().Contain("Fresh answer");
        await repository.DeleteTurnAsync(turnId, Ct);
    }
}
