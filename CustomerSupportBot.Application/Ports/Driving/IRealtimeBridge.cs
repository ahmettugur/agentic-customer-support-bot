// Ports/Driving/IRealtimeBridge.cs
// PRIMARY PORT — Tarayıcı kanalı ↔ agent pipeline köprüsü (köprü modu).
// Driving adapter (RealtimeEndpoints) WebSocket'i IBrowserChannel olarak sarmalar ve bu port'u çağırır.
// Implementasyon: Application/Services/RealtimeBridgeService (hexagonal: core'da).

using CustomerSupportBot.Application.Ports.Driven;

namespace CustomerSupportBot.Application.Ports.Driving;

/// <summary>
/// Köprü modu — Realtime sadece STT/TTS, agent pipeline cevabı üretir.
/// </summary>
public interface IRealtimeBridge
{
    Task RunAsync(IBrowserChannel channel, string sessionId, CancellationToken ct);
}
