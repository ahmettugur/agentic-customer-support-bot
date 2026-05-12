// Tests/Services/InMemoryApprovalQueueTests.cs

using CustomerSupportBot.Api.Models;
using CustomerSupportBot.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Api.Tests.Services;

public class InMemoryApprovalQueueTests
{
    private static InMemoryApprovalQueue NewQueue(int timeout = 10, bool autoApprove = false)
    {
        var opts = Options.Create(new ApprovalOptions
        {
            TimeoutSeconds = timeout,
            AutoApproveOnTimeout = autoApprove
        });
        return new InMemoryApprovalQueue(opts, NullLogger<InMemoryApprovalQueue>.Instance);
    }

    private static ApprovalRequest NewReq(string id = "a1") => new()
    {
        Id = id,
        SessionId = "s1",
        ToolName = "order_placement_tool",
        Status = ApprovalStatus.Pending
    };

    [Fact]
    public void Create_AddsRequestAndFiresEvent()
    {
        var q = NewQueue();
        ApprovalRequest? captured = null;
        q.RequestCreated += (_, r) => captured = r;

        var req = q.Create(NewReq());

        captured.Should().BeSameAs(req);
        q.GetPending().Should().ContainSingle();
    }

    [Fact]
    public void Decide_Approve_TransitionsAndFires()
    {
        var q = NewQueue();
        q.Create(NewReq());
        ApprovalRequest? captured = null;
        q.RequestDecided += (_, r) => captured = r;

        q.Decide("a1", true, "admin", "looks good").Should().BeTrue();

        captured.Should().NotBeNull();
        var pending = q.GetPending();
        pending.Should().BeEmpty();
        var got = q.Get("a1")!;
        got.Status.Should().Be(ApprovalStatus.Approved);
        got.DecidedBy.Should().Be("admin");
    }

    [Fact]
    public void Decide_Reject_StatusRejected()
    {
        var q = NewQueue();
        q.Create(NewReq());
        q.Decide("a1", false, null, "bad");
        q.Get("a1")!.Status.Should().Be(ApprovalStatus.Rejected);
    }

    [Fact]
    public void Decide_Twice_SecondReturnsFalse()
    {
        var q = NewQueue();
        q.Create(NewReq());
        q.Decide("a1", true);
        q.Decide("a1", false).Should().BeFalse();
    }

    [Fact]
    public void Decide_UnknownId_False()
    {
        var q = NewQueue();
        q.Decide("nope", true).Should().BeFalse();
    }

    [Fact]
    public async Task AwaitDecisionAsync_Decided_ReturnsResult()
    {
        var q = NewQueue();
        q.Create(NewReq());
        var task = q.AwaitDecisionAsync("a1", TestContext.Current.CancellationToken);
        q.Decide("a1", true);
        var result = await task;
        result.Status.Should().Be(ApprovalStatus.Approved);
    }

    [Fact]
    public async Task AwaitDecisionAsync_TimeoutAutoReject_StatusExpired()
    {
        var q = NewQueue(timeout: 1, autoApprove: false);
        var req = NewReq();
        q.Create(req);
        var result = await q.AwaitDecisionAsync("a1", TestContext.Current.CancellationToken);
        // AutoApprove off → reject + Expired
        result.Status.Should().Be(ApprovalStatus.Expired);
    }

    [Fact]
    public async Task AwaitDecisionAsync_TimeoutAutoApprove_StatusApproved()
    {
        var q = NewQueue(timeout: 1, autoApprove: true);
        q.Create(NewReq());
        var result = await q.AwaitDecisionAsync("a1", TestContext.Current.CancellationToken);
        result.Status.Should().Be(ApprovalStatus.Approved);
    }
}
