// Ports/Driving/IRealtimeBridge.cs
// PRIMARY PORT — Tarayıcı WebSocket ↔ agent pipeline köprüsü (köprü modu).
// Driving adapter (RealtimeEndpoints) bunu enjekte eder.
// Implementasyon: Application/Services/RealtimeBridgeService (hexagonal: core'da).
// OpenAI Realtime transport IOpenAiRealtimeClient secondary port'u arkasına taşındı.

using System.Net.WebSockets;

namespace CustomerSupportBot.Application.Ports.Driving;

/// <summary>
/// Köprü modu — Realtime sadece STT/TTS, agent pipeline cevabı üretir.
/// </summary>
public interface IRealtimeBridge
{
    Task RunAsync(WebSocket browserWs, string sessionId, CancellationToken ct);
}
