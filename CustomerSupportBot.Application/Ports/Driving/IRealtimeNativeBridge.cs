// Ports/Driving/IRealtimeNativeBridge.cs
// PRIMARY PORT — Native modda OpenAI Realtime oturumu (model kendisi konuşur).
// Driving adapter (RealtimeEndpoints) WebSocket'i IBrowserChannel olarak sarmalar ve bu port'u çağırır.
// Implementasyon: Application/Services/RealtimeNativeService (hexagonal: core'da).

using CustomerSupportBot.Application.Ports.Driven;

namespace CustomerSupportBot.Application.Ports.Driving;

/// <summary>
/// Native mod — gpt-realtime-2 kendisi konuşur, okuma-only tool'ları çağırır.
/// </summary>
public interface IRealtimeNativeBridge
{
    Task RunAsync(IBrowserChannel channel, string sessionId, CancellationToken ct);
}
