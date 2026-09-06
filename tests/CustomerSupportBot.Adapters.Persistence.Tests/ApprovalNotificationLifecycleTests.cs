using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

public class ApprovalNotificationLifecycleTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReadingRunningRequest_DoesNotConsumeTerminalNotification(bool success)
    {
        var completion = new TaskCompletionSource<ApprovalExecutionOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var router = Substitute.For<IApprovalExecutionRouter>();
        router.ExecuteAsync(Arg.Any<ApprovalRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => { started.SetResult(); return completion.Task; });
        var queue = new InMemoryApprovalQueue(Options.Create(new ApprovalOptions()), router, NullLogger<InMemoryApprovalQueue>.Instance);
        var request = await queue.CreateAsync(new ApprovalRequest { SessionId = "s", CustomerId = "1001", ToolName = "test" });
        var decision = queue.DecideAsync(request.Id, true);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            (await queue.GetUnseenForSessionAsync("s", "1001")).Should().BeEmpty();
            (await queue.MarkSeenAsync(request.Id)).Should().BeFalse();
            request.CustomerSeenAt.Should().BeNull();
        }
        finally { completion.TrySetResult(new ApprovalExecutionOutcome(success, "terminal result")); }
        await decision;
        var unseen = await queue.GetUnseenForSessionAsync("s", "1001");
        unseen.Should().ContainSingle().Which.ExecutionStatus.Should().Be(success
            ? ApprovalExecutionStatus.Succeeded : ApprovalExecutionStatus.Failed);
        (await queue.MarkSeenAsync(request.Id)).Should().BeTrue();
        (await queue.GetUnseenForSessionAsync("s", "1001")).Should().BeEmpty();
        (await queue.GetHistoryForCustomerAsync("1001")).Should().ContainSingle();
    }
}
