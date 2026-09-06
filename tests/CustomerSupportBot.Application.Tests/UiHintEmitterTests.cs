using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Services.Approval;
using CustomerSupportBot.Application.Services.UiHint;

namespace CustomerSupportBot.Application.Tests;

public class UiHintEmitterTests
{
    [Fact]
    public async Task ClosedTurn_RejectsLateProducer_AndDoesNotPolluteNextTurn()
    {
        var context = new ApprovalContextAccessor();
        var emitter = new UiHintEmitter(context);
        using var scope = context.SetScope("s", null, "query", "1001");
        emitter.Emit(new StreamEvent("test", null)).Should().BeFalse();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool> late;
        using (emitter.BeginTurn("s"))
        {
            emitter.Emit(new StreamEvent("old", null)).Should().BeTrue();
            late = Task.Run(async () =>
            {
                await release.Task;
                return emitter.Emit(new StreamEvent("late", null));
            });
        }
        using (emitter.BeginTurn("s"))
        {
            release.SetResult();
            (await late).Should().BeFalse();
            emitter.DrainPending("s").Should().BeEmpty();
        }
    }

    [Fact]
    public async Task ParallelProducers_AndDrain_DoNotLoseHints()
    {
        var context = new ApprovalContextAccessor();
        var emitter = new UiHintEmitter(context);
        using var scope = context.SetScope("s", null, "query");
        using var turn = emitter.BeginTurn("s");
        var producers = Enumerable.Range(0, 100).Select(i => Task.Run(() =>
            emitter.Emit(new StreamEvent("hint", i)))).ToArray();
        var received = new List<StreamEvent>();
        while (producers.Any(t => !t.IsCompleted))
        {
            received.AddRange(emitter.DrainPending("s"));
            await Task.Yield();
        }
        (await Task.WhenAll(producers)).Should().OnlyContain(x => x);
        received.AddRange(emitter.DrainPending("s"));
        received.Should().HaveCount(100);
        received.Select(e => e.Data).Distinct().Should().HaveCount(100);
    }
}
