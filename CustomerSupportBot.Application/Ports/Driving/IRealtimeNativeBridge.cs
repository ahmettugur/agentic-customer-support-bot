using CustomerSupportBot.Application.Ports.Driven;

namespace CustomerSupportBot.Application.Ports.Driving;

/// <summary>
/// Native mod — gpt-realtime-2 kendisi konuşur, okuma-only tool'ları çağırır.
/// </summary>
public interface IRealtimeNativeBridge
{
    Task RunAsync(IBrowserChannel channel, string sessionId, CancellationToken ct);
}
