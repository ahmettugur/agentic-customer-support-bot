using CustomerSupportBot.Application.Ports.Outbound;

namespace CustomerSupportBot.Application.Ports.Inbound;

/// <summary>
/// Sesli görüşme — gpt-realtime modeli kendisi konuşur ve tool'ları çağırır; yan etkili
/// işlemler insan onayına gönderilir.
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
