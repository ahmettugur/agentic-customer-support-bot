using CustomerSupportBot.Application.Ports.Outbound;

namespace CustomerSupportBot.Application.Ports.Inbound;

/// <summary>
/// Köprü modu — Realtime sadece STT/TTS, agent pipeline cevabı üretir.
/// </summary>
public interface IRealtimeBridge
{
    /// <param name="authenticatedCustomerId">
    /// Login'li müşterinin JWT claim'inden gelen kimliği. Oturuma bir kez bağlanır ve
    /// sipariş tool'ları bunu kullanır — bu değer olmadan her sipariş sorgusu sahiplik
    /// kontrolüne takılıp "bulunamadı" döner.
    /// </param>
    Task RunAsync(IBrowserChannel channel, string sessionId, string? authenticatedCustomerId, CancellationToken ct);
}
