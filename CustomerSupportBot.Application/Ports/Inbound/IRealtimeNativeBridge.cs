using CustomerSupportBot.Application.Ports.Outbound;

namespace CustomerSupportBot.Application.Ports.Inbound;

/// <summary>
/// Native mod — gpt-realtime-2 kendisi konuşur, okuma-only tool'ları çağırır.
/// </summary>
public interface IRealtimeNativeBridge
{
    Task RunAsync(IBrowserChannel channel, string sessionId, CancellationToken ct);
}
