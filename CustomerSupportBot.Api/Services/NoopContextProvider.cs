// Services/NoopContextProvider.cs
// Semantic memory disabled olduğunda DI'a koyulan no-op fallback.

using CustomerSupportBot.Api.Models;

namespace CustomerSupportBot.Api.Services;

internal sealed class NoopContextProvider : IContextProvider
{
    public string Name => "Noop";
    public int Order => int.MaxValue;
    public Task<string?> GetContextAsync(AgentSession session) => Task.FromResult<string?>(null);
}
