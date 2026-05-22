using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Driven;

/// <summary>
/// Context pipeline port'u — kayıtlı IContextProvider'ları çalıştırıp
/// birleştirilen bağlam metnini döner. Adapter'lar bu port üzerinden bağlam alır.
/// </summary>
public interface IContextPipeline
{
    Task<string> BuildContextAsync(AgentSession session);
}
