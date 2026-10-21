using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Application.Services.Approval;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Adapters.Agents.Tests;

public class WorkflowTraceEventProcessorTests
{
    [Fact]
    public async Task StartTraceState_BindsCreatedTraceToAmbientApprovalContext()
    {
        var accessor = new ApprovalContextAccessor();
        using var scope = accessor.SetScope("session-1", null, "siparişi iptal et", "1001");
        var processor = new WorkflowTraceEventProcessor(
            new InMemoryReasoningTraceStore(), accessor);

        var state = await processor.StartTraceStateAsync(
            new AgentSession { SessionId = "session-1" }, "siparişi iptal et", null);
        processor.BindToAmbientContext(state);

        accessor.Context!.TraceId.Should().Be(state.Trace.TraceId);
        accessor.Context.SessionId.Should().Be("session-1");
        accessor.Context.CustomerId.Should().Be("1001");
    }

    [Fact]
    public async Task StartTraceState_StampsThePromptSetVersion_OnTheTrace()
    {
        var prompts = Substitute.For<CustomerSupportBot.Application.Ports.Outbound.IPromptRepository>();
        prompts.SetVersion.Returns("a1b2c3d4");
        var processor = new WorkflowTraceEventProcessor(
            new InMemoryReasoningTraceStore(), new ApprovalContextAccessor(), prompts);

        var state = await processor.StartTraceStateAsync(new AgentSession { SessionId = "s" }, "merhaba", null);

        state.Trace.PromptVersion.Should().Be("a1b2c3d4");
    }

    [Fact]
    public async Task StartTraceState_WithoutPromptRepository_LeavesVersionNull()
    {
        var processor = new WorkflowTraceEventProcessor(new InMemoryReasoningTraceStore(), new ApprovalContextAccessor());

        var state = await processor.StartTraceStateAsync(new AgentSession { SessionId = "s" }, "merhaba", null);

        state.Trace.PromptVersion.Should().BeNull();
    }
}
