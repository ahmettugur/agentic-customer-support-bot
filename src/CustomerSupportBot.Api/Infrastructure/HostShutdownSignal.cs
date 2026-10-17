// Infrastructure/HostShutdownSignal.cs
// IShutdownSignal → ASP.NET host yaşam döngüsü: SIGTERM/rolling update ile durdurma başladı mı.

using CustomerSupportBot.Application.Ports.Outbound;

namespace CustomerSupportBot.Api.Infrastructure;

public sealed class HostShutdownSignal(IHostApplicationLifetime lifetime) : IShutdownSignal
{
    public bool IsStopping => lifetime.ApplicationStopping.IsCancellationRequested;
}
