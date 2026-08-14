using CustomerSupportBot.Application.Ports.Outbound;

namespace CustomerSupportBot.Application.Ports.Inbound;

/// <summary>
/// Native mod — gpt-realtime-2 kendisi konuşur, okuma-only tool'ları çağırır.
/// </summary>
public interface IRealtimeNativeBridge
{
    /// <param name="authenticatedCustomerId">
    /// Login'li müşterinin JWT claim'inden gelen kimliği. Oturuma bir kez bağlanır ve
    /// sipariş tool'ları bunu kullanır — bu değer olmadan her sipariş sorgusu sahiplik
    /// kontrolüne takılıp "bulunamadı" döner.
    /// </param>
    Task RunAsync(IBrowserChannel channel, string sessionId, string? authenticatedCustomerId, CancellationToken ct);
}
