using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Driven;

public interface IContextProvider
{
    string Name { get; }
    int Order { get; }
    Task<string?> GetContextAsync(AgentSession session);
}
