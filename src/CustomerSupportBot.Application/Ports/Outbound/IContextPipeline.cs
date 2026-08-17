using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Outbound;

/// <summary>
/// Context pipeline port'u — kayıtlı IContextProvider'ları çalıştırıp
/// birleştirilen bağlam metnini döner. Adapter'lar bu port üzerinden bağlam alır.
/// </summary>
public interface IContextPipeline
{
    /// <summary>
    /// Bağlamı kurar. Yalnızca birleşik metni değil, <b>hangi provider'ın katkı yaptığını</b>
    /// da döner — çağıranın buna göre karar vermesi gerekebiliyor (bkz. <see cref="ContextResult"/>).
    /// </summary>
    Task<ContextResult> BuildContextAsync(
        AgentSession session, string currentQuery, CancellationToken ct = default);
}
