// Application/Services/Providers/NoopContextProvider.cs
// Semantic memory disabled olduğunda DI'a koyulan no-op fallback.

using CustomerSupportBot.Application.Ports.Driven;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Services.Providers;

public sealed class NoopContextProvider : IContextProvider
{
    public string Name => "Noop";
    public int Order => int.MaxValue;
    public Task<string?> GetContextAsync(AgentSession session) => Task.FromResult<string?>(null);
}
