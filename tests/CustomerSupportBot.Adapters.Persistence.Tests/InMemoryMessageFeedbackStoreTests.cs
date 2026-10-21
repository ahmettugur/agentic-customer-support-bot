using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

public class InMemoryMessageFeedbackStoreTests : MessageFeedbackStoreContract
{
    protected override IMessageFeedbackStore Create() => new InMemoryMessageFeedbackStore();
}
