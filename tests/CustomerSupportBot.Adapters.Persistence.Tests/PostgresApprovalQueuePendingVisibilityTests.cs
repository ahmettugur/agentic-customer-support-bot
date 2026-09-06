// Bekleyen onayların GÖRÜNÜRLÜĞÜ.
//
// Onay kuyruğu, bekleyen kayıtları süreç-içi bir cache'te tutar ve pod'lar arası yayılım
// Redis pub/sub ile olur. Pub/sub EN FAZLA bir kez teslim eder: Redis restart'ı, ağ kesintisi
// veya abonelik boşluğu bir mesajı düşürebilir. Cache bir kez hydrate olduktan sonra bir daha
// DB'ye bakmadığı için böyle bir kayıp KALICI olur.
//
// Bunun sessiz bir arıza olması ağırlığını artırıyor: müşteri talebini göndermiştir, admin
// panelinde talep hiç belirmez, süpürme onu bulamadığı için zaman aşımına da uğratılmaz.
// Talep sonsuza kadar bekler ve kimse fark etmez.
//
// Buradaki "Redis mesajı kayboldu" modeli, iki pod'u AYRI hub'lara bağlamaktır — B pod'u
// A'nın yayınını hiç duymaz. Aynı veritabanını paylaşırlar, gerçek kurulumdaki gibi.

using CustomerSupportBot.Adapters.Persistence.Postgres;
using CustomerSupportBot.Adapters.Redis;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Locking;
using CustomerSupportBot.Application.Ports.Outbound.Messaging;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

