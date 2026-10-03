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
}
