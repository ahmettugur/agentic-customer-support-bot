using CustomerSupportBot.Application.Ports.Driven;

namespace CustomerSupportBot.Application.Ports.Driving;

/// <summary>
/// Köprü modu — Realtime sadece STT/TTS, agent pipeline cevabı üretir.
/// </summary>
public interface IRealtimeBridge
{
    Task RunAsync(IBrowserChannel channel, string sessionId, CancellationToken ct);
}
