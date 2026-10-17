// IWorkflowCheckpointRepository sözleşmesi: bellek içi ve Postgres uygulaması AYNI testlerden geçmeli.

using System.Text.Json;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

public abstract class WorkflowCheckpointRepositoryContract
{
    protected abstract IWorkflowCheckpointRepository Create();
    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected static string NewId() => Guid.NewGuid().ToString("N");

    // jsonb metni yeniden biçimlendirir ({"n":2} -> {"n": 2}); MAF ayrıştırılmış JSON okur, bu yüzden değer karşılaştırılır.
    protected static int N(string? payload) => JsonDocument.Parse(payload!).RootElement.GetProperty("n").GetInt32();

    protected static WorkflowTurnContext Turn(string traceId, DateTime? started = null, ReasoningResult? reasoning = null) =>
        new(traceId, "session-1", "siparişim nerede", "1001", reasoning, HistoryCount: 4, started ?? DateTime.UtcNow);

    [Fact]
    public async Task Turn_RoundTrips_IncludingReasoning()
    {
        var repo = Create();
        var trace = NewId();
        var reasoning = new ReasoningResult { Intent = "sipariş_sorgulama", ConstrainedTargetAgent = null };

        await repo.BeginTurnAsync(Turn(trace, reasoning: reasoning), Ct);
        var loaded = await repo.GetTurnAsync(trace, Ct);

        Assert.NotNull(loaded);
        Assert.Equal("session-1", loaded.SessionId);
        Assert.Equal("siparişim nerede", loaded.Query);
        Assert.Equal("1001", loaded.CustomerId);
        Assert.Equal(4, loaded.HistoryCount);
        Assert.Equal("sipariş_sorgulama", loaded.Reasoning?.Intent);
    }

    [Fact]
    public async Task GetTurn_Unknown_ReturnsNull() => Assert.Null(await Create().GetTurnAsync(NewId(), Ct));

    [Fact]
    public async Task Checkpoint_RoundTrips_AndListsOldestFirst()
    {
        var repo = Create();
        var trace = NewId();
        await repo.BeginTurnAsync(Turn(trace), Ct);

        await repo.AddCheckpointAsync(trace, "c1", null, "{\"n\":1}", Ct);
        await Task.Delay(15, Ct);
        await repo.AddCheckpointAsync(trace, "c2", "c1", "{\"n\":2}", Ct);

        Assert.Equal(2, N(await repo.GetCheckpointPayloadAsync(trace, "c2", Ct)));
        var list = await repo.ListCheckpointsAsync(trace, Ct);
        Assert.Equal(["c1", "c2"], list.Select(c => c.CheckpointId));
        Assert.Equal("c1", list[1].ParentCheckpointId);
        Assert.Null(list[0].ParentCheckpointId);
    }

    [Fact]
    public async Task GetCheckpointPayload_Unknown_ReturnsNull() =>
        Assert.Null(await Create().GetCheckpointPayloadAsync(NewId(), "yok", Ct));

    [Fact]
    public async Task AddCheckpoint_WithSameId_IsIdempotent()
    {
        var repo = Create();
        var trace = NewId();
        await repo.BeginTurnAsync(Turn(trace), Ct);

        await repo.AddCheckpointAsync(trace, "c1", null, "{\"n\":1}", Ct);
        await repo.AddCheckpointAsync(trace, "c1", null, "{\"n\":2}", Ct);

        Assert.Single(await repo.ListCheckpointsAsync(trace, Ct));
        Assert.Equal(2, N(await repo.GetCheckpointPayloadAsync(trace, "c1", Ct)));
    }

    [Fact]
    public async Task Checkpoints_AreScopedPerTurn()
    {
        var repo = Create();
        var a = NewId();
        var b = NewId();
        await repo.BeginTurnAsync(Turn(a), Ct);
        await repo.BeginTurnAsync(Turn(b), Ct);

        await repo.AddCheckpointAsync(a, "c1", null, "{\"turn\":\"a\"}", Ct);

        Assert.Empty(await repo.ListCheckpointsAsync(b, Ct));
        Assert.Null(await repo.GetCheckpointPayloadAsync(b, "c1", Ct));
    }

