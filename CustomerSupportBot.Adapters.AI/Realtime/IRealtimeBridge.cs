using System.Net.WebSockets;

namespace CustomerSupportBot.Adapters.AI.Realtime;

/// <summary>
/// Köprü modu — Realtime sadece STT/TTS, agent pipeline cevabı üretir.
/// </summary>
public interface IRealtimeBridge
{
    Task RunAsync(WebSocket browserWs, string sessionId, CancellationToken ct);
}
