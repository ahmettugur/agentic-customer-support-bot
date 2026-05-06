// Services/NoopContextProvider.cs
// Semantic memory disabled olduğunda DI'a koyulan no-op fallback.

using CustomerSupportBot.Models;

namespace CustomerSupportBot.Services;

internal sealed class NoopContextProvider : IContextProvider
{
    public string Name => "Noop";
    public int Order => int.MaxValue;
    public Task<string?> GetContextAsync(AgentSession session) => Task.FromResult<string?>(null);
}
