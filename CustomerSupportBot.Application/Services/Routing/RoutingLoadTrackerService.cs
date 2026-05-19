// Application/Services/Routing/RoutingLoadTrackerService.cs
// Eskalasyon kapanınca (resolve/dismiss) atanan temsilcinin CurrentLoad'unu
// otomatik olarak azaltan event subscriber. IHostedService olarak çalışır.

using CustomerSupportBot.Application.Ports.Driven.Persistence;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Application.Services.Routing;

/// <summary>
/// Smart Routing — eskalasyon kapandığında atanan temsilcinin load'unu azaltır.
/// </summary>
public sealed class RoutingLoadTrackerService : IHostedService
{
    private readonly IEscalationRepository _escalationSink;
    private readonly IHumanAgentRepository _agentRegistry;
    private readonly ILogger<RoutingLoadTrackerService> _logger;

    private EventHandler<EscalationRequest>? _handler;

    public RoutingLoadTrackerService(
        IEscalationRepository escalationSink,
        IHumanAgentRepository agentRegistry,
        ILogger<RoutingLoadTrackerService> logger)
    {
        _escalationSink = escalationSink;
        _agentRegistry = agentRegistry;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _handler = (_, esc) =>
        {
            // Sadece kapanmış (Resolved/Dismissed) eskalasyonlar için load azalt
            if (esc.Status is EscalationStatus.Resolved or EscalationStatus.Dismissed
                && !string.IsNullOrWhiteSpace(esc.SuggestedAgentId))
            {
                _agentRegistry.DecrementLoad(esc.SuggestedAgentId);
                _logger.LogDebug(
                    "RoutingLoadTracker: {AgentId} load decremented (escalation {EscId} {Status})",
                    esc.SuggestedAgentId, esc.Id, esc.Status);
            }
        };

        _escalationSink.RequestDecided += _handler;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_handler != null)
            _escalationSink.RequestDecided -= _handler;

        return Task.CompletedTask;
    }
}
