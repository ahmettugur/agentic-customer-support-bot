using CustomerSupportBot.Application.Ports.Outbound;

namespace CustomerSupportBot.Application.Ports.Inbound;

/// <summary>
/// Köprü modu — Realtime sadece STT/TTS, agent pipeline cevabı üretir.
/// </summary>
public interface IRealtimeBridge
{
    Task RunAsync(IBrowserChannel channel, string sessionId, CancellationToken ct);
}
