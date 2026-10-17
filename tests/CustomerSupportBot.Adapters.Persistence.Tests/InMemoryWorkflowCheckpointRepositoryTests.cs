using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

public class InMemoryWorkflowCheckpointRepositoryTests : WorkflowCheckpointRepositoryContract
{
    protected override IWorkflowCheckpointRepository Create() => new InMemoryWorkflowCheckpointRepository();
}
