using System.Net.WebSockets;

namespace CustomerSupportBot.Adapters.AI.Realtime;

/// <summary>
/// Native mod — gpt-realtime-2 kendisi konuşur, okuma-only tool'ları çağırır.
/// </summary>
public interface IRealtimeNativeBridge
{
    Task RunAsync(WebSocket browserWs, string sessionId, CancellationToken ct);
}
