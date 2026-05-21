// Ports/Driving/IRealtimeNativeBridge.cs
// PRIMARY PORT — Native modda OpenAI Realtime köprüsü (model kendisi konuşur).
// Driving adapter (RealtimeEndpoints) bunu enjekte eder; uygulama Adapters.AI'da.

using System.Net.WebSockets;

namespace CustomerSupportBot.Application.Ports.Driving;

/// <summary>
/// Native mod — gpt-realtime-2 kendisi konuşur, okuma-only tool'ları çağırır.
/// </summary>
public interface IRealtimeNativeBridge
{
    Task RunAsync(WebSocket browserWs, string sessionId, CancellationToken ct);
}
