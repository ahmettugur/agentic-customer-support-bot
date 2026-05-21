using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Services.Providers;

public interface IContextProvider
{
    string Name { get; }
    int Order { get; }
    Task<string?> GetContextAsync(AgentSession session);
}