[Collection("PostgresCatalog")]
public class PostgresApprovalQueuePendingVisibilityTests
{
    [Fact]
    public async Task DecisionOnPodMissingCreatedEvent_LoadsDurableRequest_AndClaimsOnlyOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        var podA = NewQueue(new InMemoryMessageBusHub().CreateNode());
        var podB = NewQueue(new InMemoryMessageBusHub().CreateNode());
        _ = podB.GetPending();
        var created = await podA.CreateAsync(NewRequest(Guid.NewGuid().ToString()), ct);
        podB.Get(created.Id).Should().BeNull();
        (await podB.DecideAsync(created.Id, true, "admin", ct: ct)).Should().BeTrue();
        (await podA.DecideAsync(created.Id, true, "admin", ct: ct)).Should().BeFalse();
        (await podA.GetAsync(created.Id, ct))!.ExecutionStatus.Should().Be(ApprovalExecutionStatus.Succeeded);
    }

    private readonly PostgresCatalogFixture _fixture;

    public PostgresApprovalQueuePendingVisibilityTests(PostgresCatalogFixture fixture)
        => _fixture = fixture;

    private sealed class NoopRouter : IApprovalExecutionRouter
    {
        public Task<ApprovalExecutionOutcome> ExecuteAsync(
            ApprovalRequest request, CancellationToken ct = default)
            => Task.FromResult(new ApprovalExecutionOutcome(true, "ok"));
    }

    private PostgresApprovalQueue NewQueue(IMessageBusPort bus, IApprovalExecutionRouter? router = null) => new(
        _fixture.DbFactory,
        Options.Create(new ApprovalOptions { StalePendingHours = 72 }),
        bus,
        new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 5 })),
        router ?? new NoopRouter(),
        NullLogger<PostgresApprovalQueue>.Instance);

    private static ApprovalRequest NewRequest(string sessionId) => new()
    {
        SessionId = sessionId,
        CustomerId = "1001",
        ToolName = WellKnown.ToolNames.OrderCancel,
        AgentName = "OrderAgent",
        Parameters = new Dictionary<string, object?> { ["orderId"] = 1030 }
    };

    [Fact]
    public async Task RunningDecision_CannotBeMarkedSeen_BeforeDurableOutcome()
    {
        var ct = TestContext.Current.CancellationToken;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var outcome = new TaskCompletionSource<ApprovalExecutionOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var router = Substitute.For<IApprovalExecutionRouter>();
        router.ExecuteAsync(Arg.Any<ApprovalRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => { started.SetResult(); return outcome.Task; });
        var queue = NewQueue(new InMemoryMessageBusHub().CreateNode(), router);
        var sessionId = Guid.NewGuid().ToString();
        var request = await queue.CreateAsync(NewRequest(sessionId), ct);
        var decision = queue.DecideAsync(request.Id, true, "admin", ct: ct);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        try
        {
            (await queue.GetUnseenForSessionAsync(sessionId, "1001", ct)).Should().BeEmpty();
            (await queue.MarkSeenAsync(request.Id, ct)).Should().BeFalse();
            (await queue.GetAsync(request.Id, ct))!.CustomerSeenAt.Should().BeNull();
        }
        finally { outcome.TrySetResult(new ApprovalExecutionOutcome(false, "failed after approval")); }
        await decision;
        (await queue.GetUnseenForSessionAsync(sessionId, "1001", ct)).Should().ContainSingle()
            .Which.ExecutionStatus.Should().Be(ApprovalExecutionStatus.Failed);
        (await queue.MarkSeenAsync(request.Id, ct)).Should().BeTrue();
        (await queue.GetUnseenForSessionAsync(sessionId, "1001", ct)).Should().BeEmpty();
    }

    /// <summary>
    /// Asıl bulgu: A pod'unda oluşan talep, yayın kaybolduğunda B pod'unun cache'inde hiç
    /// belirmez ve orada KALICI olarak görünmez kalır — B bir kez hydrate olduktan sonra DB'ye
    /// bir daha bakmaz. Kalıcı okuma ise onu bulmalıdır.
    /// </summary>
    [Fact]
    public async Task PendingCreatedOnAnotherPod_IsInvisibleToCache_ButVisibleToPersistentRead()
    {
        var ct = TestContext.Current.CancellationToken;

        // İki ayrı hub = birbirini duymayan iki pod (yayın kayboldu).
        var podA = NewQueue(new InMemoryMessageBusHub().CreateNode());
        var podB = NewQueue(new InMemoryMessageBusHub().CreateNode());

        // B'yi ÖNCE hydrate ediyoruz: kaydın oluşmasından önceki dünyayı görmüş olsun.
        // Kritik olan bu — hydrate sonrası B'nin cache'i bir daha DB'ye bakmaz.
        _ = podB.GetPending();

        var created = await podA.CreateAsync(NewRequest($"sess-{Guid.NewGuid():N}"), ct);

        podB.GetPending().Should().NotContain(r => r.Id == created.Id,
            "yayın kaybolduğu için B'nin cache'i bu kayıttan haberdar olamaz");

        var persistent = await podB.GetPendingAsync(ct);
        persistent.Should().Contain(r => r.Id == created.Id,
            "kalıcı okuma kayıtların gerçek kaynağına gitmeli");
    }

    /// <summary>
    /// Kalıcı okuma aynı zamanda uzlaştırma noktasıdır: bir kez okunduktan sonra kayıt
    /// B'nin cache'ine de girer. Aksi hâlde her okuma DB'ye gitmek zorunda kalır ve
    /// kaydı bekleyen bildirim yolu kör kalmaya devam ederdi.
    /// </summary>
    [Fact]
    public async Task PersistentRead_RepairsTheStaleCache()
    {
        var ct = TestContext.Current.CancellationToken;
        var podA = NewQueue(new InMemoryMessageBusHub().CreateNode());
        var podB = NewQueue(new InMemoryMessageBusHub().CreateNode());
        _ = podB.GetPending();

        var created = await podA.CreateAsync(NewRequest($"sess-{Guid.NewGuid():N}"), ct);
        _ = await podB.GetPendingAsync(ct);

        podB.GetPending().Should().Contain(r => r.Id == created.Id);
        podB.Get(created.Id).Should().NotBeNull();
    }

    /// <summary>
    /// Uzlaştırma, kaydı OLUŞTURAN pod'un girdisini ezmemeli. Ezilseydi o girdinin
    /// TaskCompletionSource'u kaybolur ve kaydı bekleyen AwaitDecisionAsync karar geldiğinde
    /// hiçbir zaman tamamlanmazdı.
    /// </summary>
    [Fact]
    public async Task PersistentRead_DoesNotBreakAWaiterOnTheCreatingPod()
    {
        var ct = TestContext.Current.CancellationToken;
        var pod = NewQueue(new InMemoryMessageBusHub().CreateNode());

        var created = await pod.CreateAsync(NewRequest($"sess-{Guid.NewGuid():N}"), ct);
        var waiter = pod.AwaitDecisionAsync(created.Id, ct);

        // Uzlaştırma çalışır — bekleyen varken.
        _ = await pod.GetPendingAsync(ct);

        (await pod.DecideAsync(created.Id, approved: true, decidedBy: "admin", ct: ct))
            .Should().BeTrue();

        var resolved = await waiter.WaitAsync(TimeSpan.FromSeconds(10), ct);
        resolved.Status.Should().Be(ApprovalStatus.Approved);
    }

    /// <summary>Karara bağlanmış kayıtlar bekleyenler listesinde görünmemeli.</summary>
    [Fact]
    public async Task DecidedRequests_AreNotReturnedByPersistentRead()
    {
        var ct = TestContext.Current.CancellationToken;
        var pod = NewQueue(new InMemoryMessageBusHub().CreateNode());

        var created = await pod.CreateAsync(NewRequest($"sess-{Guid.NewGuid():N}"), ct);
        await pod.DecideAsync(created.Id, approved: false, decidedBy: "admin", ct: ct);

        (await pod.GetPendingAsync(ct)).Should().NotContain(r => r.Id == created.Id);
    }
}
