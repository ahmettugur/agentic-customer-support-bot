// Tests/Services/PostgresApprovalQueueDedupTests.cs
//
// Bulgu 1.1: ApprovalGateService'in eskiden yaptığı "önce GetPending() (cache) tara, sonra
// yoksa CreateAsync çağır" deseni atomik değildi — kontrol ile yazma arasında (TOCTOU) ve
// çok-pod'lu kurulumda (cache eksik olabilir) bir yarış vardı. İki pod aynı session+tool+
// parametre kombinasyonu için eşzamanlı CreateAsync çağırırsa, DB'deki kısmi unique index
// (ux_approvals_pending_dedup) ikinci INSERT'i reddetmeli ve CreateAsync yarışı kazanan
// kaydı geri dönmeli — İKİ AYRI Pending kayıt OLUŞMAMALI.

using CustomerSupportBot.Adapters.Persistence.Postgres;
using CustomerSupportBot.Adapters.Redis;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Locking;
using CustomerSupportBot.Application.Ports.Outbound.Messaging;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

[Collection("PostgresCatalog")]
public class PostgresApprovalQueueDedupTests
{
    private readonly PostgresCatalogFixture _fixture;

    public PostgresApprovalQueueDedupTests(PostgresCatalogFixture fixture) => _fixture = fixture;

    private sealed class NoopExecutionRouter : IApprovalExecutionRouter
    {
        public Task<ApprovalExecutionOutcome> ExecuteAsync(ApprovalRequest request, CancellationToken ct = default) =>
            Task.FromResult(new ApprovalExecutionOutcome(true, "noop"));
    }

    private static IAppDistributedLock NewLock() =>
        new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 5 }));

    private PostgresApprovalQueue NewQueue(IMessageBusPort bus) =>
        new(
            _fixture.DbFactory,
            Options.Create(new ApprovalOptions { StalePendingHours = 72 }),
            bus,
            NewLock(),
            new NoopExecutionRouter(),
            NullLogger<PostgresApprovalQueue>.Instance);

    private static ApprovalRequest NewRequest(string sessionId, string toolName, string paramSignature) => new()
    {
        SessionId = sessionId,
        CustomerId = "ALFKI",
        ToolName = toolName,
        AgentName = "OrderAgent",
        Parameters = new Dictionary<string, object?> { ["orderId"] = 1030 },
        ParamSignature = paramSignature
    };

    [Fact]
    public async Task CreateAsync_ConcurrentDuplicateAcrossPods_OnlyOnePendingRowSurvives()
    {
        var sessionId = $"s-{Guid.NewGuid():N}";
        var toolName = WellKnown.ToolNames.OrderCancel;
        const string sig = "{\"orderId\":1030}";

        // İki AYRI PostgresApprovalQueue örneği = iki pod, AYNI Postgres'e bağlı.
        var hub = new InMemoryMessageBusHub();
        var podA = NewQueue(hub.CreateNode());
        var podB = NewQueue(hub.CreateNode());

        var barrier = new Barrier(2);
        Task<ApprovalRequest> RaceAsync(PostgresApprovalQueue pod) => Task.Run(async () =>
        {
            var req = NewRequest(sessionId, toolName, sig);
            barrier.SignalAndWait(TimeSpan.FromSeconds(5));
            return await pod.CreateAsync(req, TestContext.Current.CancellationToken);
        });

        var taskA = RaceAsync(podA);
        var taskB = RaceAsync(podB);
        var results = await Task.WhenAll(taskA, taskB);

        // Her iki çağrı da BAŞARILI dönmeli (biri throw etmemeli) ve AYNI Id'yi işaret etmeli —
        // yarışı kaybeden pod, kazananın kaydını geri almış olmalı.
        results[0].Id.Should().Be(results[1].Id,
            "iki eşzamanlı çağrı aynı session+tool+parametre için TEK bir onay kaydına yakınsamalı");

        // DB'de gerçekten TEK bir Pending satır var mı — asıl kanıt burada.
        await using var ctx = await _fixture.DbFactory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var pendingCount = ctx.Approvals.Count(a =>
            a.SessionId == sessionId && a.ToolName == toolName && a.Status == "Pending");
        pendingCount.Should().Be(1,
            "DB kısıtı ikinci INSERT'i reddetmeliydi — iki kayıt oluşursa admin ikisini de " +
            "onaylayabilir ve gerçek iş İKİ KEZ yürütülür");
    }

    [Fact]
    public async Task CreateAsync_DifferentParamSignature_CreatesSeparateRows()
    {
        var sessionId = $"s-{Guid.NewGuid():N}";
        var toolName = WellKnown.ToolNames.OrderCancel;
        var hub = new InMemoryMessageBusHub();
        var queue = NewQueue(hub.CreateNode());

        var reqA = NewRequest(sessionId, toolName, "{\"orderId\":1030}");
        var reqB = NewRequest(sessionId, toolName, "{\"orderId\":1042}");

        var createdA = await queue.CreateAsync(reqA, TestContext.Current.CancellationToken);
        var createdB = await queue.CreateAsync(reqB, TestContext.Current.CancellationToken);

        createdA.Id.Should().NotBe(createdB.Id,
            "farklı sipariş numaraları farklı işlemlerdir, dedup'a takılmamalı");
    }

    [Fact]
    public async Task CreateAsync_NullParamSignature_NeverDedupes()
    {
        // SessionId'siz/kimliksiz akışlarda (A2A/realtime) ParamSignature set edilmez —
        // kısmi index bu satırları hiç kapsamamalı, her çağrı kendi kaydını almalı.
        var toolName = WellKnown.ToolNames.OrderCancel;
        var hub = new InMemoryMessageBusHub();
        var queue = NewQueue(hub.CreateNode());

        var reqA = new ApprovalRequest { SessionId = null, ToolName = toolName, ParamSignature = null };
        var reqB = new ApprovalRequest { SessionId = null, ToolName = toolName, ParamSignature = null };

        var createdA = await queue.CreateAsync(reqA, TestContext.Current.CancellationToken);
        var createdB = await queue.CreateAsync(reqB, TestContext.Current.CancellationToken);

        createdA.Id.Should().NotBe(createdB.Id);
    }
}
