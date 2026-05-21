// Ports/Driving/IRealtimeBridge.cs
// PRIMARY PORT — Tarayıcı WebSocket ↔ OpenAI Realtime köprüsü (köprü modu).
// Driving adapter (RealtimeEndpoints) bunu enjekte eder; uygulama Adapters.AI'da.

using System.Net.WebSockets;

namespace CustomerSupportBot.Application.Ports.Driving;

/// <summary>
/// Köprü modu — Realtime sadece STT/TTS, agent pipeline cevabı üretir.
/// </summary>
public interface IRealtimeBridge
{
    Task RunAsync(WebSocket browserWs, string sessionId, CancellationToken ct);
}
