// Tests/Services/InMemoryApprovalQueueTests.cs

using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using CustomerSupportBot.Tests.Shared;
using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Application.Ports.Outbound;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

public class InMemoryApprovalQueueTests
{
    private static InMemoryApprovalQueue NewQueue(int timeout = 10, bool autoApprove = false)
    {
        var opts = Options.Create(new ApprovalOptions
        {
            TimeoutSeconds = timeout,
            AutoApproveOnTimeout = autoApprove
        });
        return new InMemoryApprovalQueue(opts, new NoopApprovalExecutionRouter(), NullLogger<InMemoryApprovalQueue>.Instance);
    }

    private static ApprovalRequest NewReq(string id = "a1") => new()
    {
        Id = id,
        SessionId = "s1",
        ToolName = "order_placement_tool",
        Status = ApprovalStatus.Pending
    };

    [Fact]
    public async Task Create_AddsRequestAndFiresEvent()
    {
        var q = NewQueue();
        ApprovalRequest? captured = null;
        q.RequestCreated += (_, r) => captured = r;

        var req = await q.CreateAsync(NewReq(), TestContext.Current.CancellationToken);

        captured.Should().BeSameAs(req);
        q.GetPending().Should().ContainSingle();
    }

    [Fact]
    public async Task Decide_Approve_TransitionsAndFires()
    {
        var q = NewQueue();
        await q.CreateAsync(NewReq(), TestContext.Current.CancellationToken);
        ApprovalRequest? captured = null;
        q.RequestDecided += (_, r) => captured = r;

        (await q.DecideAsync("a1", true, "admin", "looks good", TestContext.Current.CancellationToken)).Should().BeTrue();

        captured.Should().NotBeNull();
        var pending = q.GetPending();
        pending.Should().BeEmpty();
        var got = q.Get("a1")!;
        got.Status.Should().Be(ApprovalStatus.Approved);
        got.DecidedBy.Should().Be("admin");
    }

    [Fact]
    public async Task Decide_Reject_StatusRejected()
    {
        var q = NewQueue();
        await q.CreateAsync(NewReq(), TestContext.Current.CancellationToken);
        await q.DecideAsync("a1", false, null, "bad", TestContext.Current.CancellationToken);
        q.Get("a1")!.Status.Should().Be(ApprovalStatus.Rejected);
    }

    [Fact]
    public async Task Decide_Twice_SecondReturnsFalse()
    {
        var q = NewQueue();
        await q.CreateAsync(NewReq(), TestContext.Current.CancellationToken);
        await q.DecideAsync("a1", true, ct: TestContext.Current.CancellationToken);
        (await q.DecideAsync("a1", false, ct: TestContext.Current.CancellationToken)).Should().BeFalse();
    }

    [Fact]
    public async Task Decide_UnknownId_False()
    {
        var q = NewQueue();
        (await q.DecideAsync("nope", true, ct: TestContext.Current.CancellationToken)).Should().BeFalse();
    }

    [Fact]
    public async Task AwaitDecisionAsync_Decided_ReturnsResult()
    {
        var q = NewQueue();
        await q.CreateAsync(NewReq(), TestContext.Current.CancellationToken);
        var task = q.AwaitDecisionAsync("a1", TestContext.Current.CancellationToken);
        await q.DecideAsync("a1", true, ct: TestContext.Current.CancellationToken);
        var result = await task;
        result.Status.Should().Be(ApprovalStatus.Approved);
    }

    [Fact]
    public async Task AwaitDecisionAsync_TimeoutAutoReject_StatusExpired()
    {
        var q = NewQueue(timeout: 1, autoApprove: false);
        var req = NewReq();
        await q.CreateAsync(req, TestContext.Current.CancellationToken);
        var result = await q.AwaitDecisionAsync("a1", TestContext.Current.CancellationToken);
        // AutoApprove off — reject + Expired
        result.Status.Should().Be(ApprovalStatus.Expired);
    }

    [Fact]
    public async Task AwaitDecisionAsync_TimeoutAutoApprove_StatusApproved()
    {
        var q = NewQueue(timeout: 1, autoApprove: true);
        await q.CreateAsync(NewReq(), TestContext.Current.CancellationToken);
        var result = await q.AwaitDecisionAsync("a1", TestContext.Current.CancellationToken);
        result.Status.Should().Be(ApprovalStatus.Approved);
    }

    // ─── Bulgu 1.1: mükerrer talep engelleme atomik olmalı ──────────────────────

    [Fact]
    public async Task CreateAsync_SameSessionToolAndSignature_ReturnsExistingPendingInstead()
    {
        var q = NewQueue();
        var first = await q.CreateAsync(new ApprovalRequest
        {
            Id = "a1",
            SessionId = "s1",
            ToolName = "order_cancel_tool",
            ParamSignature = "{\"orderId\":1030}"
        }, TestContext.Current.CancellationToken);

        var second = await q.CreateAsync(new ApprovalRequest
        {
            Id = "a2",
            SessionId = "s1",
            ToolName = "order_cancel_tool",
            ParamSignature = "{\"orderId\":1030}"
        }, TestContext.Current.CancellationToken);

        second.Id.Should().Be(first.Id,
            "aynı session+tool+parametre için ikinci çağrı yeni kayıt YARATMAMALI, mevcudu döndürmeli");
    }

    [Fact]
    public async Task CreateAsync_ConcurrentDuplicates_OnlyOneEntrySurvives()
    {
        var q = NewQueue();
        const string sessionId = "s-concurrent";
        const string sig = "{\"orderId\":1030}";

        var barrier = new Barrier(10);
        var tasks = Enumerable.Range(0, 10).Select(i => Task.Run(async () =>
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(5));
            return await q.CreateAsync(new ApprovalRequest
            {
                Id = $"race-{i}",
                SessionId = sessionId,
                ToolName = "order_cancel_tool",
                ParamSignature = sig
            }, TestContext.Current.CancellationToken);
        })).ToArray();

        var results = await Task.WhenAll(tasks);

        results.Select(r => r.Id).Distinct().Should().ContainSingle(
            "10 eşzamanlı istekten yalnızca biri gerçek bir kayıt yaratmalı, diğer 9'u onu geri almalı");
    }

    [Fact]
    public async Task CreateAsync_DifferentSignature_CreatesSeparateEntry()
    {
        var q = NewQueue();
        var first = await q.CreateAsync(new ApprovalRequest
        {
            Id = "a1",
            SessionId = "s1",
            ToolName = "order_cancel_tool",
            ParamSignature = "{\"orderId\":1030}"
        }, TestContext.Current.CancellationToken);

        var second = await q.CreateAsync(new ApprovalRequest
        {
            Id = "a2",
            SessionId = "s1",
            ToolName = "order_cancel_tool",
            ParamSignature = "{\"orderId\":1042}"
        }, TestContext.Current.CancellationToken);

        second.Id.Should().Be("a2", "farklı sipariş numarası farklı işlemdir, dedup'a takılmamalı");
    }

    [Fact]
    public async Task CreateAsync_NullParamSignature_NeverDedupes()
    {
        var q = NewQueue();
        var first = await q.CreateAsync(new ApprovalRequest
        {
            Id = "a1",
            SessionId = null,
            ToolName = "order_cancel_tool",
            ParamSignature = null
        }, TestContext.Current.CancellationToken);

        var second = await q.CreateAsync(new ApprovalRequest
        {
            Id = "a2",
            SessionId = null,
            ToolName = "order_cancel_tool",
            ParamSignature = null
        }, TestContext.Current.CancellationToken);

        second.Id.Should().Be("a2");
    }
}
