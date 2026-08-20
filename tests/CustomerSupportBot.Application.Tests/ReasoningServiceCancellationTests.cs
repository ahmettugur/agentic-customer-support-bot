using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Reasoning;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Tests;

public class ReasoningServiceCancellationTests
{
    private sealed class CancellingReasoningClient : IReasoningChatClient
    {
        public string ModelName => "test";
        public string ReasoningEffort => "low";

        public Task<string> CompleteAsync(
            IReadOnlyList<ConversationMessage> messages,
            CancellationToken ct = default) => Task.FromCanceled<string>(ct);

        public async IAsyncEnumerable<string> StreamAsync(
            IReadOnlyList<ConversationMessage> messages,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            yield break;
        }
    }

    private static ReasoningService Build()
    {
        var prompts = Substitute.For<IPromptRepository>();
        prompts.Render(Arg.Any<string>(), Arg.Any<IDictionary<string, string?>?>())
            .Returns("system prompt");
        prompts.Get(Arg.Any<string>()).Returns("");

        return new ReasoningService(
            new CancellingReasoningClient(),
            NullLogger<ReasoningService>.Instance,
            prompts,
            new EntityVerifier(
                Substitute.For<IOrderRepository>(),
                Substitute.For<IComplaintRepository>(),
                NullLogger<EntityVerifier>.Instance),
            new ReasoningSanityChecker(NullLogger<ReasoningSanityChecker>.Instance),
            Options.Create(new WorkflowGuardOptions()));
    }

    [Fact]
    public async Task ReasonAsync_CallerCancellation_IsNotConvertedToFallback()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => Build().ReasonAsync(
            "merhaba", new AgentSession(), null, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ReasonStreamingAsync_CallerCancellation_IsNotConvertedToIncompleteResult()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        async Task EnumerateAsync()
        {
            await foreach (var _ in Build().ReasonStreamingAsync(
                "merhaba", new AgentSession(), null, cts.Token))
            {
            }
        }

        Func<Task> act = EnumerateAsync;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
