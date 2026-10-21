using CustomerSupportBot.Adapters.Persistence.Postgres;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Tests.Shared;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

[Collection("PostgresCatalog")]
public class PostgresWorkflowCheckpointRepositoryTests(PostgresCatalogFixture fixture) : WorkflowCheckpointRepositoryContract
{
    protected override IWorkflowCheckpointRepository Create() => new PostgresWorkflowCheckpointRepository(fixture.DbFactory);

    [Fact]
    public async Task DeletingATurn_CascadesToItsCheckpointRows()
    {
        var repo = Create();
        var trace = NewId();
        await repo.BeginTurnAsync(Turn(trace), Ct);
        await repo.AddCheckpointAsync(trace, "c1", null, "{}", Ct);

        await repo.DeleteTurnAsync(trace, Ct);

        await using var db = await fixture.DbFactory.CreateDbContextAsync(Ct);
        Assert.False(db.WorkflowCheckpoints.Any(c => c.TraceId == trace), "FK cascade checkpoint satırlarını da silmeli");
    }
}
