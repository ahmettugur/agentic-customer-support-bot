using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Driven.Persistence;

/// <summary>
/// Eskalasyon kayıtları için secondary port.
///</summary>
public interface IEscalationSink
{
    EscalationRequest Create(EscalationRequest request);
    IReadOnlyList<EscalationRequest> GetOpen();
    IReadOnlyList<EscalationRequest> GetRecent(int count = 50);
    EscalationRequest? Get(string id);
    bool Decide(string id, string action, string? assignedTo = null, string? resolution = null);

    event EventHandler<EscalationRequest>? RequestCreated;
    event EventHandler<EscalationRequest>? RequestDecided;
}