    [Fact]
    public async Task DeleteTurn_RemovesTheTurnAndItsCheckpoints_ButNotOthers()
    {
        var repo = Create();
        var gone = NewId();
        var kept = NewId();
        await repo.BeginTurnAsync(Turn(gone), Ct);
        await repo.BeginTurnAsync(Turn(kept), Ct);
        await repo.AddCheckpointAsync(gone, "c1", null, "{}", Ct);
        await repo.AddCheckpointAsync(kept, "c1", null, "{}", Ct);

        await repo.DeleteTurnAsync(gone, Ct);

        Assert.Null(await repo.GetTurnAsync(gone, Ct));
        Assert.Empty(await repo.ListCheckpointsAsync(gone, Ct));
        Assert.NotNull(await repo.GetTurnAsync(kept, Ct));
        Assert.Single(await repo.ListCheckpointsAsync(kept, Ct));
    }

    [Fact]
    public async Task DeleteTurn_Unknown_DoesNotThrow() => await Create().DeleteTurnAsync(NewId(), Ct);

    // Postgres varyantında aynı veritabanını diğer testler de kullanır: bu testler yalnızca KENDİ turlarına bakar.

    [Fact]
    public async Task ListTurnsIdleSince_ReturnsOnlyTurnsWhoseLastActivityIsBeforeTheCutoff()
    {
        var repo = Create();
        var stale = NewId();
        var fresh = NewId();
        var freshByCheckpoint = NewId();
        // Eski başlamış ama yeni checkpoint yazmış tur "yaşıyor" sayılmalı.
        await repo.BeginTurnAsync(Turn(stale, started: DateTime.UtcNow.AddMinutes(-10)), Ct);
        await repo.BeginTurnAsync(Turn(fresh, started: DateTime.UtcNow), Ct);
        await repo.BeginTurnAsync(Turn(freshByCheckpoint, started: DateTime.UtcNow.AddMinutes(-10)), Ct);
        await repo.AddCheckpointAsync(freshByCheckpoint, "c1", null, "{}", Ct);

        var ids = (await repo.ListTurnsIdleSinceAsync(DateTime.UtcNow.AddMinutes(-5), max: 1000, Ct)).Select(t => t.TraceId).ToList();

        Assert.Contains(stale, ids);
        Assert.DoesNotContain(fresh, ids);
        Assert.DoesNotContain(freshByCheckpoint, ids);
    }

    [Fact]
    public async Task ListTurnsIdleSince_ListsOldestFirst()
    {
        var repo = Create();
        var oldest = NewId();
        var middle = NewId();
        var newest = NewId();
        await repo.BeginTurnAsync(Turn(newest, started: DateTime.UtcNow.AddMinutes(-20)), Ct);
        await repo.BeginTurnAsync(Turn(oldest, started: DateTime.UtcNow.AddMinutes(-40)), Ct);
        await repo.BeginTurnAsync(Turn(middle, started: DateTime.UtcNow.AddMinutes(-30)), Ct);

        var ids = (await repo.ListTurnsIdleSinceAsync(DateTime.UtcNow.AddMinutes(-10), max: 1000, Ct)).Select(t => t.TraceId).ToList();

        Assert.True(ids.IndexOf(oldest) >= 0 && ids.IndexOf(oldest) < ids.IndexOf(middle) && ids.IndexOf(middle) < ids.IndexOf(newest));
    }

    [Fact]
    public async Task ListTurnsIdleSince_HonorsMax()
    {
        var repo = Create();
        await repo.BeginTurnAsync(Turn(NewId(), started: DateTime.UtcNow.AddMinutes(-20)), Ct);
        await repo.BeginTurnAsync(Turn(NewId(), started: DateTime.UtcNow.AddMinutes(-20)), Ct);

        Assert.Single(await repo.ListTurnsIdleSinceAsync(DateTime.UtcNow.AddMinutes(-10), max: 1, Ct));
    }
}
