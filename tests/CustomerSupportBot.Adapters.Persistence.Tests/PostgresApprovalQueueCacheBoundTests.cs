// Tests/Services/PostgresApprovalQueueCacheBoundTests.cs
//
// Onay kuyruğunun bellek içi cache'i SINIRLI olmalı. Kayıtlar eskiden hiç çıkarılmıyordu:
// her onay talebi süreç ömrü boyunca bellekte kalıyordu. Karara bağlanmış en eski kayıtlar
// artık çıkarılır; Pending kayıtlar asla çıkarılmaz ve çıkarılan bir kayda tekil erişim DB'ye
// düşer.

using CustomerSupportBot.Adapters.Persistence.Postgres;
using CustomerSupportBot.Adapters.Redis;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

[Collection("PostgresCatalog")]
public class PostgresApprovalQueueCacheBoundTests
{
    private const int Cap = 4;
    private readonly PostgresCatalogFixture _fixture;

    public PostgresApprovalQueueCacheBoundTests(PostgresCatalogFixture fixture) => _fixture = fixture;

    private sealed class NoopExecutionRouter : IApprovalExecutionRouter
    {
        public Task<ApprovalExecutionOutcome> ExecuteAsync(ApprovalRequest request, CancellationToken ct = default) =>
            Task.FromResult(new ApprovalExecutionOutcome(true, "noop"));
    }

    private PostgresApprovalQueue NewQueue() => new(
        _fixture.DbFactory,
        Options.Create(new ApprovalOptions { StalePendingHours = 72 }),
        new InMemoryMessageBusHub().CreateNode(),
        new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 5 })),
        new NoopExecutionRouter(),
        NullLogger<PostgresApprovalQueue>.Instance)
    {
        MaxCachedDecided = Cap
    };

    private static ApprovalRequest NewRequest(string sessionId, int n) => new()
    {
        SessionId = sessionId,
        CustomerId = "ALFKI",
        ToolName = "order_cancel_tool",
        AgentName = "OrderAgent",
        Parameters = new Dictionary<string, object?> { ["orderId"] = n },
        ParamSignature = $"order_cancel_tool:{n}",
        RequestedAt = DateTime.UtcNow.AddSeconds(n)
    };

    [Fact]
    public async Task DecidedEntries_AreEvicted_PendingOnesAreKept()
    {
        var ct = TestContext.Current.CancellationToken;
        var sessionId = $"s-{Guid.NewGuid():N}";
        var queue = NewQueue();
        await queue.WarmUpAsync(ct);

        var pending = new List<string>();
        for (var i = 0; i < 2; i++)
            pending.Add((await queue.CreateAsync(NewRequest(sessionId, 1000 + i), ct)).Id);

        var decided = new List<string>();
        for (var i = 0; i < 12; i++)
        {
            var created = await queue.CreateAsync(NewRequest(sessionId, i), ct);
            (await queue.DecideAsync(created.Id, approved: i % 2 == 0, "admin", ct: ct)).Should().BeTrue();
            decided.Add(created.Id);
        }

        var cached = queue.GetRecent(int.MaxValue).Where(r => r.SessionId == sessionId).ToList();
        cached.Count(r => r.Status != ApprovalStatus.Pending).Should()
            .BeLessThanOrEqualTo(Cap + Cap / 4, "karara bağlanmış kayıtlar sınırın ötesinde birikmemeli");
        cached.Select(r => r.Id).Should().Contain(pending, "Pending kayıtlar asla çıkarılmaz");
        cached.Select(r => r.Id).Should().Contain(decided[^1], "en yeni kararlar cache'te kalır");

        var evicted = decided[0];
        queue.Get(evicted).Should().BeNull("en eski karar cache'ten çıkarıldı");
        var fromDb = await queue.GetAsync(evicted, ct);
        fromDb.Should().NotBeNull("tekil erişim DB'ye düşer");
        fromDb!.Status.Should().Be(ApprovalStatus.Approved);

        (await queue.DecideAsync(evicted, approved: false, "admin", ct: ct)).Should()
            .BeFalse("çıkarılmış ama zaten karara bağlanmış kayıt ikinci kez karara bağlanamaz");
    }
}
